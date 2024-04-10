using System;
using System.Collections.Generic;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    internal class AssignmentVisitor : SyntaxWalkerBase
    {
        private readonly string ilVar;
        private readonly AssignmentExpressionSyntax assignment;

        internal AssignmentVisitor(IVisitorContext ctx, string ilVar, AssignmentExpressionSyntax node) : base(ctx)
        {
            this.ilVar = ilVar;
            assignment = node;

            PreProcessRefOutAssignments(node.Left);
        }

        internal AssignmentVisitor(IVisitorContext ctx, string ilVar) : base(ctx)
        {
            this.ilVar = ilVar;
        }

        public LinkedListNode<string> InstructionPrecedingValueToLoad { get; set; }

        /*
         * ExpressionVisitor (the caller that ends up triggering this method) assumes
         * that it needs to visit the right node followed by the left one. For array
         * element access this will cause issues because the value to be stored
         * need to be loaded after the array reference and the index, i.e, after
         * visiting the *left* node.
         *
         * To fix this we remember the instruction that precedes the load instructions
         * and move all the instructions from that point until the first instruction
         * added by visiting the left node.
         */
        public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        {
            var lastInstructionLoadingRhs = Context.CurrentLine;
            if (InlineArrayProcessor.TryHandleIntIndexElementAccess(Context, ilVar, node, out var elementType))
            {
                Context.MoveLinesToEnd(InstructionPrecedingValueToLoad, lastInstructionLoadingRhs);
                Context.EmitCilInstruction(ilVar, elementType.StindOpCodeFor());
                return;
            }
            
            ExpressionVisitor.Visit(Context, ilVar, node.Expression);
            foreach (var arg in node.ArgumentList.Arguments)
            {
                ExpressionVisitor.Visit(Context, ilVar, arg);
            }
            
            if (!HandleIndexer(node, lastInstructionLoadingRhs))
            {
                Context.MoveLinesToEnd(InstructionPrecedingValueToLoad, lastInstructionLoadingRhs);
                var arrayElementType = Context.SemanticModel.GetTypeInfo(node).Type.EnsureNotNull();
                var stelemOpCode = arrayElementType.StelemOpCode();
                var operand = stelemOpCode == OpCodes.Stelem_Any ? Context.TypeResolver.Resolve(arrayElementType) : null;
                Context.EmitCilInstruction(ilVar, stelemOpCode, operand);
            }
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var last = Context.CurrentLine;
            ExpressionVisitor.Visit(Context, ilVar, node.Expression);
            Context.MoveLinesToEnd(InstructionPrecedingValueToLoad, last);

            node.Name.Accept(this);
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            //TODO: tuple declaration with an initializer is represented as an assignment
            //      revisit the following if/when we handle tuples
            if (node.IsVar)
            {
                return;
            }

            var member = Context.SemanticModel.GetSymbolInfo(node);
            Utils.EnsureNotNull(member.Symbol, $"Failed to resolve symbol for node: {node.SourceDetails()}.");

            if (member.Symbol.Kind != SymbolKind.NamedType
                && member.Symbol.ContainingType.IsValueType
                && node.Parent is ObjectCreationExpressionSyntax { ArgumentList: { Arguments: { Count: 0 } } })
            {
                return;
            }

            LoadImplicitTargetForMemberReference(node, member.Symbol);
            AddCallToOpImplicitIfRequired(node);

            switch (member.Symbol)
            {
                case IParameterSymbol parameter:
                    ParameterAssignment(parameter);
                    break;

                case ILocalSymbol local:
                    LocalVariableAssignment(local);
                    break;

                case IFieldSymbol field:
                    FieldAssignment(field, node);
                    break;

                case IPropertySymbol property:
                    PropertyAssignment(node, property);
                    break;
            }
        }

        public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.PointerIndirectionExpression))
            {
                var last = Context.CurrentLine;
                ExpressionVisitor.Visit(Context, ilVar, node.Operand);
                Context.MoveLinesToEnd(InstructionPrecedingValueToLoad, last);
            }

            base.VisitPrefixUnaryExpression(node);
        }

        // In code like var x = new Foo { ["someValue"] = 1 };
        // '["someValue] = 1' is represented as an implicit element access.
        // The object being instantiated ('Foo' in this case) must have a compatible
        // indexer (in this case one taking a string and returning an integer).
        public override void VisitImplicitElementAccess(ImplicitElementAccessSyntax node)
        {
            var lastInstructionLoadingRhs = Context.CurrentLine;

            Context.EmitCilInstruction(ilVar, OpCodes.Dup);
            foreach (var arg in node.ArgumentList.Arguments)
            {
                ExpressionVisitor.Visit(Context, ilVar, arg.Expression);
                HandleIndexer(node, lastInstructionLoadingRhs);
            }

            base.VisitImplicitElementAccess(node);
        }

        private void AddCallToOpImplicitIfRequired(IdentifierNameSyntax node)
        {
            if (node.Parent is not AssignmentExpressionSyntax assignmentExpression || assignmentExpression.Left != node)
                return;

            var conversion = Context.SemanticModel.ClassifyConversion(assignmentExpression.Right, Context.SemanticModel.GetTypeInfo(node).Type);
            if (conversion.IsImplicit && conversion.MethodSymbol != null
                                      && !conversion.IsMethodGroup) // method group to delegate conversions should not call the method being converted...
            {
                AddMethodCall(ilVar, conversion.MethodSymbol);
            }
        }

        // push `implicit this` (target of the assignment) or target reference in an object initializer expression to the stack if needed.
        void LoadImplicitTargetForMemberReference(IdentifierNameSyntax node, ISymbol memberSymbol)
        {
            if (memberSymbol is IFieldSymbol { RefKind: not RefKind.None } && !assignment.Right.IsKind(SyntaxKind.RefExpression))
                return;

            if (!memberSymbol.IsStatic

                && memberSymbol.Kind != SymbolKind.Parameter // Parameters/Locals are never leafs in a MemberReferenceExpression
                && memberSymbol.Kind != SymbolKind.Local

                && !node.Parent.IsKind(SyntaxKind.SimpleMemberAccessExpression))
            {
                // we have either 1) an assignment to member in object initializer. For instance, var x = new Foo { Value = 1 }
                // i.e, `Value = 1`, in which case the stack top will contain the reference to the newly instantiate object;
                // in this case we only need to duplicate the top of the stack, or 2) an implicit reference to `this`.
                var loadOpCode = node.Parent != null && node.Parent.Parent.IsKind(SyntaxKind.ObjectInitializerExpression)
                                        ? OpCodes.Dup
                                        : OpCodes.Ldarg_0;

                Context.WriteCilInstructionAfter<string>(ilVar, loadOpCode, null, null, InstructionPrecedingValueToLoad);
            }
        }

        bool HandleIndexer(SyntaxNode node, LinkedListNode<string> lastInstructionLoadingRhs)
        {
            var expSymbol = Context.SemanticModel.GetSymbolInfo(node).Symbol;
            if (expSymbol is not IPropertySymbol propertySymbol)
                return false;

            if (propertySymbol.RefKind == RefKind.Ref)
            {
                // in this case we have something like `span[1] = CalculateValue()` and we need
                // to generate the code like:
                //
                // 1) load `ref` to be assigned to, i.e span.get_Item()
                // 2) load value to assign, i.e CalculateValue()
                //
                // so we emit the call to get_item() and then move the instructions generated for `CalculateValue()`
                // bellow it.
                AddMethodCall(ilVar, propertySymbol.GetMethod);
                Context.MoveLinesToEnd(InstructionPrecedingValueToLoad, lastInstructionLoadingRhs);
                EmitIndirectStore(propertySymbol.Type);
            }
            else
            {
                Context.MoveLinesToEnd(InstructionPrecedingValueToLoad, lastInstructionLoadingRhs);
                AddMethodCall(ilVar, propertySymbol.SetMethod);
            }

            return true;
        }
        private void EmitIndirectStore(ITypeSymbol typeBeingStored)
        {
            var indirectStoreOpCode = typeBeingStored.StindOpCodeFor();
            Context.EmitCilInstruction(ilVar, indirectStoreOpCode, indirectStoreOpCode == OpCodes.Stobj ? Context.TypeResolver.Resolve(typeBeingStored.ElementTypeSymbolOf()) : null);
        }

        private void PropertyAssignment(IdentifierNameSyntax node, IPropertySymbol property)
        {
            property.EnsurePropertyExists(Context, node);
            // If there's no setter it means we have a getter only auto property and it is being
            // initialized in the ctor in which case we need to assign directly to the backing field.
            if (property.SetMethod == null)
            {
                var propertyBackingFieldName = Utils.BackingFieldNameForAutoProperty(property.Name);
                var found = Context.DefinitionVariables.GetVariable(propertyBackingFieldName, VariableMemberKind.Field, property.ContainingType.ToDisplayString());
                Context.EmitCilInstruction(ilVar, OpCodes.Stfld, found.VariableName);
            }
            else
                AddMethodCall(ilVar, property.SetMethod, isAccessOnThisOrObjectCreation: node.Parent.IsAccessOnThisOrObjectCreation());
        }

        private void FieldAssignment(IFieldSymbol field, IdentifierNameSyntax name)
        {
            if (field.IsVolatile)
                Context.EmitCilInstruction(ilVar, OpCodes.Volatile);

            var fieldDefinitionVariable = field.EnsureFieldExists(Context, name);
            MemberAssignment(field.Type, field.RefKind, fieldDefinitionVariable, field.StoreOpCodeForFieldAccess());
        }

        private void LocalVariableAssignment(ILocalSymbol localVariable)
        {
            var localVariableVar = Context.DefinitionVariables.GetVariable(localVariable.Name, VariableMemberKind.LocalVariable);
            MemberAssignment(localVariable.Type, localVariable.RefKind, localVariableVar, OpCodes.Stloc);
        }

        private void ParameterAssignment(IParameterSymbol parameter)
        {
            var paramVariable = Context.DefinitionVariables.GetVariable(parameter.Name, VariableMemberKind.Parameter, parameter.ContainingSymbol.ToDisplayString());
            MemberAssignment(parameter.Type, parameter.RefKind, paramVariable, OpCodes.Starg_S);
        }

        private void MemberAssignment(ITypeSymbol memberType, RefKind memberRefKind, DefinitionVariable memberDefinitionVariable, OpCode storeOpCode)
        {
            if (NeedsIndirectStore(memberType, memberRefKind))
            {
                EmitIndirectStore(memberType);
            }
            else
            {
                if (!memberDefinitionVariable.IsValid)
                {
                    throw new InvalidOperationException("Invalid definition variable");
                }
                Context.EmitCilInstruction(ilVar, storeOpCode, memberDefinitionVariable.VariableName);
            }
        }

        //TODO: the logic to decide whether an assignment should be an indirect store looks to be wrong
        //      at least the pointer check looks to be missing a case like:
        //      int *p, *p1 = null;
        //      p = p1; // <-- *p* is a pointer and right (p1) is not an AddressOffExpression but still,
        //              //     this is a simple store, not an indirect one.
        private bool NeedsIndirectStore(ITypeSymbol assignmentTargetMemberType, RefKind assignmentTargetMemberRefKind) =>
            (assignmentTargetMemberType is IPointerTypeSymbol && !assignment.Right.IsKind(SyntaxKind.AddressOfExpression))
            || assignmentTargetMemberRefKind != RefKind.None && !assignment.Right.IsKind(SyntaxKind.RefExpression);

        /// <summary>
        /// When assigning a value to a *ref like* member, the generated IL needs to load
        /// the address of the target storage before loading the value to be stored. For instance,
        /// 
        /// void Foo(ref int i) => i = 10;
        ///
        /// needs to generate something like:
        /// ldarga i
        /// ldc.i4, 10
        /// stind.i4
        ///
        /// so in this case we visit the left side of the assignment twice; the 1st one
        /// calls this method and is responsible to load the address to be indirectly
        /// assigned (in the example above, the "ldarga i" instruction) and the second
        /// one generates the indirect assignment ("stind.i4" instruction in the example
        /// above).
        /// </summary>
        /// <param name="node"></param>
        void PreProcessRefOutAssignments(ExpressionSyntax node)
        {
            if (assignment.Right.IsKind(SyntaxKind.RefExpression))
                return;

            var symbol = Context.SemanticModel.GetSymbolInfo(node).Symbol;
            if (symbol is IParameterSymbol { RefKind: not RefKind.None } parameterSymbol)
            {
                ProcessParameter(ilVar, (IdentifierNameSyntax) node, parameterSymbol);
            }
            else if (symbol is IFieldSymbol { RefKind: not RefKind.None } fieldSymbol)
            {
                ProcessField(ilVar, (IdentifierNameSyntax) node, fieldSymbol);
            }
            else if (symbol is ILocalSymbol { RefKind: not RefKind.None } localSymbol)
            {
                ProcessLocalVariable(ilVar, (IdentifierNameSyntax) node, localSymbol);
            }
        }
    }
}
