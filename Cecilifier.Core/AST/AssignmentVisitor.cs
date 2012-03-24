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

		protected override void VisitIdentifierName(IdentifierNameSyntax node)
		{
			var member = Context.SemanticModel.GetSemanticInfo(node);

			switch (member.Symbol.Kind)
			{
				case SymbolKind.Parameter:
					ParameterAssign(member.Symbol as ParameterSymbol);
					break;

				case SymbolKind.Local:
					LocalVariableAssign(member.Symbol as LocalSymbol);
					break;
			}
		}

		private void LocalVariableAssign(LocalSymbol localVariable)
		{
			AddCilInstruction(ilVar, OpCodes.Stloc, 1);
		}

		private void ParameterAssign(ParameterSymbol parameter)
		{
			AddCilInstructionCastOperand(ilVar, OpCodes.Starg_S, (byte) parameter.Ordinal);
		}

		private readonly string ilVar;
	}
}
