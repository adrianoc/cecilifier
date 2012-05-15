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
    	internal ExpressionVisitor(IVisitorContext ctx, string ilVar) : base(ctx)
        {
        	this.ilVar = ilVar;
        }

        protected override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
			if (node.Kind == SyntaxKind.AssignExpression)
			{
				Visit(node.Right);
				new AssignmentVisitor(Context, ilVar).Visit(node.Left);
			}
			else
			{
				Visit(node.Left);
				Visit(node.Right);

				var handler = OperatorHandlerFor(node.OperatorToken);
				handler(Context, ilVar, Context.SemanticModel.GetSemanticInfo(node.Left).Type, Context.SemanticModel.GetSemanticInfo(node.Right).Type);
			}
        }

    	private Action<IVisitorContext, string, TypeSymbol, TypeSymbol> OperatorHandlerFor(SyntaxToken operatorToken)
    	{
			if (operatorHandlers.ContainsKey(operatorToken.Kind))
			{
				return operatorHandlers[operatorToken.Kind];
			}

    		throw new Exception(string.Format("Operator {0} not supported yet (expression: {1})", operatorToken.ValueText, operatorToken.Parent));
    	}

    	protected override void VisitLiteralExpression(LiteralExpressionSyntax node)
        {
			switch(node.Kind)
			{
				case SyntaxKind.StringLiteralExpression:
					AddCilInstruction(ilVar, OpCodes.Ldstr, node.GetFullText());
					break;

				case SyntaxKind.NumericLiteralExpression:
					var opCode = LoadOpCodeFor(node);
					AddCilInstruction(ilVar, opCode.Item1, node.GetFullText());
					if (opCode.Item2 != null)
					{
						AddCilInstruction(ilVar, OpCodes.Box, opCode.Item2);
					}
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
		protected override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
			Visit(node.Expression);
			PushCall();

			Visit(node.ArgumentList);
			FixCallSite();
        }

    	protected override void VisitConditionalExpression(ConditionalExpressionSyntax node)
    	{
    		int count = 0;
    		Func<OpCode, string> Emit = op =>
    		                            	{
    		                            			var instVarName = op.Name + "_" + node.GetHashCode() + "_" + count++;
													AddCecilExpression(@"var {0} = {1}.Create({2});", instVarName, ilVar, op.ConstantName());

    		                                 		return instVarName;
    		                                 	};

    		var conditionEnd = Emit(OpCodes.Nop);
			var whenFalse = Emit(OpCodes.Nop);
			
			Visit(node.Condition);
			AddCilInstruction(ilVar, OpCodes.Brfalse_S, whenFalse);

			Visit(node.WhenTrue);
			AddCilInstruction(ilVar, OpCodes.Br_S, conditionEnd);

			AddCecilExpression("{0}.Append({1});", ilVar, whenFalse);
			Visit(node.WhenFalse);

			AddCecilExpression("{0}.Append({1});", ilVar, conditionEnd);
        }

		protected override void VisitIdentifierName(IdentifierNameSyntax node)
		{
			var member = Context.SemanticModel.GetSemanticInfo(node);

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

		protected override void VisitThisExpression(ThisExpressionSyntax node)
		{
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

    	protected override void VisitMemberAccessExpression(MemberAccessExpressionSyntax exp)
        {
			Visit(exp.Expression);
        	Visit(exp.Name);
        }

        protected override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
        }

        protected override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

        protected override void VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

        protected override void VisitMakeRefExpression(MakeRefExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

        protected override void VisitRefTypeExpression(RefTypeExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

        protected override void VisitRefValueExpression(RefValueExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

        protected override void VisitCheckedExpression(CheckedExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

        protected override void VisitDefaultExpression(DefaultExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

        protected override void VisitTypeOfExpression(TypeOfExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

        protected override void VisitSizeOfExpression(SizeOfExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

        protected override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

        protected override void VisitCastExpression(CastExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

        protected override void VisitInitializerExpression(InitializerExpressionSyntax node)
        {
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

		protected override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
		{
			//TODO: Refactor to reuse code from VisitIdentifierName....
			var ctorInfo = Context.SemanticModel.GetSemanticInfo(node.Type);

			var methodSymbol = (MethodSymbol) ctorInfo.Symbol;
			EnsureMethodAvailable(methodSymbol);

			AddCilInstruction(ilVar, OpCodes.Newobj, methodSymbol.MethodResolverExpression(Context));
			PushCall();

			Visit(node.ArgumentListOpt);
			FixCallSite();
		}

        protected override void VisitExpressionStatement(ExpressionStatementSyntax node)
        {
			base.Visit(node.Expression);

			var info = Context.GetSemanticInfo(node.Expression);
			if (node.Expression.Kind != SyntaxKind.AssignExpression &&  info.Type.SpecialType != SpecialType.System_Void)
			{
				AddCilInstruction(ilVar, OpCodes.Pop);
			}
        }

		protected override void VisitConstructorInitializer(ConstructorInitializerSyntax node)
		{
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

    	private void WriteLine(string msg, params object[] args)
    	{
    		Console.WriteLine(msg, args);
    	}

		private Tuple<OpCode, object> LoadOpCodeFor(LiteralExpressionSyntax node)
		{
			var info = Context.SemanticModel.GetSemanticInfo(node);

			switch (info.Type.SpecialType)
			{
				case SpecialType.System_Single:
					return Tuple.Create<OpCode, object>(OpCodes.Ldc_R4, !info.ImplicitConversion.IsBoxing ? null : ResolvePredefinedType(info.Type));

				case SpecialType.System_Double:
					return Tuple.Create<OpCode, object>(OpCodes.Ldc_R8, !info.ImplicitConversion.IsBoxing ? null : ResolvePredefinedType(info.Type));
				
				case SpecialType.System_Int16:
				case SpecialType.System_Int32:
					return Tuple.Create<OpCode, object>(OpCodes.Ldc_I4, !info.ImplicitConversion.IsBoxing ? null : ResolvePredefinedType(info.Type));

				case SpecialType.System_Int64:
					return Tuple.Create<OpCode, object>(OpCodes.Ldc_I8, !info.ImplicitConversion.IsBoxing ? null : ResolvePredefinedType(info.Type));
			}

			throw new ArgumentException(string.Format("Literal type {0} not supported.", info.Type.Name), "node");
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

		private void ProcessLocalVariable(IdentifierNameSyntax localVar, SemanticInfo varInfo)
		{
			AddCilInstruction(ilVar, OpCodes.Ldloc, LocalVariableIndex(localVar.PlainName));
			InjectRequiredConversions(varInfo);
		}

		private void ProcessParameter(IdentifierNameSyntax node, SemanticInfo paramInfo)
		{
			OpCode []optimizedLdArgs = { OpCodes.Ldarg_0, OpCodes.Ldarg_1, OpCodes.Ldarg_2, OpCodes.Ldarg_3};

			var param = paramInfo.Symbol as ParameterSymbol;

			var method = param.ContainingSymbol as MethodSymbol;
			if (node.Parent.Kind == SyntaxKind.MemberAccessExpression && paramInfo.Type.IsValueType)
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


			InjectRequiredConversions(paramInfo);
			//switch (param.Ordinal)
			//{
			//    case 0:
			//        AddCilInstruction(ilVar, method.IsStatic ? OpCodes.Ldarg_0 : OpCodes.Ldarg_1);
			//        break;

			//    case 1:
			//        AddCilInstruction(ilVar, method.IsStatic ? OpCodes.Ldarg_1 : OpCodes.Ldarg_2);
			//        break;

			//    case 2:
			//    default:
			//        if (method.IsStatic)
			//        {
			//            AddCilInstruction(ilVar, OpCodes.Ldarg_3);
			//        }
			//        else
			//        {
			//            AddCilInstruction(ilVar, OpCodes.Ldarg, param.Ordinal);	
			//        }
			//        break;
			//}

			//InjectRequiredConversions(paramInfo);
		}

    	private void InjectRequiredConversions(SemanticInfo semanticInfo)
    	{
    		if (semanticInfo.ImplicitConversion.IsNumeric)
    		{
    			switch (semanticInfo.ConvertedType.SpecialType)
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
    					throw new Exception(string.Format("Conversion from {0} to {1}  not implemented.", semanticInfo.Type, semanticInfo.ConvertedType));
    			}
    		}
    		else if (semanticInfo.ImplicitConversion.IsBoxing)
    		{
    			AddCilInstruction(ilVar, OpCodes.Box, ResolvePredefinedType(semanticInfo.Type));
    		}
    	}

    	private void ProcessMethodCall(IdentifierNameSyntax node, MethodSymbol method)
		{
			if (!method.IsStatic && method.IsDefinedInCurrentType(Context) && node.Parent.Kind == SyntaxKind.InvocationExpression)
			{
				AddCilInstruction(ilVar, OpCodes.Ldarg_0);
			}

			EnsureMethodAvailable(method);
			AddCilInstruction(ilVar, method.IsVirtual || method.IsAbstract ? OpCodes.Callvirt : OpCodes.Call, method.MethodResolverExpression(Context));
			//AddCilInstruction(ilVar, method.IsVirtual || method.IsAbstract || method.IsOverride ? OpCodes.Callvirt : OpCodes.Call, method.MethodResolverExpression(Context));
		}

		// TypeSyntax ?
        // InstanceExpressionSyntax ?

        // 
        // AnonymousMethodExpressionSyntax
        // SimpleLambdaExpressionSyntax
        // ParenthesizedLambdaExpressionSyntax
        // 
        // 
        // AnonymousObjectCreationExpressionSyntax
        // ArrayCreationExpressionSyntax
        // ImplicitArrayCreationExpressionSyntax
        // StackAllocArrayCreationExpressionSyntax
        // QueryExpressionSyntax
		
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
			{
				ctx.WriteCecilExpression(@"{0}.Append({0}.Create({1}));", ilVar, OpCodes.Div.ConstantName());
			};

			operatorHandlers[SyntaxKind.GreaterThanToken] = (ctx, ilVar, left, right) =>
			{
				ctx.WriteCecilExpression(@"{0}.Append({0}.Create({1}));", ilVar, OpCodes.Cgt.ConstantName());
			};
		}

    	//private readonly IMemoryLocationResolver resolver;
		private readonly string ilVar;
    	private Stack<LinkedListNode<string>> callFixList = new Stack<LinkedListNode<string>>();
    	
		private static Action<IVisitorContext, string> NoOpInstance = delegate { };
		private static IDictionary<SyntaxKind, Action<IVisitorContext, string, TypeSymbol, TypeSymbol>> operatorHandlers = new Dictionary<SyntaxKind, Action<IVisitorContext, string, TypeSymbol, TypeSymbol>>();
    }

	internal interface IOperatorHandler
	{
	}
}
