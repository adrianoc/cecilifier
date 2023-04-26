using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Cecilifier.Core.Variables;
using Mono.Cecil.Cil;
using static Cecilifier.Core.Misc.CodeGenerationHelpers;

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

        public bool TargetOfAssignmentIsValueType { get; private set; } = true;

        public override void VisitUsingStatement(UsingStatementSyntax node)
        {
            // our direct parent is a using statement, which means we have something like:
            // using(new Value()) {}
            DeclareAndInitializeValueTypeLocalVariable();
        }

        public override void VisitEqualsValueClause(EqualsValueClauseSyntax node)
        {
            //local variable assignment
            var firstAncestorOrSelf = node.FirstAncestorOrSelf<VariableDeclarationSyntax>();
            var varName = firstAncestorOrSelf?.Variables[0].Identifier.ValueText;

            var operand = Context.DefinitionVariables.GetVariable(varName, VariableMemberKind.LocalVariable).VariableName;
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloca_S, operand);
            var type = Context.TypeResolver.Resolve(ctorInfo.Symbol.ContainingType);
            Context.EmitCilInstruction(ilVar, OpCodes.Initobj, type);
        }

        public override void VisitReturnStatement(ReturnStatementSyntax node)
        {
            DeclareAndInitializeValueTypeLocalVariable();
        }

        public override void VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
        {
            DeclareAndInitializeValueTypeLocalVariable();
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            DeclareAndInitializeValueTypeLocalVariable();
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            var instantiatedType = Context.TypeResolver.Resolve(ctorInfo.Symbol.ContainingType);
            var visitor = new NoArgsValueTypeObjectCreatingInAssignmentVisitor(Context, ilVar, instantiatedType, DeclareAndInitializeValueTypeLocalVariable);
            node.Left.Accept(visitor);
            TargetOfAssignmentIsValueType = visitor.TargetOfAssignmentIsValueType;
        }

        public override void VisitArgument(ArgumentSyntax node)
        {
            DeclareAndInitializeValueTypeLocalVariable();
        }

        private void DeclareAndInitializeValueTypeLocalVariable()
        {
            var resolvedVarType = Context.TypeResolver.Resolve(ctorInfo.Symbol.ContainingType);
            var tempLocal = AddLocalVariableToCurrentMethod(Context, "vt", resolvedVarType);
            
            using var _ = Context.DefinitionVariables.WithVariable(tempLocal);
            
            switch (ctorInfo.Symbol.ContainingType.SpecialType)
            {
                case SpecialType.System_Char:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                    Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4_0);
                    if (ctorInfo.Symbol.ContainingType.SpecialType == SpecialType.System_Int64)
                        Context.EmitCilInstruction(ilVar, OpCodes.Conv_I8);
                    Context.EmitCilInstruction(ilVar, OpCodes.Stloc, tempLocal.VariableName);
                    Context.EmitCilInstruction(ilVar, OpCodes.Ldloca_S, tempLocal.VariableName);
                    break;

                case SpecialType.None:
                    InitValueTypeLocalVariable(tempLocal.VariableName, ctorInfo.Symbol.ContainingType);
                    Context.EmitCilInstruction(ilVar, OpCodes.Ldloc, tempLocal.VariableName);
                    break;

                default:
                    Context.WriteComment($"Instantiating {ctorInfo.Symbol.Name} is not supported.");
                    break;
            }
        }

        private void InitValueTypeLocalVariable(string localVariable, ITypeSymbol variableType)
        {
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloca_S, localVariable);
            AddCilInstruction(ilVar, OpCodes.Initobj, variableType);
        }
    }
}
