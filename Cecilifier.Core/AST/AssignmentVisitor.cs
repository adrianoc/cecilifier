using Cecilifier.Core.Extensions;
using Mono.Cecil.Cil;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.AST
{
	class AssignmentVisitor : SyntaxWalkerBase
	{
		internal AssignmentVisitor(IVisitorContext ctx, string ilVar) : base(ctx)
		{
			this.ilVar = ilVar;
		}

		public override void VisitIdentifierName(IdentifierNameSyntax node)
		{
			var member = Context.SemanticModel.GetSymbolInfo(node);
			if (member.Symbol.ContainingType.IsValueType && node.Parent.Kind == SyntaxKind.ObjectCreationExpression && ((ObjectCreationExpressionSyntax)node.Parent).ArgumentList.Arguments.Count == 0) return;

			switch (member.Symbol.Kind)
			{
				case SymbolKind.Parameter:
					ParameterAssignment(member.Symbol as ParameterSymbol);
					break;

				case SymbolKind.Local:
					LocalVariableAssignment(member.Symbol as LocalSymbol);
					break;

				case SymbolKind.Field:
					FieldAssignment(member.Symbol as FieldSymbol);
					break;
			}
		}

		private void FieldAssignment(FieldSymbol field)
		{
			AddCilInstruction(ilVar, OpCodes.Stfld, field.FieldResolverExpression(Context));
		}

		private void LocalVariableAssignment(LocalSymbol localVariable)
		{
			var methodVar = LocalVariableNameForCurrentNode();
			AddCilInstruction(ilVar, OpCodes.Stloc, LocalVariableIndex(methodVar, localVariable));
		}

		private void ParameterAssignment(ParameterSymbol parameter)
		{
			AddCilInstructionCastOperand(ilVar, OpCodes.Starg_S, (byte) parameter.Ordinal);
		}

		private readonly string ilVar;
	}
}
