using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cecilifier.Core.Extensions;
using Mono.Cecil.Cil;
using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.AST
{
    class ExpressionVisitor : SyntaxWalkerBase
    {
		internal static bool Visit(IVisitorContext ctx, string ilVar, SyntaxNode node)
		{
			if (node == null) return false;

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

	    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
			if (node.Kind == SyntaxKind.AssignExpression)
			{
				ProcessAssignmentExpression(node);
			}
			else
			{
				ProcessBinaryExpression(node);
			}
        }

    	private void ProcessBinaryExpression(BinaryExpressionSyntax node)
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

    	private void ProcessAssignmentExpression(BinaryExpressionSyntax node)
    	{
			Visit(node.Right);
    		if (!valueTypeNoArgObjCreation)
    		{
    			new AssignmentVisitor(Context, ilVar).Visit(node.Left);
    		}
    	}

	    public override void VisitLiteralExpression(LiteralExpressionSyntax node)
        {
			switch(node.Kind)
			{
				case SyntaxKind.StringLiteralExpression:
					AddCilInstruction(ilVar, OpCodes.Ldstr, node.ToFullString());
					break;

				case SyntaxKind.NumericLiteralExpression:
					AddCilInstruction(ilVar, LoadOpCodeFor(node), node.ToString());
					break;
				
				default:
					throw new ArgumentException("Literal of type " + node + " not supported yet.");
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
					ProcessMethodCall(node, member.Symbol as MethodSymbol);
					break;

				case SymbolKind.Parameter:
					ProcessParameter(node, member);
					break;

				case SymbolKind.Local:
					ProcessLocalVariable(node, member);
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
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
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
			var ctorInfo = Context.SemanticModel.GetSymbolInfo(node.Type);

		    var methodSymbol = (MethodSymbol) ctorInfo.Symbol;
			if (TryProcessNoArgsCtorInvocationOnValueType(node, methodSymbol, ctorInfo)) return;

			EnsureMethodAvailable(methodSymbol);

			AddCilInstruction(ilVar, OpCodes.Newobj, methodSymbol.MethodResolverExpression(Context));
			PushCall();

			Visit(node.ArgumentList);
			FixCallSite();
		}

    	private bool TryProcessNoArgsCtorInvocationOnValueType(ObjectCreationExpressionSyntax node, MethodSymbol methodSymbol, SymbolInfo ctorInfo)
    	{
    		if (ctorInfo.Symbol.ContainingType.IsReferenceType || methodSymbol.Parameters.Count > 0)
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
			base.Visit(node.Expression);

			var info = Context.GetTypeInfo(node.Expression);
			if (node.Expression.Kind != SyntaxKind.AssignExpression &&  info.Type.SpecialType != SpecialType.System_Void)
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
			}

			throw new ArgumentException(string.Format("Literal type {0} not supported.", info.Type), "node");
		}

		private void EnsureMethodAvailable(MethodSymbol method)
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

		private void ProcessLocalVariable(IdentifierNameSyntax localVar, SymbolInfo varInfo)
		{
			AddCilInstruction(ilVar, OpCodes.Ldloc, LocalVariableIndex(localVar.ToString()));
		}

		private void ProcessParameter(IdentifierNameSyntax node, SymbolInfo paramInfo)
		{
			OpCode []optimizedLdArgs = { OpCodes.Ldarg_0, OpCodes.Ldarg_1, OpCodes.Ldarg_2, OpCodes.Ldarg_3};

			var param = paramInfo.Symbol as ParameterSymbol;

			var method = param.ContainingSymbol as MethodSymbol;
			if (node.Parent.Kind == SyntaxKind.MemberAccessExpression && paramInfo.Symbol.ContainingType.IsValueType)
			{
				AddCilInstruction(ilVar, OpCodes.Ldarga, param.Ordinal + +(method.IsStatic ? 0 : 1));
			}
			else
			{
				if (param.Ordinal >= 2)
				{
					AddCilInstruction(ilVar, OpCodes.Ldarg, param.Ordinal);
				}
				else
				{
					var loadOpCode = optimizedLdArgs[param.Ordinal + (method.IsStatic ? 0 : 1)];
					AddCilInstruction(ilVar, loadOpCode);
				}
			}
		}

    	private void InjectRequiredConversions(ExpressionSyntax expression)
    	{
			var info = Context.SemanticModel.GetTypeInfo(expression);
			InjectRequiredConversions(info);
    	}

    	private void InjectRequiredConversions(TypeInfo typeInfo)
    	{
    		if (typeInfo.ImplicitConversion.IsNumeric)
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

			if (typeInfo.ImplicitConversion.IsBoxing)
			{
				AddCilInstruction(ilVar, OpCodes.Box, typeInfo.Type);
			}
    	}

    	private void ProcessMethodCall(IdentifierNameSyntax node, MethodSymbol method)
		{
			if (!method.IsStatic && method.IsDefinedInCurrentType(Context) && node.Parent.Kind == SyntaxKind.InvocationExpression)
			{
				AddCilInstruction(ilVar, OpCodes.Ldarg_0);
			}
			
			EnsureMethodAvailable(method);
    		AddMethodCall(ilVar, method);
		}

		private Action<IVisitorContext, string, TypeSymbol, TypeSymbol> OperatorHandlerFor(SyntaxToken operatorToken)
		{
			if (operatorHandlers.ContainsKey(operatorToken.Kind))
			{
				return operatorHandlers[operatorToken.Kind];
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
					ctx.WriteCecilExpression("{0}.Append({0}.Create({1}, assembly.MainModule.Import(typeof(string).GetMethod(\"Concat\", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public, null, new[] {{ typeof(Object), typeof(Object) }}, null))));", ilVar, OpCodes.Call.ConstantName());
				}
				else
				{
					ctx.WriteCecilExpression(@"{0}.Append({0}.Create({1}));", ilVar, OpCodes.Add.ConstantName());
				}
			};
			
			operatorHandlers[SyntaxKind.SlashToken] = (ctx, ilVar, left, right) =>
				ctx.WriteCecilExpression(@"{0}.Append({0}.Create({1}));", ilVar, OpCodes.Div.ConstantName());

			operatorHandlers[SyntaxKind.GreaterThanToken] = (ctx, ilVar, left, right) => 
				ctx.WriteCecilExpression(@"{0}.Append({0}.Create({1}));", ilVar, OpCodes.Cgt.ConstantName());
			
		}

    	private bool valueTypeNoArgObjCreation;
		private readonly string ilVar;
    	private Stack<LinkedListNode<string>> callFixList = new Stack<LinkedListNode<string>>();
    	
		private static Action<IVisitorContext, string> NoOpInstance = delegate { };
		private static IDictionary<SyntaxKind, Action<IVisitorContext, string, TypeSymbol, TypeSymbol>> operatorHandlers = new Dictionary<SyntaxKind, Action<IVisitorContext, string, TypeSymbol, TypeSymbol>>();
    }

	internal interface IOperatorHandler
	{
	}
}
