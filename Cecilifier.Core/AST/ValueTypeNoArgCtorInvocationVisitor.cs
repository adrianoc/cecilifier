using Cecilifier.Core.Extensions;
using Cecilifier.Core.Variables;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;

namespace Cecilifier.Core.AST
{
    // Visitor used to handle instantiation of value types when an implicit,  parameterless ctor is used,
    // i.e, 'new MyValueType()'.
    //
    // It is used to abstract the fact that, in this scenario, the instructions emitted when newing a value type
    // are very different from the ones used to newing a reference type.
    //
    // A good part of this visitor deals with the case in which we need to introduce a temporary variable; this is most
    // the case when the result of the 'new T()' expression is not used as a) a direct assignment to a value type variable
    // or b) as the initializer in the declaration of the value type variable.
    //
    // This visitor expects to visit the parent of the ObjectCreationExpression, for instance, in 'M(new T())', this visitor
    // should be called to visit the method invocation.
    //
    // Note that the generated code will hardly match the generated code by a C# compiler. Empirically we observed that
    // the later may decide to emit either a 'newobj' or 'call ctor' depending on release/debug mode, whether there's
    // already storage or not allocated to store the value type, etc.
    //
    // For simplicity the only differentiation we have is based on whether the invoked constructor is an implicit,
    // parameterless one in which case this visitor is used; in all other cases the same code used to handle constructor
    // invocations on reference types is used.
    internal class ValueTypeNoArgCtorInvocationVisitor : SyntaxWalkerBase
    {
        private readonly SymbolInfo ctorInfo;
        private readonly string ilVar;
        private readonly BaseObjectCreationExpressionSyntax objectCreationExpressionSyntax;

        internal ValueTypeNoArgCtorInvocationVisitor(IVisitorContext ctx, string ilVar, BaseObjectCreationExpressionSyntax objectCreationExpressionSyntax, SymbolInfo ctorInfo) : base(ctx)
        {
            this.ctorInfo = ctorInfo;
            this.ilVar = ilVar;
            this.objectCreationExpressionSyntax = objectCreationExpressionSyntax;
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
            //local variable declaration initialized through a initializer.
            //i.e, var x = new MyStruct();
            var firstAncestorOrSelf = node.FirstAncestorOrSelf<VariableDeclarationSyntax>();
            var varName = firstAncestorOrSelf?.Variables[0].Identifier.ValueText;

            var operand = Context.DefinitionVariables.GetVariable(varName, VariableMemberKind.LocalVariable).VariableName;
            InitValueTypeLocalVariable(operand);
            TargetOfAssignmentIsValueType = true;
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
            var accessedMember = ModelExtensions.GetSymbolInfo(Context.SemanticModel, node).Symbol.EnsureNotNull();
            if (accessedMember.ContainingType.SpecialType == SpecialType.System_ValueType)
            {
                Context.SetFlag(Constants.ContextFlags.MemberReferenceRequiresConstraint, ResolvedStructType());
            }
        }

        public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            if (node.Condition == objectCreationExpressionSyntax)
            {
                Context.WriteComment("");
                Context.WriteComment($"Simple value type instantiation ('{objectCreationExpressionSyntax.ToFullString()}') is not supported as the condition of a ternary operator in the expression: {node.ToFullString()}");
                Context.WriteComment("");
                return;
            }
            
            if (node.WhenTrue != objectCreationExpressionSyntax && node.WhenFalse != objectCreationExpressionSyntax)
                return;

            if (node.WhenFalse is ObjectCreationExpressionSyntax falseExpression && (falseExpression.ArgumentList == null || falseExpression.ArgumentList.Arguments.Count == 0) &&
                node.WhenTrue is ObjectCreationExpressionSyntax trueExpression && (trueExpression.ArgumentList == null || trueExpression.ArgumentList?.Arguments.Count == 0))
            {
                // both branches are object creation expressions for parameterless value types; lets visit the base to decide whether there are already storage allocated 
                // or not and take appropriate action.
                ((CSharpSyntaxNode) node.Parent).Accept(this);
                return;
            }

            // one of the branches are not an object creation expression so we need to add a variable (for that at least),
            // initialize it and load it to the stack to be consumed, for instance as an argument of a method call.
            var valueTypeLocalVariable = DeclareAndInitializeValueTypeLocalVariable();
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloc, valueTypeLocalVariable.VariableName);
        }

        public override void VisitCastExpression(CastExpressionSyntax node)
        {
            var valueTypeLocalVariable = DeclareAndInitializeValueTypeLocalVariable();
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloc, valueTypeLocalVariable.VariableName);
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            var instantiatedType = ResolvedStructType();
            var visitor = new NoArgsValueTypeObjectCreatingInAssignmentVisitor(Context, ilVar, instantiatedType, DeclareAndInitializeValueTypeLocalVariable, objectCreationExpressionSyntax);
            node.Left.Accept(visitor);
            TargetOfAssignmentIsValueType = visitor.TargetOfAssignmentIsValueType;
        }

        public override void VisitArgument(ArgumentSyntax node)
        {
            var valueTypeLocalVariable = DeclareAndInitializeValueTypeLocalVariable();
            var loadOpCode = node.IsPassedAsInParameter(Context) ? OpCodes.Ldloca : OpCodes.Ldloc;
            Context.EmitCilInstruction(ilVar, loadOpCode, valueTypeLocalVariable.VariableName);
        }

        public bool TargetOfAssignmentIsValueType { get; private set; }

        private DefinitionVariable DeclareAndInitializeValueTypeLocalVariable()
        {
            var resolvedVarType = ResolvedStructType();
            var tempLocal = Context.AddLocalVariableToCurrentMethod("vt", resolvedVarType);
            
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

            if (objectCreationExpressionSyntax.Initializer is not null)
            {
                // To process an InitializerExpressionSyntax, ExpressionVisitor expects the top of the stack to contain
                // the reference of the object to be set.
                // Since initialisation of value types through a parameterless ctor uses the `initobj` instruction
                // at this point there's no object reference in the stack (it was consumed by the `Initobj` instruction)
                // so we push the address of the variable that we just initialised again. Notice that after processing
                // the initializer we need to pop this reference from the stack again.
                Context.EmitCilInstruction(ilVar, OpCodes.Ldloca_S, localVariable);
                ProcessInitializerIfNotNull(Context, ilVar, objectCreationExpressionSyntax.Initializer);
            }
        }

        internal static void ProcessInitializerIfNotNull(IVisitorContext context, string ilVar, InitializerExpressionSyntax initializer)
        {
            if (initializer == null)
                return;
            
            ExpressionVisitor.Visit(context, ilVar, initializer);
            context.EmitCilInstruction(ilVar, OpCodes.Pop);
        }

        private string ResolvedStructType() =>  Context.TypeResolver.ResolveAny(ctorInfo.Symbol.ContainingType);
    }
}
