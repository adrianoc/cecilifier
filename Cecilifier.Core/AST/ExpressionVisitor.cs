using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    class ExpressionVisitor : SyntaxWalkerBase
    {
		internal static bool Visit(IVisitorContext ctx, string ilVar, SyntaxNode node)
		{
			if (node == null) return true;

			var ev = new ExpressionVisitor(ctx, ilVar);
			ev.Visit(node);

			return ev.valueTypeNoArgObjCreation;
		}

    	private ExpressionVisitor(IVisitorContext ctx, string ilVar) : base(ctx)
        {
        	this.ilVar = ilVar;
        }

	    public override void VisitEqualsValueClause(EqualsValueClauseSyntax node)
		{
			base.VisitEqualsValueClause(node);
			InjectRequiredConversions(node.Value);
		}

	    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
	    {
			var visitor = new AssignmentVisitor(Context, ilVar);
			visitor.PreProcessRefOutAssignments(node.Left);

			Visit(node.Right);
			if (!valueTypeNoArgObjCreation)
			{
				visitor.Visit(node.Left);
			}
		}

		public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
			Visit(node.Left);
			InjectRequiredConversions(node.Left);

			Visit(node.Right);
			InjectRequiredConversions(node.Right);

			var handler = OperatorHandlerFor(node.OperatorToken);
			handler(
				Context, 
				ilVar, 
				Context.SemanticModel.GetTypeInfo(node.Left).Type,
				Context.SemanticModel.GetTypeInfo(node.Right).Type);
        }

	    public override void VisitLiteralExpression(LiteralExpressionSyntax node)
        {
			switch(node.Kind())
			{
				case SyntaxKind.StringLiteralExpression:
					AddCilInstruction(ilVar, OpCodes.Ldstr, node.ToFullString());
					break;

				case SyntaxKind.CharacterLiteralExpression:
				case SyntaxKind.NumericLiteralExpression:
				    AddLocalVariableAndHandleCallOnValueTypeLiterals(node, "assembly.MainModule.TypeSystem.Int32", node.ToString());
					break;

			    case SyntaxKind.TrueLiteralExpression:
			    case SyntaxKind.FalseLiteralExpression:
			        AddLocalVariableAndHandleCallOnValueTypeLiterals(node, "assembly.MainModule.TypeSystem.Boolean", Boolean.Parse(node.ToString()) ? 1 :0);
			        break;

                default:
					throw new ArgumentException($"Literal ( {node}) of type {node.Kind()} not supported yet.");
			}

            void AddLocalVariableAndHandleCallOnValueTypeLiterals(LiteralExpressionSyntax literalNode, string cecilTypeSystemReference, object literalValue)
            {
                AddCilInstruction(ilVar, LoadOpCodeFor(literalNode), literalValue);
                var localVarParent = (CSharpSyntaxNode) literalNode.Parent;
                if (localVarParent.Accept(new UsageVisitor()) == UsageKind.CallTarget)
                {
                    var tempLocalName = MethodExtensions.LocalVariableNameFor("tmp_", "tmp_".UniqueId().ToString());
                    AddCecilExpression("var {0} = new VariableDefinition({1});", tempLocalName, cecilTypeSystemReference);
                    AddCecilExpression("{0}.Body.Variables.Add({1});", Context.CurrentLocalVariable.VarName, tempLocalName);

                    AddCilInstruction(ilVar, OpCodes.Stloc, $"{tempLocalName}");
                    AddCilInstruction(ilVar, OpCodes.Ldloca_S, $"{tempLocalName}");
                }
            }
		}


        /*
		 *            +--> ArgumentList
		 *            |
		 *       /---------\
		 * n.DoIt(10 + x, y);
		 * \----/
		 *	 |
		 *	 +---> Expression: MemberAccessExpression
		 * 
		 * We do not have a natural order to visit the expression and the argument list:
		 * 
		 * - If we visit in the order: arguments, expression (which would be the natural order)
		 *			push 10
		 *			push x
		 *			add
		 *			push y
		 *			push n <-- Should be the first
		 *			Call DoIt(Int32, Int32)
		 * 
		 * - If we visit in the order: expression, arguments
		 *			push n 
		 *			Call DoIt(Int32, Int32) <--+
		 *			push 10                    |
		 *			push x                     |  Should be here
		 *			add                        |
		 *			push y                     |
		 *			         <-----------------+
		 * 
		 * To fix this we visit in the order [exp, args] and move the call operation after visiting the arguments
		 */
        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
	    {
		    Visit(node.Expression);
			PushCall();

			Visit(node.ArgumentList);
			FixCallSite();
        }

	    public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
    	{
    		Func<OpCode, string> emit = op =>
			{
    			var instVarName = op.Name + op.Name.UniqueId();
				AddCecilExpression(@"var {0} = {1}.Create({2});", instVarName, ilVar, op.ConstantName());

				return instVarName;
			};

    		var conditionEnd = emit(OpCodes.Nop);
			var whenFalse = emit(OpCodes.Nop);
			
			Visit(node.Condition);
			AddCilInstruction(ilVar, OpCodes.Brfalse_S, whenFalse);

			Visit(node.WhenTrue);
			AddCilInstruction(ilVar, OpCodes.Br_S, conditionEnd);

			AddCecilExpression("{0}.Append({1});", ilVar, whenFalse);
			Visit(node.WhenFalse);

			AddCecilExpression("{0}.Append({1});", ilVar, conditionEnd);
        }

	    public override void VisitIdentifierName(IdentifierNameSyntax node)
	    {
		    var member = Context.SemanticModel.GetSymbolInfo(node);

		    switch (member.Symbol.Kind)
		    {
			    case SymbolKind.Method:
				    ProcessMethodCall(node, member.Symbol as IMethodSymbol);
				    break;

			    case SymbolKind.Parameter:
				    ProcessParameter(ilVar, node, member.Symbol as IParameterSymbol);
				    break;

			    case SymbolKind.Local:
				    ProcessLocalVariable(node, member);
				    break;

			    case SymbolKind.Property:
				    ProcessProperty(node, member.Symbol as IPropertySymbol);
				    break;
		    }
	    }

	    public override void VisitArgument(ArgumentSyntax node)
		{
			base.VisitArgument(node);
			InjectRequiredConversions(node.Expression);
		}

	    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax exp)
        {
			Visit(exp.Expression);
        	Visit(exp.Name);
        }

	    public override void VisitThisExpression(ThisExpressionSyntax node)
		{
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

	    public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
        }

	    public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

		public override void VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
		{
			base.VisitParenthesizedExpression(node);

			var localVarParent = (CSharpSyntaxNode) node.Parent;
			if (localVarParent.Accept(new UsageVisitor()) == UsageKind.CallTarget)
			{
				var tempLocalName = MethodExtensions.LocalVariableNameFor("tmp_", "tmp_".UniqueId().ToString());
				AddCecilExpression("var {0} = new VariableDefinition(assembly.MainModule.TypeSystem.Int32);", tempLocalName);
				AddCecilExpression("{0}.Body.Variables.Add({1});", Context.CurrentLocalVariable.VarName, tempLocalName);

				AddCilInstruction(ilVar, OpCodes.Stloc, tempLocalName);
				AddCilInstruction(ilVar, OpCodes.Ldloca_S, tempLocalName);
			}
		}

		public override void VisitMakeRefExpression(MakeRefExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

	    public override void VisitRefTypeExpression(RefTypeExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

	    public override void VisitRefValueExpression(RefValueExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

	    public override void VisitCheckedExpression(CheckedExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

	    public override void VisitDefaultExpression(DefaultExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

	    public override void VisitTypeOfExpression(TypeOfExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

	    public override void VisitSizeOfExpression(SizeOfExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

	    public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

	    public override void VisitCastExpression(CastExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

	    public override void VisitInitializerExpression(InitializerExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

	    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
		{
			//TODO: Refactor to reuse code from VisitIdentifierName....
			var ctorInfo = Context.SemanticModel.GetSymbolInfo(node);

			var method = (IMethodSymbol) ctorInfo.Symbol;
			if (TryProcessNoArgsCtorInvocationOnValueType(node, method, ctorInfo)) return;

			EnsureMethodAvailable(method);

			AddCilInstruction(ilVar, OpCodes.Newobj, method.MethodResolverExpression(Context));
			PushCall();

			Visit(node.ArgumentList);
			FixCallSite();
		}

    	private bool TryProcessNoArgsCtorInvocationOnValueType(ObjectCreationExpressionSyntax node, IMethodSymbol methodSymbol, SymbolInfo ctorInfo)
    	{
    		if (ctorInfo.Symbol.ContainingType.IsReferenceType || methodSymbol.Parameters.Length > 0)
    		{
    			return false;
    		}

    		new ValueTypeNoArgCtorInvocationVisitor(Context, ilVar, ctorInfo).Visit(node.Parent);
			return valueTypeNoArgObjCreation = true;
    	}

    	private bool ConsumesStack(SyntaxNode node)
    	{
    		return new StackTransitionAnalizer(node).ConsumesStack();
    	}

	    public override void VisitExpressionStatement(ExpressionStatementSyntax node)
        {
			Context.WriteCecilExpression($"\r\n// {node}\r\n");
			base.Visit(node.Expression);

			var info = Context.GetTypeInfo(node.Expression);
			if (node.Expression.Kind() != SyntaxKind.SimpleAssignmentExpression &&  info.Type.SpecialType != SpecialType.System_Void)
			{
				AddCilInstruction(ilVar, OpCodes.Pop);
			}
        }

    	private void WriteLine(string msg, params object[] args)
    	{
    		Console.WriteLine(msg, args);
    	}

		private OpCode LoadOpCodeFor(LiteralExpressionSyntax node)
		{
			var info = Context.SemanticModel.GetTypeInfo(node);
			switch (info.Type.SpecialType)
			{
				case SpecialType.System_Single:
					return OpCodes.Ldc_R4;

				case SpecialType.System_Double:
					return OpCodes.Ldc_R8;
				
				case SpecialType.System_Int16:
				case SpecialType.System_Int32:
					return OpCodes.Ldc_I4;

				case SpecialType.System_Int64:
					return OpCodes.Ldc_I8;

				case SpecialType.System_Char:
					return OpCodes.Ldc_I4;

                case SpecialType.System_Boolean:
                    return OpCodes.Ldc_I4;
			}

			throw new ArgumentException(string.Format("Literal type {0} not supported.", info.Type), "node");
		}

		private void EnsureMethodAvailable(IMethodSymbol method)
		{
			if (!method.IsDefinedInCurrentType(Context)) return;

			var varName = method.LocalVariableName();
			if (Context.Contains(varName)) return;

			//TODO: Try to reuse SyntaxWalkerBase.ResolveType(TypeSyntax)
			var returnType = ResolveTypeLocalVariable(method.ReturnType.Name) ?? ResolvePredefinedType(method.ReturnType);
			MethodDeclarationVisitor.AddMethodDefinition(Context, varName, method.Name, "MethodAttributes.Private", returnType);
		}

		private void FixCallSite()
		{
			Context.MoveLineAfter(callFixList.Pop(), Context.CurrentLine);
		}

		private void PushCall()
		{
			callFixList.Push(Context.CurrentLine);
		}

		private void ProcessProperty(IdentifierNameSyntax node, IPropertySymbol propertySymbol)
		{
			var parentExp = (CSharpSyntaxNode) node.Parent;

			if (parentExp.Kind() == SyntaxKind.SimpleAssignmentExpression)
			{
				AddMethodCall(ilVar, propertySymbol.SetMethod);
			}
			else
			{
				AddMethodCall(ilVar, propertySymbol.GetMethod);
			}
		}

		private void ProcessLocalVariable(IdentifierNameSyntax localVar, SymbolInfo varInfo)
		{
			var symbol = (ILocalSymbol) varInfo.Symbol;
			var localVarParent = (CSharpSyntaxNode) localVar.Parent;
			if (symbol.Type.IsValueType && localVarParent.Accept(new UsageVisitor()) == UsageKind.CallTarget)
			{
				AddCilInstruction(ilVar, OpCodes.Ldloca_S, Context.MapLocalVariableNameToCecil(symbol.Name));
				return;
			}

			AddCilInstruction(ilVar, OpCodes.Ldloc, Context.MapLocalVariableNameToCecil(symbol.Name));
		}

		private void ProcessMethodCall(IdentifierNameSyntax node, IMethodSymbol method)
		{
			if (!method.IsStatic && method.IsDefinedInCurrentType(Context) && node.Parent.Kind() == SyntaxKind.InvocationExpression)
			{
				AddCilInstruction(ilVar, OpCodes.Ldarg_0);
			}

			EnsureMethodAvailable(method);
			AddMethodCall(ilVar, method);
		}

		private void InjectRequiredConversions(ExpressionSyntax expression)
	    {
		    var typeInfo = Context.SemanticModel.GetTypeInfo(expression);

		    var conversion = Context.SemanticModel.GetConversion(expression);
			//TODO: 
		    if (conversion.IsImplicit && conversion.IsNumeric)
		    {
			    switch (typeInfo.ConvertedType.SpecialType)
			    {
				    case SpecialType.System_Single:
					    AddCilInstruction(ilVar, OpCodes.Conv_R4);
					    return;
				    case SpecialType.System_Double:
					    AddCilInstruction(ilVar, OpCodes.Conv_R8);
					    return;

				    case SpecialType.System_Byte:
					    AddCilInstruction(ilVar, OpCodes.Conv_I1);
					    return;
				    case SpecialType.System_Int16:
					    AddCilInstruction(ilVar, OpCodes.Conv_I2);
					    return;
				    case SpecialType.System_Int32:
					    AddCilInstruction(ilVar, OpCodes.Conv_I4);
					    return;
				    case SpecialType.System_Int64:
					    AddCilInstruction(ilVar, OpCodes.Conv_I8);
					    return;

				    default:
					    throw new Exception(string.Format("Conversion from {0} to {1}  not implemented.", typeInfo.Type, typeInfo.ConvertedType));
			    }
		    }

		    if (conversion.IsImplicit && conversion.IsBoxing)
		    {
			    AddCilInstruction(ilVar, OpCodes.Box, typeInfo.Type);
		    }
	    }
		
		private Action<IVisitorContext, string, ITypeSymbol, ITypeSymbol> OperatorHandlerFor(SyntaxToken operatorToken)
		{
			if (operatorHandlers.ContainsKey(operatorToken.Kind()))
			{
				return operatorHandlers[operatorToken.Kind()];
			}

			throw new Exception(string.Format("Operator {0} not supported yet (expression: {1})", operatorToken.ValueText, operatorToken.Parent));
		}

		static ExpressionVisitor()
		{
			//TODO: Use AddCilInstruction instead.
			operatorHandlers[SyntaxKind.PlusToken] = (ctx, ilVar, left, right) =>
			{
				if (left.SpecialType == SpecialType.System_String)
				{
					WriteCecilExpression(ctx, "{0}.Append({0}.Create({1}, assembly.MainModule.Import(typeof(string).GetMethod(\"Concat\", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public, null, new[] {{ typeof(Object), typeof(Object) }}, null))));", ilVar, OpCodes.Call.ConstantName());
				}
				else
				{
					WriteCecilExpression(ctx, @"{0}.Append({0}.Create({1}));", ilVar, OpCodes.Add.ConstantName());
				}
			};
			
			operatorHandlers[SyntaxKind.SlashToken] = (ctx, ilVar, left, right) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.Div.ConstantName()}));");
		    operatorHandlers[SyntaxKind.GreaterThanToken] = (ctx, ilVar, left, right) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.Cgt.ConstantName()}));");
		    operatorHandlers[SyntaxKind.EqualsEqualsToken] = (ctx, ilVar, left, right) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.Ceq.ConstantName()}));");
		    operatorHandlers[SyntaxKind.LessThanToken] = (ctx, ilVar, left, right) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.Clt.ConstantName()}));");
			operatorHandlers[SyntaxKind.MinusToken] = (ctx, ilVar, left, right) =>  WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.Sub.ConstantName()}));");
		}

    	private bool valueTypeNoArgObjCreation;
		private readonly string ilVar;
    	private Stack<LinkedListNode<string>> callFixList = new Stack<LinkedListNode<string>>();
    	
		private static Action<IVisitorContext, string> NoOpInstance = delegate { };
		private static IDictionary<SyntaxKind, Action<IVisitorContext, string, ITypeSymbol, ITypeSymbol>> operatorHandlers = new Dictionary<SyntaxKind, Action<IVisitorContext, string, ITypeSymbol, ITypeSymbol>>();
    }

	internal interface IOperatorHandler
	{
	}
}
