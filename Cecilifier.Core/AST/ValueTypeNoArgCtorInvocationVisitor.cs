using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
	class ValueTypeNoArgCtorInvocationVisitor : SyntaxWalkerBase
	{
		private readonly SymbolInfo ctorInfo;
		private string ilVar;

		internal ValueTypeNoArgCtorInvocationVisitor(IVisitorContext ctx, string ilVar, SymbolInfo ctorInfo) : base(ctx)
		{
			this.ctorInfo = ctorInfo;
			this.ilVar = ilVar;
		}

		public override void VisitEqualsValueClause(EqualsValueClauseSyntax node)
		{
			//local variable assignment
			var firstAncestorOrSelf = node.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>();
			var varName = firstAncestorOrSelf.Declaration.Variables[0].Identifier.ValueText;

			AddCilInstruction(ilVar, OpCodes.Ldloca_S, LocalVariableIndexWithCast<byte>(varName));
			AddCilInstruction(ilVar, OpCodes.Initobj, ctorInfo.Symbol.ContainingType.ResolverExpression(Context));
		}

		public override void VisitReturnStatement(ReturnStatementSyntax node)
		{
			DeclareAndInitializeValueTypeLocalVariable();
		}

		public override void VisitBinaryExpression(BinaryExpressionSyntax node)
		{
			if (node.Kind() == SyntaxKind.SimpleAssignmentExpression)
			{
				new NoArgsValueTypeObjectCreatingInAssignmentVisitor(Context, ilVar).Visit(node.Left);
				AddCilInstruction(ilVar, OpCodes.Initobj, ctorInfo.Symbol.ContainingType.ResolverExpression(Context));
			}
		}

		public override void VisitArgument(ArgumentSyntax node)
		{
			DeclareAndInitializeValueTypeLocalVariable();
		}

		private void DeclareAndInitializeValueTypeLocalVariable()
		{
			var resolvedVarType = ResolveType(ctorInfo.Symbol.ContainingType);
			var tempLocalName = MethodExtensions.LocalVariableNameFor("tmp_", new[] {"tmp_".UniqueId().ToString()});
			AddCecilExpression("var {0} = new VariableDefinition(\"{1}\", {2});", tempLocalName, tempLocalName, resolvedVarType);

			AddCecilExpression("{0}.Body.Variables.Add({1});", Context.CurrentLocalVariable.VarName, tempLocalName);

			AddCilInstruction(ilVar, OpCodes.Ldloca_S, LocalVariableIndexWithCast<byte>(tempLocalName));
			AddCilInstruction(ilVar, OpCodes.Initobj, ctorInfo.Symbol.ContainingType.ResolverExpression(Context));
			AddCilInstruction(ilVar, OpCodes.Ldloc, LocalVariableIndex(tempLocalName));
		}
	}
}
