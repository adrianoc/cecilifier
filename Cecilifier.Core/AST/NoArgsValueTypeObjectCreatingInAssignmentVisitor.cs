using System;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

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
			//var info = new SymbolInfo();
			var info = Context.SemanticModel.GetSymbolInfo(node);
			switch (info.Symbol.Kind)
			{
				case SymbolKind.Local:
					AddCilInstruction(ilVar, OpCodes.Ldloca_S, LocalVariableIndex(node.Identifier.ValueText));
					break;

				case SymbolKind.Field:
					var fs = (IFieldSymbol) info.Symbol;
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
					var parameterSymbol = ((IParameterSymbol) info.Symbol);
					//AddCilInstruction(ilVar, parameterSymbol.RefKind == RefKind.None ? OpCodes.Ldarga : OpCodes.Ldarg, Context.Parameters.BackingVariableNameFor(parameterSymbol.Name));
					AddCilInstruction(ilVar, parameterSymbol.RefKind == RefKind.None ? OpCodes.Ldarga : OpCodes.Ldarg, parameterSymbol.Ordinal);
					break;
			}
		}

		public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
		{
			AddCilInstruction(ilVar, OpCodes.Ldloca_S, LocalVariableIndex(node.Declaration.Variables[0].Identifier.ValueText));
		}
	}
}
