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

        public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        {
            ExpressionVisitor.Visit(Context, ilVar, node);
            
            //ExpressionVisitor assumes the visited expression is to be handled as a 'load'
            //whence it will emit a Ldelem_ref as after visiting `node` but...
            var ldelemToReplace = Context.CurrentLine;
            ldelemToReplace.List.Remove(ldelemToReplace);
            
            //...since we have an `assignment` to an array element which is of type
            //struct, we need to load the element address instead. 
            Context.EmitCilInstruction(ilVar, OpCodes.Ldelema);
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            var info = Context.SemanticModel.GetSymbolInfo(node);
            switch (info.Symbol.Kind)
            {
                case SymbolKind.Local:
                    var operand = Context.DefinitionVariables.GetVariable(node.Identifier.ValueText, VariableMemberKind.LocalVariable).VariableName;
                    var localSymbol = (ILocalSymbol) info.Symbol;
                    Context.EmitCilInstruction(ilVar, localSymbol.RefKind == RefKind.None ? OpCodes.Ldloca_S : OpCodes.Ldloc_S, operand);
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
                    if (parameterSymbol.RefKind != RefKind.None)
                    {
                        // In this scenario AssignmentVisitor.PreProcessRefOutAssignments() has already
                        // pushed the right value to the stack
                        break;
                    }
                    
                    var opCode = parameterSymbol.RefKind == RefKind.None ? OpCodes.Ldarga : OpCodes.Ldarg;
                    Context.EmitCilInstruction(ilVar, opCode, parameterSymbol.Ordinal); // TODO: Static / Instance methods handling...
                    break;
            }
        }
    }
}
