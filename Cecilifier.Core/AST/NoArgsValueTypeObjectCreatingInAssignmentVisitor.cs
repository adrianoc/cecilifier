using System;
using Cecilifier.Core.Extensions;
using Mono.Cecil.Cil;
using Roslyn.Compilers;
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

		public override void VisitIdentifierName(IdentifierNameSyntax node)
		{
			var info = new SymbolInfo();
			//var info = Context.GetDeclaredSymbol(node);
			switch (info.Symbol.Kind)
			{
				case SymbolKind.Local:
					AddCilInstruction(ilVar, OpCodes.Ldloca_S, LocalVariableIndexWithCast<byte>(node.ToFullString()));
					break;

				case SymbolKind.Field:
					var fs = (FieldSymbol) info.Symbol;
					string fieldResolverExpression = fs.FieldResolverExpression(Context);

					if (info.Symbol.IsStatic)
					{
						AddCilInstruction(ilVar, OpCodes.Ldsflda, fieldResolverExpression);
					}
					else
					{
						AddCilInstruction(ilVar, OpCodes.Ldarg_0);
						AddCilInstruction(ilVar, OpCodes.Ldflda, fieldResolverExpression);
					}
					break;
					
				case SymbolKind.Parameter:
					var parameterSymbol = ((ParameterSymbol) info.Symbol);
					AddCilInstruction(ilVar, parameterSymbol.RefKind == RefKind.None ? OpCodes.Ldarga : OpCodes.Ldarg, parameterSymbol.Ordinal);
					break;
			}
		}

		public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
		{
			AddCilInstruction(ilVar, OpCodes.Ldloca_S, LocalVariableIndexWithCast<byte>(node.Declaration.Variables[0].Identifier.ValueText));
		}
	}
}
