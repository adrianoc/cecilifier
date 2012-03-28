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
				//TODO: Handle operator.
				Visit(node.Right);
			}
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
					AddCilInstruction(ilVar, opCode, node.GetFullText());
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
		 *			push x                     |  Should be the here
		 *			add                        |
		 *			push y                     |
		 *			         <-----------------+
		 * 
		 * To fix this we visit in the later order (exp, args) and move the call operation after visiting the arguments
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
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
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
					ProcessParameter(member.Symbol as ParameterSymbol);
					break;

				case SymbolKind.Local:
					ProcessLocalVariable(member.Symbol as LocalSymbol);
					break;
			}
		}

    	//protected override void VisitArgument(ArgumentSyntax node)
		//{
		//    WriteLine("[{0}] : {1} ({2})", new StackFrame().GetMethod().Name, node, node.Parent.Parent);
		//    // node.Parent.Parent => possible method definition...
		//    var info = Context.SemanticModel.GetDeclaredSymbol(node);
		//    info = Context.SemanticModel.GetDeclaredSymbol(node.Expression);
			
		//}

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
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

		protected override void VisitConstructorInitializer(ConstructorInitializerSyntax node)
		{
			WriteLine("[{0}] : {1}", new StackFrame().GetMethod().Name, node);
		}

    	private void WriteLine(string msg, params object[] args)
    	{
    		Console.WriteLine(msg, args);
    	}

		private OpCode LoadOpCodeFor(LiteralExpressionSyntax node)
		{
			var info = Context.SemanticModel.GetSemanticInfo(node);
			switch (info.Type.SpecialType)
			{
				case SpecialType.System_Single: return OpCodes.Ldc_R4;
				case SpecialType.System_Double: return OpCodes.Ldc_R8;
				
				case SpecialType.System_Int16:
				case SpecialType.System_Int32: return OpCodes.Ldc_I4;

				case SpecialType.System_Int64: return OpCodes.Ldc_I8;
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

		private void ProcessLocalVariable(LocalSymbol localVariable)
		{
			AddCilInstruction(ilVar, OpCodes.Ldloc, 1);
			//AddCilInstruction(ilVar, OpCodes.Ldloc, localVariable.Name);
		}

		private void ProcessParameter(ParameterSymbol param)
		{
			var method = param.ContainingSymbol as MethodSymbol;
			switch (param.Ordinal)
			{
				case 0:
					AddCilInstruction(ilVar, method.IsStatic ? OpCodes.Ldarg_0 : OpCodes.Ldarg_1);
					break;

				case 1:
					AddCilInstruction(ilVar, method.IsStatic ? OpCodes.Ldarg_1 : OpCodes.Ldarg_2);
					break;

				case 2:
				default:
					if (method.IsStatic)
					{
						AddCilInstruction(ilVar, OpCodes.Ldarg_3);
					}
					else
					{
						AddCilInstruction(ilVar, OpCodes.Ldarg, param.Ordinal);	
					}
					break;
			}
		}

		private void ProcessMethodCall(IdentifierNameSyntax node, MethodSymbol method)
		{
			if (!method.IsStatic && method.IsDefinedInCurrentType(Context) && node.Parent.Kind == SyntaxKind.InvocationExpression)
			{
				AddCilInstruction(ilVar, OpCodes.Ldarg_0);
			}

			EnsureMethodAvailable(method);
			AddCilInstruction(ilVar, OpCodes.Call, method.MethodResolverExpression(Context));
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
		
		//private readonly IMemoryLocationResolver resolver;
		private readonly string ilVar;
    	private Stack<LinkedListNode<string>> callFixList = new Stack<LinkedListNode<string>>();
    }
}
