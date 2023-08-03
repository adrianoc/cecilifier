using Cecilifier.Core.Extensions;
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

        public override void VisitUsingStatement(UsingStatementSyntax node)
        {
            // our direct parent is a using statement, which means we have something like:
            // using(new Value()) {}
            var valueTypeLocalVariable = DeclareAndInitializeValueTypeLocalVariable();
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloc, valueTypeLocalVariable.VariableName);
        }

        public override void VisitEqualsValueClause(EqualsValueClauseSyntax node)
        {
            //local variable assignment
            var firstAncestorOrSelf = node.FirstAncestorOrSelf<VariableDeclarationSyntax>();
            var varName = firstAncestorOrSelf?.Variables[0].Identifier.ValueText;

            var operand = Context.DefinitionVariables.GetVariable(varName, VariableMemberKind.LocalVariable).VariableName;
            InitValueTypeLocalVariable(operand);
        }

        public override void VisitReturnStatement(ReturnStatementSyntax node)
        {
            var valueTypeLocalVariable = DeclareAndInitializeValueTypeLocalVariable();
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloc, valueTypeLocalVariable.VariableName);
        }

        public override void VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
        {
            var valueTypeLocalVariable = DeclareAndInitializeValueTypeLocalVariable();
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloc, valueTypeLocalVariable.VariableName);
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var valueTypeLocalVariable = DeclareAndInitializeValueTypeLocalVariable();
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloca, valueTypeLocalVariable.VariableName);
            var accessedMember = Context.SemanticModel.GetSymbolInfo(node).Symbol.EnsureNotNull();
            if (accessedMember.ContainingType.SpecialType == SpecialType.System_ValueType)
            {
                Context.EmitCilInstruction(ilVar, OpCodes.Constrained, ResolvedStructType());
            }
        }

        public override void VisitCastExpression(CastExpressionSyntax node)
        {
            var valueTypeLocalVariable = DeclareAndInitializeValueTypeLocalVariable();
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloc, valueTypeLocalVariable.VariableName);
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            var instantiatedType = ResolvedStructType();
            var visitor = new NoArgsValueTypeObjectCreatingInAssignmentVisitor(Context, ilVar, instantiatedType, DeclareAndInitializeValueTypeLocalVariable);
            node.Left.Accept(visitor);
            TargetOfAssignmentIsValueType = visitor.TargetOfAssignmentIsValueType;
        }

        public override void VisitArgument(ArgumentSyntax node)
        {
            var valueTypeLocalVariable = DeclareAndInitializeValueTypeLocalVariable();
            var loadOpCode = node.IsPassedAsInParameter(Context) ? OpCodes.Ldloca : OpCodes.Ldloc;
            Context.EmitCilInstruction(ilVar, loadOpCode, valueTypeLocalVariable.VariableName);
        }

        public bool TargetOfAssignmentIsValueType { get; private set; } = true;

        private DefinitionVariable DeclareAndInitializeValueTypeLocalVariable()
        {
            var resolvedVarType = ResolvedStructType();
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
                    break;

                case SpecialType.None:
                    InitValueTypeLocalVariable(tempLocal.VariableName);
                    break;

                default:
                    Context.WriteComment($"Instantiating {ctorInfo.Symbol.Name} is not supported.");
                    break;
            }

            return tempLocal;
        }

        private void InitValueTypeLocalVariable(string localVariable)
        {
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloca_S, localVariable);
            Context.EmitCilInstruction(ilVar, OpCodes.Initobj, ResolvedStructType());
        }
        
        private string ResolvedStructType() =>  Context.TypeResolver.Resolve(ctorInfo.Symbol.ContainingType);
    }
}
