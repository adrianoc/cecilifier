using Cecilifier.Core.Extensions;
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
            AddCilInstruction(ilVar, OpCodes.Initobj, ctorInfo.Symbol.ContainingType.ResolveExpression(Context));
        }

        public override void VisitReturnStatement(ReturnStatementSyntax node)
        {
            DeclareAndInitializeValueTypeLocalVariable();
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            //base.VisitMemberAccessExpression(node);
            DeclareAndInitializeValueTypeLocalVariable();
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            new NoArgsValueTypeObjectCreatingInAssignmentVisitor(Context, ilVar).Visit(node.Left);
            AddCilInstruction(ilVar, OpCodes.Initobj, ctorInfo.Symbol.ContainingType.ResolveExpression(Context));
        }

        public override void VisitArgument(ArgumentSyntax node)
        {
            DeclareAndInitializeValueTypeLocalVariable();
        }

        private void DeclareAndInitializeValueTypeLocalVariable()
        {
            var resolvedVarType = Context.TypeResolver.Resolve(ctorInfo.Symbol.ContainingType);
            var tempLocalName = MethodExtensions.LocalVariableNameFor("tmp_", "tmp_".UniqueId().ToString());
            AddCecilExpression("var {0} = new VariableDefinition({1});", tempLocalName, resolvedVarType);

            AddCecilExpression("{0}.Body.Variables.Add({1});", Context.DefinitionVariables.GetLastOf(MemberKind.Method).VariableName, tempLocalName);

            AddCilInstruction(ilVar, OpCodes.Ldloca_S, tempLocalName);
            AddCilInstruction(ilVar, OpCodes.Initobj, ctorInfo.Symbol.ContainingType.ResolveExpression(Context));
            AddCilInstruction(ilVar, OpCodes.Ldloc, tempLocalName);
        }
    }
}
