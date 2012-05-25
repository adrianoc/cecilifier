using Cecilifier.Core.Extensions;
using Mono.Cecil.Cil;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.AST
{
	class ValueTypeNoArgCtorInvocationVisitor : SyntaxWalkerBase
	{
		private readonly SemanticInfo ctorInfo;
		private string ilVar;

		internal ValueTypeNoArgCtorInvocationVisitor(IVisitorContext ctx, string ilVar, SemanticInfo ctorInfo) : base(ctx)
		{
			this.ctorInfo = ctorInfo;
			this.ilVar = ilVar;
		}

		protected override void VisitEqualsValueClause(EqualsValueClauseSyntax node)
		{
			//local variable assignment
			var firstAncestorOrSelf = node.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>();
			var varName = firstAncestorOrSelf.Declaration.Variables[0].Identifier.ValueText;

			AddCilInstruction(ilVar, OpCodes.Ldloca_S, LocalVariableIndexWithCast<byte>(varName));
			AddCilInstruction(ilVar, OpCodes.Initobj, ctorInfo.Type.ResolverExpression(Context));
		}

		protected override void VisitReturnStatement(ReturnStatementSyntax node)
		{
			DeclareAndInitializeValueTypeLocalVariable();
		}

		protected override void VisitBinaryExpression(BinaryExpressionSyntax node)
		{
			if (node.Kind == SyntaxKind.AssignExpression)
			{
				new NoArgsValueTypeObjectCreatingInAssignmentVisitor(Context, ilVar).Visit(node.Left);
				AddCilInstruction(ilVar, OpCodes.Initobj, ctorInfo.Type.ResolverExpression(Context));
			}
		}

		protected override void VisitArgument(ArgumentSyntax node)
		{
			DeclareAndInitializeValueTypeLocalVariable();
		}

		private void DeclareAndInitializeValueTypeLocalVariable()
		{
			var resolvedVarType = ResolveType(ctorInfo.Type);
			var tempLocalName = LocalVariableNameFor("tmp_", "tmp_".UniqueId().ToString());
			AddCecilExpression("var {0} = new VariableDefinition(\"{1}\", {2});", tempLocalName, tempLocalName, resolvedVarType);

			AddCecilExpression("{0}.Body.Variables.Add({1});", Context.CurrentLocalVariable.VarName, tempLocalName);

			AddCilInstruction(ilVar, OpCodes.Ldloca_S, LocalVariableIndexWithCast<byte>(tempLocalName));
			AddCilInstruction(ilVar, OpCodes.Initobj, ctorInfo.Type.ResolverExpression(Context));
			AddCilInstruction(ilVar, OpCodes.Ldloc, LocalVariableIndex(tempLocalName));
		}
	}
}
