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
            var tempLocalName = MethodExtensions.LocalVariableNameFor("tmp_", "tmp_".UniqueId().ToString());
            AddCecilExpression("var {0} = new VariableDefinition({1});", tempLocalName, resolvedVarType);

            AddCecilExpression("{0}.Body.Variables.Add({1});", Context.DefinitionVariables.GetLastOf(MemberKind.Method).VariableName, tempLocalName);

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
                    AddCilInstruction(ilVar, OpCodes.Ldloca_S, tempLocalName);
                    AddCilInstruction(ilVar, OpCodes.Initobj, Context.TypeResolver.Resolve(ctorInfo.Symbol.ContainingType));
                    
                    //TODO: Loading the value is likely wrong; it may be necessary
                    //      to load the address of the value type if the parent
                    //      is a method invocation.
                    AddCilInstruction(ilVar, OpCodes.Ldloc, tempLocalName);
                    break;
                
                default:
                    Context.WriteComment($"Instantiating {ctorInfo.Symbol.Name} is not supported.");
                    break;
            }
        }
    }
}
