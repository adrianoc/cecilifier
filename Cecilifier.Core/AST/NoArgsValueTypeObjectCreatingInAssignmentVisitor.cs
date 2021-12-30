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
                    string operand = Context.DefinitionVariables.GetVariable(node.Identifier.ValueText, VariableMemberKind.LocalVariable).VariableName;
                    Context.EmitCilInstruction(ilVar, OpCodes.Ldloca_S, operand);
                    break;

                case SymbolKind.Field:
                    var fs = (IFieldSymbol) info.Symbol;
                    var fieldResolverExpression = fs.FieldResolverExpression(Context);
                    if (info.Symbol.IsStatic)
                    {
                        Context.EmitCilInstruction(ilVar, OpCodes.Ldsflda, fieldResolverExpression);
                    }
                    else
                    {
                        Context.EmitCilInstruction(ilVar, OpCodes.Ldarg_0);
                        Context.EmitCilInstruction(ilVar, OpCodes.Ldflda, fieldResolverExpression);
                    }

                    break;

                case SymbolKind.Parameter:
                    var parameterSymbol = (IParameterSymbol) info.Symbol;
                    OpCode opCode = parameterSymbol.RefKind == RefKind.None ? OpCodes.Ldarga : OpCodes.Ldarg;
                    Context.EmitCilInstruction(ilVar, opCode, parameterSymbol.Ordinal);
                    break;
            }
        }

        public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            string operand = Context.DefinitionVariables.GetVariable(node.Declaration.Variables[0].Identifier.ValueText, VariableMemberKind.LocalVariable).VariableName;
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloca_S, operand);
        }
    }
}
