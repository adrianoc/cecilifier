using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    internal class NoArgsValueTypeObjectCreatingInAssignmentVisitor : SyntaxWalkerBase
    {
        private readonly string ilVar;

        internal NoArgsValueTypeObjectCreatingInAssignmentVisitor(IVisitorContext ctx, string ilVar) : base(ctx)
        {
            this.ilVar = ilVar;
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            var info = Context.SemanticModel.GetSymbolInfo(node);
            switch (info.Symbol.Kind)
            {
                case SymbolKind.Local:
                    AddCilInstruction(ilVar, OpCodes.Ldloca_S, Context.DefinitionVariables.GetVariable(node.Identifier.ValueText, VariableMemberKind.LocalVariable).VariableName);
                    break;

                case SymbolKind.Field:
                    var fs = (IFieldSymbol) info.Symbol;
                    var fieldResolverExpression = fs.FieldResolverExpression(Context);
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
                    var parameterSymbol = (IParameterSymbol) info.Symbol;
                    AddCilInstruction(ilVar, parameterSymbol.RefKind == RefKind.None ? OpCodes.Ldarga : OpCodes.Ldarg, parameterSymbol.Ordinal);
                    break;
            }
        }

        public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            AddCilInstruction(ilVar, OpCodes.Ldloca_S, Context.DefinitionVariables.GetVariable(node.Declaration.Variables[0].Identifier.ValueText, VariableMemberKind.LocalVariable).VariableName);
        }
    }
}
