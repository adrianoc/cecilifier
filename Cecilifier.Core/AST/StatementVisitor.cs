using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    internal class StatementVisitor : SyntaxWalkerBase
    {
        private static string _ilVar;
        private HashSet<SyntaxKind> _ignoreForLogging = new HashSet<SyntaxKind>(new[]
        {
            SyntaxKind.Block,
            SyntaxKind.TryStatement,
            SyntaxKind.CatchClause,
        });

        internal StatementVisitor(IVisitorContext ctx) : base(ctx)
        {
        }

        internal static void Visit(IVisitorContext context, string ilVar, StatementSyntax node)
        {
            _ilVar = ilVar;
            node.Accept(new StatementVisitor(context));
        }

        public override void VisitYieldStatement(YieldStatementSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitForStatement(ForStatementSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitWhileStatement(WhileStatementSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitSwitchStatement(SwitchStatementSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitLockStatement(LockStatementSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitUnsafeStatement(UnsafeStatementSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitFixedStatement(FixedStatementSyntax node)
        {
            using (Context.WithFlag("fixed"))
            {
                HandleVariableDeclaration(node.Declaration);
            }
            
            Visit(node.Statement);
        }

        public override void VisitBlock(BlockSyntax node)
        {
            using (Context.DefinitionVariables.EnterScope())
            {
                base.VisitBlock(node);
            }
        }

        public override void VisitReturnStatement(ReturnStatementSyntax node)
        {
            ExpressionVisitor.Visit(Context, _ilVar, node.Expression);
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

            void SetTryStart(string instVar)
            {
                Context.InstructionAdded -= SetTryStart;
                exceptionHandlerTable[0].TryStart = instVar;
            }

            Context.InstructionAdded += SetTryStart;

            node.Block.Accept(this);

            var firstInstructionAfterTryCatchBlock = CreateCilInstruction(_ilVar, OpCodes.Nop);
            exceptionHandlerTable[exceptionHandlerTable.Length - 1].HandlerEnd = firstInstructionAfterTryCatchBlock; // sets up last handler end instruction

            AddCilInstruction(_ilVar, OpCodes.Leave, firstInstructionAfterTryCatchBlock);

            for (var i = 0; i < node.Catches.Count; i++)
            {
                HandleCatchClause(node.Catches[i], exceptionHandlerTable, i, firstInstructionAfterTryCatchBlock);
            }

            HandleFinallyClause(node.Finally, exceptionHandlerTable);

            AddCecilExpression($"{_ilVar}.Append({firstInstructionAfterTryCatchBlock});");

            WriteExceptionHandlers(exceptionHandlerTable);
        }

        private void WriteExceptionHandlers(ExceptionHandlerEntry[] exceptionHandlerTable)
        {
            string methodVar = Context.DefinitionVariables.GetLastOf(MemberKind.Method);
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
            exceptionHandlerTable[currentIndex].HandlerStart = AddCilInstruction(_ilVar, OpCodes.Pop); // pops the exception object from stack...

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

            void SetFinallyStart(string instVar)
            {
                Context.InstructionAdded -= SetFinallyStart;
                exceptionHandlerTable[finallyEntryIndex].HandlerStart = instVar;
                exceptionHandlerTable[finallyEntryIndex].TryEnd = instVar;

                if (finallyEntryIndex != 0)
                {
                    // We have one or more catch blocks... set the end of the last catch block as the first instruction of the *finally*
                    exceptionHandlerTable[finallyEntryIndex - 1].HandlerEnd = instVar;
                }
            }

            Context.InstructionAdded += SetFinallyStart;

            base.VisitFinallyClause(node);
            AddCilInstruction(_ilVar, OpCodes.Endfinally);
        }

        private void AddLocalVariable(TypeSyntax type, VariableDeclaratorSyntax localVar, string methodVar)
        {
            var isFixedStatement = Context.HasFlag("fixed");
            if (isFixedStatement)
            {
                type = ((PointerTypeSyntax) type).ElementType;
            }
            
            var resolvedVarType = type.IsVar
                ? ResolveExpressionType(localVar.Initializer.Value)
                : ResolveType(type);


            if (isFixedStatement)
            {
                resolvedVarType = $"{resolvedVarType}.MakeByReferenceType()";
            }
            
            var cecilVarDeclName = TempLocalVar($"lv_{localVar.Identifier.ValueText}");
            AddCecilExpression("var {0} = new VariableDefinition({1});", cecilVarDeclName, resolvedVarType);
            AddCecilExpression("{0}.Body.Variables.Add({1});", methodVar, cecilVarDeclName);

            Context.DefinitionVariables.RegisterNonMethod(string.Empty, localVar.Identifier.ValueText, MemberKind.LocalVariable, cecilVarDeclName);
        }

        private void ProcessVariableInitialization(VariableDeclaratorSyntax localVar)
        {
            if (ExpressionVisitor.Visit(Context, _ilVar, localVar.Initializer))
            {
                return;
            }

            var localVarDef = Context.DefinitionVariables.GetVariable(localVar.Identifier.ValueText, MemberKind.LocalVariable);
            AddCilInstruction(_ilVar, OpCodes.Stloc, localVarDef.VariableName);
        }

        private void HandleVariableDeclaration(VariableDeclarationSyntax declaration)
        {
            var methodVar = Context.DefinitionVariables.GetLastOf(MemberKind.Method).VariableName;
            foreach (var localVar in declaration.Variables)
            {
                AddLocalVariable(declaration.Type, localVar, methodVar);
                ProcessVariableInitialization(localVar);
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
    }
}
