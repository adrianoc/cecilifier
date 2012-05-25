using System;
using Cecilifier.Core.Extensions;
using Mono.Cecil.Cil;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.AST
{
	class NoArgsValueTypeObjectCreatingInAssignmentVisitor : SyntaxWalkerBase
	{
		private readonly string ilVar;

		internal NoArgsValueTypeObjectCreatingInAssignmentVisitor(IVisitorContext ctx, string ilVar) : base(ctx)
		{
			this.ilVar = ilVar;
		}

		protected override void VisitIdentifierName(IdentifierNameSyntax node)
		{
			var info = Context.GetSemanticInfo(node);
			switch (info.Symbol.Kind)
			{
				case SymbolKind.Local:
					AddCilInstruction(ilVar, OpCodes.Ldloca_S, LocalVariableIndexWithCast<byte>(node.PlainName));
					break;

				case SymbolKind.Field:
					var fs = (FieldSymbol) info.Symbol;
					if (info.Symbol.IsStatic)
					{
						AddCilInstruction(ilVar, OpCodes.Ldsflda, fs.FieldResolverExpression(Context));
					}
					else
					{
						AddCilInstruction(ilVar, OpCodes.Ldarg_0);
						AddCilInstruction(ilVar, OpCodes.Ldflda, fs.FieldResolverExpression(Context));
					}
					break;
					
				case SymbolKind.Parameter:
					break;
					//throw new NotImplementedException("Parameters are not supported in " + node.Parent);
			}
		}

		protected override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
		{
			AddCilInstruction(ilVar, OpCodes.Ldloca_S, LocalVariableIndexWithCast<byte>(node.Declaration.Variables[0].Identifier.ValueText));
		}
	}
}
