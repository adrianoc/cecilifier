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

        internal AssignmentVisitor(IVisitorContext ctx, string ilVar, AssignmentExpressionSyntax node) : base(ctx)
        {
            this.ilVar = ilVar;
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
                var operand = stelemOpCode == OpCodes.Stelem_Any ? Context.TypeResolver.Resolve(arrayElementType) : null ;
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
            
            // push `implicit this` (target of the assignment) to the stack if needed.
            if (!member.Symbol.IsStatic 

                && member.Symbol.Kind != SymbolKind.Parameter // Parameters/Locals are never leafs in a MemberReferenceExpression
                && member.Symbol.Kind != SymbolKind.Local
                
                && !node.Parent.IsKind(SyntaxKind.SimpleMemberAccessExpression))
            { 
                InsertCilInstructionAfter<string>(InstructionPrecedingValueToLoad, ilVar, OpCodes.Ldarg_0);
            }

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
                    FieldAssignment(field);
                    break;
                
                case IPropertySymbol property:
                    PropertyAssignment(node, property);
                    break;
            }
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

        bool HandleIndexer(ElementAccessExpressionSyntax node, LinkedListNode<string> lastInstructionLoadingRhs)
        {
            var expSymbol = Context.SemanticModel.GetSymbolInfo(node).Symbol;
            if (expSymbol is not IPropertySymbol propertySymbol)
            {
                return false;
            }

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
                OpCode opCode = propertySymbol.Type.Stind();
                Context.EmitCilInstruction(ilVar, opCode);
            }
            else
            {
                Context.MoveLinesToEnd(InstructionPrecedingValueToLoad, lastInstructionLoadingRhs);
                AddMethodCall(ilVar, propertySymbol.SetMethod);
            }

            return true;
        }
        
        private void PropertyAssignment(IdentifierNameSyntax node, IPropertySymbol property)
        {
            // If there's no setter it means we have a getter only auto property and it is being
            // initialized in the ctor in which case we need to assign directly to the backing field.
            if (property.SetMethod == null)
            {
                var propertyBackingFieldName = Utils.BackingFieldNameForAutoProperty(property.Name);
                var found = Context.DefinitionVariables.GetVariable(propertyBackingFieldName, VariableMemberKind.Field, property.ContainingType.Name);
                Context.EmitCilInstruction(ilVar, OpCodes.Stfld, found.VariableName);
            }
            else
                AddMethodCall(ilVar, property.SetMethod, isAccessOnThisOrObjectCreation:node.IsAccessOnThisOrObjectCreation());
        }

        private void FieldAssignment(IFieldSymbol field)
        {
            var storeOpCode = field.IsStatic ? OpCodes.Stsfld : OpCodes.Stfld;
            if (field.IsVolatile)
                Context.EmitCilInstruction(ilVar, OpCodes.Volatile);

            var operand = Context.DefinitionVariables.GetVariable(field.Name, VariableMemberKind.Field, field.ContainingType.Name).VariableName;
            Context.EmitCilInstruction(ilVar, storeOpCode, operand);
        }

        private void LocalVariableAssignment(ILocalSymbol localVariable)
        {
            string operand = Context.DefinitionVariables.GetVariable(localVariable.Name, VariableMemberKind.LocalVariable).VariableName;
            Context.EmitCilInstruction(ilVar, OpCodes.Stloc, operand);
        }

        private void ParameterAssignment(IParameterSymbol parameter)
        {
            if (parameter.RefKind == RefKind.None)
            {
                if (parameter.Type.TypeKind == TypeKind.Array)
                {
                    Context.EmitCilInstruction(ilVar, OpCodes.Stelem_Ref);
                }
                else
                {
                    var paramVariable = Context.DefinitionVariables.GetVariable(parameter.Name, VariableMemberKind.Parameter).VariableName;
                    Context.EmitCilInstruction(ilVar, OpCodes.Starg_S, paramVariable);
                }
            }
            else
            {
                Context.EmitCilInstruction(ilVar, parameter.Type.Stind());
            }
        }

        void PreProcessRefOutAssignments(ExpressionSyntax node)
        {
            var paramSymbol = ParameterVisitor.Process(Context, node);
            if (paramSymbol != null && paramSymbol.RefKind != RefKind.None)
            {
                ProcessParameter(ilVar, (IdentifierNameSyntax) node, paramSymbol);
            }
        }
    }
}
