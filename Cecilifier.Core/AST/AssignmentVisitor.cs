using System;
using Cecilifier.Core.Extensions;
using Mono.Cecil.Cil;
using Roslyn.Compilers;
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
			if (parameter.RefKind == RefKind.None)
			{
				AddCilInstructionCastOperand(ilVar, OpCodes.Starg_S, (byte) parameter.Ordinal);
			}
			else
			{
				OpCode opCode = OpCodes.Stind_Ref;
				switch (parameter.Type.SpecialType)
				{
					case SpecialType.None:
					case SpecialType.System_Char:
					case SpecialType.System_Int16:
						opCode = OpCodes.Stind_I2;
						break;

					case SpecialType.System_Int32:
						opCode = OpCodes.Stind_I4;
						break;

					case SpecialType.System_Single:
						opCode = OpCodes.Stind_R4;
						break;

					default:
						if (!parameter.Type.IsReferenceType) throw new NotSupportedException(string.Format("Assinment to ref/out parameters of type {0} not supported yet.", parameter.Type));
						break;
				}

				AddCilInstruction(ilVar, opCode);
			}
		}

		public void PreProcessRefOutAssignments(ExpressionSyntax node)
		{
			var paramSymbol = ParameterVisitor.Process(Context, node);
			if (paramSymbol != null && paramSymbol.RefKind != RefKind.None)
			{
				ProcessParameter(ilVar, node, paramSymbol);
			}
		}

		private readonly string ilVar;
	}
}
