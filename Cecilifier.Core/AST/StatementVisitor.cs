using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    internal class StatementVisitor : SyntaxWalkerBase
    {
        private static string _ilVar;

        private StatementVisitor(IVisitorContext ctx) : base(ctx)
        {
        }

        internal static void Visit(IVisitorContext context, string ilVar, StatementSyntax node)
        {
            _ilVar = ilVar;
            node.Accept(new StatementVisitor(context));
        }

        public override void Visit(SyntaxNode node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            Context.WriteNewLine();
            Context.WriteComment(node.HumanReadableSummary());
            
            base.Visit(node);
        }

        public override void VisitForStatement(ForStatementSyntax node)
        {
            // Initialization
            HandleVariableDeclaration(node.Declaration);
            foreach (var init in node.Initializers)
            {
                ExpressionVisitor.Visit(Context, _ilVar, init);
            }

            var forEndLabel = Context.Naming.Label("fel");
            WriteCecilExpression(Context, $"var {forEndLabel} = {_ilVar}.Create(OpCodes.Nop);");

            var forConditionLabel = AddCilInstructionWithLocalVariable(_ilVar, OpCodes.Nop);
            
            // Condition
            ExpressionVisitor.Visit(Context, _ilVar, node.Condition);
            AddCilInstruction(_ilVar, OpCodes.Brfalse, forEndLabel);
            
            // Body
            node.Statement.Accept(this);

            // Increment
            foreach (var incrementExpression in node.Incrementors)
            {
                ExpressionVisitor.Visit(Context, _ilVar, incrementExpression);
            }
            
            AddCilInstruction(_ilVar, OpCodes.Br, forConditionLabel);
            WriteCecilExpression(Context, $"{_ilVar}.Append({forEndLabel});");
        }
    
        public override void VisitSwitchStatement(SwitchStatementSyntax node)
        {
            var switchExpressionType = ResolveExpressionType(node.Expression);
            var evaluatedExpressionVariable = AddLocalVariable(switchExpressionType);

            ExpressionVisitor.Visit(Context, _ilVar, node.Expression);
            AddCilInstruction(_ilVar, OpCodes.Stloc, evaluatedExpressionVariable); // stores evaluated expression in local var
            
            // Add label to end of switch
            var endOfSwitchLabel = CreateCilInstruction(_ilVar, OpCodes.Nop);
            breakToInstructionVars.Push(endOfSwitchLabel);
            
            // Write the switch conditions.
            var nextTestLabels = node.Sections.Select(_ => CreateCilInstruction(_ilVar, OpCodes.Nop)).ToArray();
            var currentLabelIndex = 0;
            foreach (var switchSection in node.Sections)
            {
                if (switchSection.Labels.First().Kind() == SyntaxKind.DefaultSwitchLabel)
                {
                    AddCilInstruction(_ilVar, OpCodes.Br, nextTestLabels[currentLabelIndex]);   
                    continue;
                }
                
                foreach (var sectionLabel in switchSection.Labels)
                {
                    Context.WriteComment($"{sectionLabel.ToString()} (condition)");
                    AddCilInstruction(_ilVar, OpCodes.Ldloc, evaluatedExpressionVariable);
                    ExpressionVisitor.Visit(Context, _ilVar, sectionLabel);
                    AddCilInstruction(_ilVar, OpCodes.Beq_S, nextTestLabels[currentLabelIndex]);
                    Context.WriteNewLine();
                }
                currentLabelIndex++;
            }

            // if at runtime the code hits this point it means none of the labels matched.
            // so, just jump to the end of the switch.
            AddCilInstruction(_ilVar, OpCodes.Br, endOfSwitchLabel);
            
            // Write the statements for each switch section...
            currentLabelIndex = 0;
            foreach (var switchSection in node.Sections)
            {
                Context.WriteComment($"{switchSection.Labels.First().ToString()} (code)");
                AddCecilExpression($"{_ilVar}.Append({nextTestLabels[currentLabelIndex]});");
                foreach (var statement in switchSection.Statements)
                {
                    statement.Accept(this);
                }
                Context.WriteNewLine();
                currentLabelIndex++;
            }
            
            Context.WriteComment("End of switch");
            AddCecilExpression($"{_ilVar}.Append({endOfSwitchLabel});");

            breakToInstructionVars.Pop();
        }

        public override void VisitBreakStatement(BreakStatementSyntax node)
        {
            if (breakToInstructionVars.Count == 0)
            {
                throw new InvalidOperationException("Invalid break.");
            }

            AddCilInstruction(_ilVar, OpCodes.Br, breakToInstructionVars.Peek());
        }

        public override void VisitFixedStatement(FixedStatementSyntax node)
        {
            using (Context.WithFlag(Constants.ContextFlags.Fixed))
            {
                HandleVariableDeclaration(node.Declaration);
            }
            
            Visit(node.Statement);
        }

        public override void VisitReturnStatement(ReturnStatementSyntax node)
        {
            ExpressionVisitor.Visit(Context, _ilVar, node);
            AddCilInstruction(_ilVar, OpCodes.Ret);
        }

        public override void VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            ExpressionVisitor.Visit(Context, _ilVar, node);
        }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            IfStatementVisitor.Visit(Context, _ilVar, node);
        }

        public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            HandleVariableDeclaration(node.Declaration);
        }

        public override void VisitTryStatement(TryStatementSyntax node)
        {
            var exceptionHandlerTable = new ExceptionHandlerEntry[node.Catches.Count + (node.Finally != null ? 1 : 0)];

            var tryStartVar = AddCilInstructionWithLocalVariable(_ilVar, OpCodes.Nop);
            exceptionHandlerTable[0].TryStart = tryStartVar;
            
            node.Block.Accept(this);

            var firstInstructionAfterTryCatchBlock = CreateCilInstruction(_ilVar, OpCodes.Nop);
            exceptionHandlerTable[^1].HandlerEnd = firstInstructionAfterTryCatchBlock; // sets up last handler end instruction

            AddCilInstruction(_ilVar, OpCodes.Leave, firstInstructionAfterTryCatchBlock);

            for (var i = 0; i < node.Catches.Count; i++)
            {
                HandleCatchClause(node.Catches[i], exceptionHandlerTable, i, firstInstructionAfterTryCatchBlock);
            }

            HandleFinallyClause(node.Finally, exceptionHandlerTable);

            AddCecilExpression($"{_ilVar}.Append({firstInstructionAfterTryCatchBlock});");

            WriteExceptionHandlers(exceptionHandlerTable);
        }
        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) => node.Accept(new MethodDeclarationVisitor(Context));

        public override void VisitForEachStatement(ForEachStatementSyntax node) { LogUnsupportedSyntax(node); }
        public override void VisitWhileStatement(WhileStatementSyntax node) { LogUnsupportedSyntax(node); }
        public override void VisitLockStatement(LockStatementSyntax node) { LogUnsupportedSyntax(node); }
        public override void VisitUnsafeStatement(UnsafeStatementSyntax node) { LogUnsupportedSyntax(node); }
        public override void VisitThrowStatement(ThrowStatementSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitCheckedStatement(CheckedStatementSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitContinueStatement(ContinueStatementSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitDoStatement(DoStatementSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitGotoStatement(GotoStatementSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitYieldStatement(YieldStatementSyntax node) { LogUnsupportedSyntax(node); }
        
        private void WriteExceptionHandlers(ExceptionHandlerEntry[] exceptionHandlerTable)
        {
            string methodVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
            foreach (var handlerEntry in exceptionHandlerTable)
            {
                AddCecilExpression($"{methodVar}.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.{handlerEntry.Kind})");
                AddCecilExpression("{");
                if (handlerEntry.Kind == ExceptionHandlerType.Catch)
                {
                    AddCecilExpression($"    CatchType = {handlerEntry.CatchType},");
                }

                AddCecilExpression($"    TryStart = {handlerEntry.TryStart},");
                AddCecilExpression($"    TryEnd = {handlerEntry.TryEnd},");
                AddCecilExpression($"    HandlerStart = {handlerEntry.HandlerStart},");
                AddCecilExpression($"    HandlerEnd = {handlerEntry.HandlerEnd}");
                AddCecilExpression("});");
            }
        }

        private void HandleCatchClause(CatchClauseSyntax node, ExceptionHandlerEntry[] exceptionHandlerTable, int currentIndex, string firstInstructionAfterTryCatchBlock)
        {
            exceptionHandlerTable[currentIndex].Kind = ExceptionHandlerType.Catch;
            exceptionHandlerTable[currentIndex].HandlerStart = AddCilInstructionWithLocalVariable(_ilVar, OpCodes.Pop); // pops the exception object from stack...

            if (currentIndex == 0)
            {
                // The last instruction of the try block is the first instruction of the first catch block
                exceptionHandlerTable[0].TryEnd = exceptionHandlerTable[currentIndex].HandlerStart;
            }
            else
            {
                exceptionHandlerTable[currentIndex - 1].HandlerEnd = exceptionHandlerTable[currentIndex].HandlerStart;
            }

            exceptionHandlerTable[currentIndex].TryStart = exceptionHandlerTable[0].TryStart;
            exceptionHandlerTable[currentIndex].TryEnd = exceptionHandlerTable[0].TryEnd;
            exceptionHandlerTable[currentIndex].CatchType = ResolveType(node.Declaration.Type);

            VisitCatchClause(node);
            AddCilInstruction(_ilVar, OpCodes.Leave, firstInstructionAfterTryCatchBlock);
        }

        private void HandleFinallyClause(FinallyClauseSyntax node, ExceptionHandlerEntry[] exceptionHandlerTable)
        {
            if (node == null)
            {
                return;
            }

            var finallyEntryIndex = exceptionHandlerTable.Length - 1;

            exceptionHandlerTable[finallyEntryIndex].TryStart = exceptionHandlerTable[0].TryStart;
            exceptionHandlerTable[finallyEntryIndex].TryEnd = exceptionHandlerTable[0].TryEnd;
            exceptionHandlerTable[finallyEntryIndex].Kind = ExceptionHandlerType.Finally;

            var finallyStartVar = AddCilInstructionWithLocalVariable(_ilVar, OpCodes.Nop);
            exceptionHandlerTable[finallyEntryIndex].HandlerStart = finallyStartVar;
            exceptionHandlerTable[finallyEntryIndex].TryEnd = finallyStartVar;
            
            if (finallyEntryIndex != 0)
            {
                // We have one or more catch blocks... set the end of the last catch block as the first instruction of the *finally*
                exceptionHandlerTable[finallyEntryIndex - 1].HandlerEnd = finallyStartVar;
            }

            base.VisitFinallyClause(node);
            AddCilInstruction(_ilVar, OpCodes.Endfinally);
        }

        private void AddLocalVariable(TypeSyntax type, VariableDeclaratorSyntax localVar, DefinitionVariable methodVar)
        {
            var isFixedStatement = Context.HasFlag(Constants.ContextFlags.Fixed);
            if (isFixedStatement)
            {
                type = ((PointerTypeSyntax) type).ElementType;
            }
            
            var resolvedVarType = type.IsVar
                ? ResolveExpressionType(localVar.Initializer?.Value)
                : ResolveType(type);
            
            if (isFixedStatement)
            {
                resolvedVarType = $"{resolvedVarType}.MakeByReferenceType()";
            }
            
            AddLocalVariableWithResolvedType(localVar.Identifier.Text, methodVar, resolvedVarType);
        }

        private string AddLocalVariable(string varType)
        {
            var currentMethod = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
            if (!currentMethod.IsValid)
                throw new InvalidOperationException("Could not resolve current method declaration variable.");

            return AddLocalVariableWithResolvedType("switchCondition", currentMethod, varType);
        }

        private void ProcessVariableInitialization(VariableDeclaratorSyntax localVar, ITypeSymbol variableType)
        {
            if (localVar.Initializer == null)
                return;
            
            var localVarDef = Context.DefinitionVariables.GetVariable(localVar.Identifier.ValueText, VariableMemberKind.LocalVariable);
            if (localVar.Initializer.Value.IsKind(SyntaxKind.IndexExpression))
            {
                // code is something like `Index field = ^5`; 
                // in this case we need to load the address of the field since the expression ^5 (IndexerExpression) will result in a call to System.Index ctor (which is a value type and expects
                // the address of the value type to be in the top of the stack
                AddCilInstruction(_ilVar, OpCodes.Ldloca, localVarDef.VariableName);
            }

            if (ExpressionVisitor.Visit(Context, _ilVar, localVar.Initializer))
            {
                return;
            }

            var valueBeingAssignedIsByRef = Context.SemanticModel.GetSymbolInfo(localVar.Initializer.Value).Symbol.IsByRef();
            if (!variableType.IsByRef() && valueBeingAssignedIsByRef)
            {
                AddCilInstruction(_ilVar, LoadIndirectOpCodeFor(variableType.SpecialType));
            }
            
            AddCilInstruction(_ilVar, OpCodes.Stloc, localVarDef.VariableName);
        }

        private void HandleVariableDeclaration(VariableDeclarationSyntax declaration)
        {
            var variableType = Context.SemanticModel.GetTypeInfo(declaration.Type);
            var methodVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
            foreach (var localVar in declaration.Variables)
            {
                AddLocalVariable(declaration.Type, localVar, methodVar);
                ProcessVariableInitialization(localVar, variableType.Type);
            }
        }

        private struct ExceptionHandlerEntry
        {
            public ExceptionHandlerType Kind;
            public string CatchType;
            public string TryStart;
            public string TryEnd;
            public string HandlerStart;
            public string HandlerEnd;
        }
        
        // Stack with name of variables that holds instructions that a *break statement* 
        // will jump to. Each statement that supports *breaking* must push the instruction
        // target of the break and pop it back when it gets out of scope.
        private Stack<string> breakToInstructionVars = new Stack<string>();
    }
}
