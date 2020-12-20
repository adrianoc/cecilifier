using System;
using System.Collections.Generic;
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

            // Counts the # of instructions used to load the value to be stored
            var c = InstructionPrecedingValueToLoad;
            int instCount = 0;
            while (c != last)
            {
                c = c.Next;
                instCount++;
            }

            // move the instruction after the instructions that loads the array reference
            // and index.
            c = InstructionPrecedingValueToLoad.Next;
            while (instCount-- > 0)
            {
                var next = c.Next;
                Context.MoveLineAfter(c, Context.CurrentLine);
                c = next;
            }

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

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            //TODO: tuple declaration with an initializer is represented as an assignment
            //      revisit the following if when we handle tuples
            if (node.IsVar)
            {
                return;
            }

            var member = Context.SemanticModel.GetSymbolInfo(node);
            if (member.Symbol.ContainingType.IsValueType && node.Parent.Kind() == SyntaxKind.ObjectCreationExpression && ((ObjectCreationExpressionSyntax) node.Parent).ArgumentList.Arguments.Count == 0)
            {
                return;
            }

            switch (member.Symbol.Kind)
            {
                case SymbolKind.Parameter:
                    ParameterAssignment((IParameterSymbol) member.Symbol);
                    break;

                case SymbolKind.Local:
                    LocalVariableAssignment(member.Symbol as ILocalSymbol);
                    break;

                case SymbolKind.Field:
                    FieldAssignment(member.Symbol as IFieldSymbol);
                    break;
                
                case SymbolKind.Property:
                    PropertyAssignment(member.Symbol as IPropertySymbol);
                    break;
            }
        }

        private void PropertyAssignment(IPropertySymbol property)
        {
            if (!property.IsStatic)
            {
                InsertCilInstructionAfter<string>(InstructionPrecedingValueToLoad, ilVar, OpCodes.Ldarg_0);
            }

            AddMethodCall(ilVar, property.SetMethod, isAccessOnThisOrObjectCreation:false);
        }

        private void FieldAssignment(IFieldSymbol field)
        {
            OpCode storeOpCode;
            if (field.IsStatic)
            {
                storeOpCode = OpCodes.Stsfld;
            }
            else
            {
                storeOpCode = OpCodes.Stfld;
                InsertCilInstructionAfter<string>(InstructionPrecedingValueToLoad, ilVar, OpCodes.Ldarg_0);
            }

            if (field.IsVolatile)
                AddCilInstruction(ilVar, OpCodes.Volatile);
            
            AddCilInstruction(ilVar, storeOpCode, Context.DefinitionVariables.GetVariable(field.Name, MemberKind.Field, field.ContainingType.Name).VariableName);
        }

        private void LocalVariableAssignment(ILocalSymbol localVariable)
        {
            AddCilInstruction(ilVar, OpCodes.Stloc, Context.DefinitionVariables.GetVariable(localVariable.Name, MemberKind.LocalVariable).VariableName);
        }

        private void ParameterAssignment(IParameterSymbol parameter)
        {
            if (parameter.RefKind == RefKind.None)
            {
                var paramVariable = Context.DefinitionVariables.GetVariable(parameter.Name, MemberKind.Parameter).VariableName;
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
                var opCode = OpCodes.Stind_Ref;
                switch (parameter.Type.SpecialType)
                {
                    case SpecialType.None:
                    case SpecialType.System_Char:
                    case SpecialType.System_Int16:
                        opCode = OpCodes.Stind_I2;
                        break;

                    case SpecialType.System_Int32:
                        opCode = OpCodes.Stind_I4;
                        break;

                    case SpecialType.System_Single:
                        opCode = OpCodes.Stind_R4;
                        break;

                    default:
                        if (!parameter.Type.IsReferenceType)
                        {
                            throw new NotSupportedException(string.Format("Assinment to ref/out parameters of type {0} not supported yet.", parameter.Type));
                        }

                        break;
                }

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
