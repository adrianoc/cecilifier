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
            var last = Context.CurrentLine;
            ExpressionVisitor.Visit(Context, ilVar, node.Expression);
            foreach (var arg in node.ArgumentList.Arguments)
            {
                ExpressionVisitor.Visit(Context, ilVar, arg);
            }
            Context.MoveLinesToEnd(InstructionPrecedingValueToLoad, last);
            
            var expSymbol = Context.SemanticModel.GetSymbolInfo(node).Symbol;
            if (expSymbol is IPropertySymbol propertySymbol)
            {
                AddMethodCall(ilVar, propertySymbol.SetMethod);
            }
            else
            {
                AddCilInstruction(ilVar, OpCodes.Stelem_Ref);
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
            Utils.EnsureNotNull(member.Symbol == null, $"Failed to resolve symbol for node: {node.SourceDetails()}.");

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
                    PropertyAssignment(property);
                    break;
            }
        }

        private void PropertyAssignment(IPropertySymbol property)
        {
            AddMethodCall(ilVar, property.SetMethod, isAccessOnThisOrObjectCreation:false);
        }

        private void FieldAssignment(IFieldSymbol field)
        {
            var storeOpCode = field.IsStatic ? OpCodes.Stsfld : OpCodes.Stfld;
            if (field.IsVolatile)
                AddCilInstruction(ilVar, OpCodes.Volatile);
            
            AddCilInstruction(ilVar, storeOpCode, Context.DefinitionVariables.GetVariable(field.Name, VariableMemberKind.Field, field.ContainingType.Name).VariableName);
        }

        private void LocalVariableAssignment(ILocalSymbol localVariable)
        {
            AddCilInstruction(ilVar, OpCodes.Stloc, Context.DefinitionVariables.GetVariable(localVariable.Name, VariableMemberKind.LocalVariable).VariableName);
        }

        private void ParameterAssignment(IParameterSymbol parameter)
        {
            if (parameter.RefKind == RefKind.None)
            {
                var paramVariable = Context.DefinitionVariables.GetVariable(parameter.Name, VariableMemberKind.Parameter).VariableName;
                if (parameter.Type.TypeKind == TypeKind.Array)
                {
                    AddCilInstruction(ilVar, OpCodes.Stelem_Ref);
                }
                else
                {
                    AddCilInstruction(ilVar, OpCodes.Starg_S, paramVariable);
                }
            }
            else
            {
                var opCode = parameter.Type.SpecialType switch
                {
                    SpecialType.None => OpCodes.Stind_I2,
                    SpecialType.System_Char => OpCodes.Stind_I2,
                    SpecialType.System_Int16 => OpCodes.Stind_I2,

                    SpecialType.System_Int32 => OpCodes.Stind_I4,
                    SpecialType.System_Single => OpCodes.Stind_R4,
                    _ => parameter.Type.IsReferenceType
                        ? OpCodes.Stind_Ref
                        : throw new NotSupportedException($"Assignment to ref/out parameters of type {parameter.Type} not supported yet.")
                };
                
                AddCilInstruction(ilVar, opCode);
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
