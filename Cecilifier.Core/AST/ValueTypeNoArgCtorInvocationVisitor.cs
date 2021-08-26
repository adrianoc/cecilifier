using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    internal class ValueTypeNoArgCtorInvocationVisitor : SyntaxWalkerBase
    {
        private readonly SymbolInfo ctorInfo;
        private readonly string ilVar;

        internal ValueTypeNoArgCtorInvocationVisitor(IVisitorContext ctx, string ilVar, SymbolInfo ctorInfo) : base(ctx)
        {
            this.ctorInfo = ctorInfo;
            this.ilVar = ilVar;
        }

        public override void VisitEqualsValueClause(EqualsValueClauseSyntax node)
        {
            //local variable assignment
            var firstAncestorOrSelf = node.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>();
            var varName = firstAncestorOrSelf.Declaration.Variables[0].Identifier.ValueText;

            AddCilInstruction(ilVar, OpCodes.Ldloca_S, Context.DefinitionVariables.GetVariable(varName, MemberKind.LocalVariable).VariableName);
            AddCilInstruction(ilVar, OpCodes.Initobj, Context.TypeResolver.Resolve(ctorInfo.Symbol.ContainingType));
        }

        public override void VisitReturnStatement(ReturnStatementSyntax node)
        {
            DeclareAndInitializeValueTypeLocalVariable();
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            DeclareAndInitializeValueTypeLocalVariable();
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            new NoArgsValueTypeObjectCreatingInAssignmentVisitor(Context, ilVar).Visit(node.Left);
            AddCilInstruction(ilVar, OpCodes.Initobj, Context.TypeResolver.Resolve(ctorInfo.Symbol.ContainingType));
        }

        public override void VisitArgument(ArgumentSyntax node)
        {
            DeclareAndInitializeValueTypeLocalVariable();
        }

        private void DeclareAndInitializeValueTypeLocalVariable()
        {
            var resolvedVarType = Context.TypeResolver.Resolve(ctorInfo.Symbol.ContainingType);
            var tempLocalName = AddLocalVariableWithResolvedType("vt", Context.DefinitionVariables.GetLastOf(MemberKind.Method), resolvedVarType);

            switch (ctorInfo.Symbol.ContainingType.SpecialType)
            {
                case SpecialType.System_Char:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                    AddCilInstruction(ilVar, OpCodes.Ldc_I4_0);
                    if (ctorInfo.Symbol.ContainingType.SpecialType == SpecialType.System_Int64)
                        AddCilInstruction(ilVar, OpCodes.Conv_I8);
                    AddCilInstruction(ilVar, OpCodes.Stloc, tempLocalName);
                    AddCilInstruction(ilVar, OpCodes.Ldloca_S, tempLocalName);
                    break;
                    
                case SpecialType.None:
                    InitValueTypeLocalVariable(tempLocalName, ctorInfo.Symbol.ContainingType);
                    AddCilInstruction(ilVar, OpCodes.Ldloc, tempLocalName);
                    break;
                
                default:
                    Context.WriteComment($"Instantiating {ctorInfo.Symbol.Name} is not supported.");
                    break;
            }
        }
        
        private void InitValueTypeLocalVariable(string localVariable, INamedTypeSymbol variableType)
        {
            AddCilInstruction(ilVar, OpCodes.Ldloca_S, localVariable);
            AddCilInstruction(ilVar, OpCodes.Initobj, Context.TypeResolver.Resolve(variableType));
        }
    }
}
