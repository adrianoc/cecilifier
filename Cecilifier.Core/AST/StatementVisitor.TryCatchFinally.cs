using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

#nullable enable annotations
namespace Cecilifier.Core.AST
{
    internal partial class StatementVisitor
    {
        private void ProcessTryCatchFinallyBlock<TState>(string ilVar, CSharpSyntaxNode tryStatement, CatchClauseSyntax[] catches, Action<TState>? finallyBlockHandler, TState state = default)
        {
            ProcessWithInTryCatchFinallyBlock(ilVar, _ => tryStatement.Accept(this), catches, finallyBlockHandler, state);
        }

        private void ProcessWithInTryCatchFinallyBlock<TState>(string ilVar, Action<TState> toProcess, CatchClauseSyntax[] catches, Action<TState> finallyBlockHandler, TState state)
        {
            var exceptionHandlerTable = new ExceptionHandlerEntry[catches.Length + (finallyBlockHandler != null ? 1 : 0)];

            Context.WriteNewLine();
            Context.WriteComment("Try start");
            var tryStartVar = AddCilInstructionWithLocalVariable(ilVar, OpCodes.Nop);
            exceptionHandlerTable[0].TryStart = tryStartVar;

            toProcess(state);

            Context.WriteNewLine();
            Context.WriteComment("Try end");
            
            var firstInstructionAfterTryCatchBlock = CreateCilInstruction(ilVar, OpCodes.Nop);
            exceptionHandlerTable[^1].HandlerEnd = firstInstructionAfterTryCatchBlock; // sets up last handler end instruction

            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Leave, firstInstructionAfterTryCatchBlock);

            for (var i = 0; i < catches.Length; i++)
            {
                HandleCatchClause(ilVar, catches[i], exceptionHandlerTable, i, firstInstructionAfterTryCatchBlock);
            }

            HandleFinallyClause(ilVar, finallyBlockHandler, exceptionHandlerTable, state);

            AddCecilExpression($"{ilVar}.Append({firstInstructionAfterTryCatchBlock});");

            WriteExceptionHandlers(exceptionHandlerTable);
        }

        private void WriteExceptionHandlers(IEnumerable<ExceptionHandlerEntry> exceptionHandlerTable)
        {
            string methodVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
            foreach (var handlerEntry in exceptionHandlerTable)
            {
                AddCecilExpression($"{methodVar}.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.{handlerEntry.Kind})");
                AddCecilExpression("{");
                if (handlerEntry.Kind == Mono.Cecil.Cil.ExceptionHandlerType.Catch)
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

        private void HandleCatchClause(string ilVar, CatchClauseSyntax node, ExceptionHandlerEntry[] exceptionHandlerTable, int currentIndex, string firstInstructionAfterTryCatchBlock)
        {
            exceptionHandlerTable[currentIndex].Kind = Mono.Cecil.Cil.ExceptionHandlerType.Catch;
            exceptionHandlerTable[currentIndex].HandlerStart = AddCilInstructionWithLocalVariable(ilVar, OpCodes.Pop); // pops the exception object from stack...

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
            exceptionHandlerTable[currentIndex].CatchType = ResolveType(node.Declaration!.Type, ResolveTargetKind.None);

            VisitCatchClause(node);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Leave, firstInstructionAfterTryCatchBlock);
        }

        private void HandleFinallyClause<TState>(string ilVar, Action<TState> finallyBlockHandler, ExceptionHandlerEntry[] exceptionHandlerTable, TState state)
        {
            if (finallyBlockHandler == null)
                return;

            var finallyEntryIndex = exceptionHandlerTable.Length - 1;

            exceptionHandlerTable[finallyEntryIndex].TryStart = exceptionHandlerTable[0].TryStart;
            exceptionHandlerTable[finallyEntryIndex].TryEnd = exceptionHandlerTable[0].TryEnd;
            exceptionHandlerTable[finallyEntryIndex].Kind = Mono.Cecil.Cil.ExceptionHandlerType.Finally;

            Context.WriteNewLine();
            Context.WriteComment("finally start");
            var finallyStartVar = AddCilInstructionWithLocalVariable(ilVar, OpCodes.Nop);
            exceptionHandlerTable[finallyEntryIndex].HandlerStart = finallyStartVar;
            exceptionHandlerTable[finallyEntryIndex].TryEnd = finallyStartVar;

            if (finallyEntryIndex != 0)
            {
                // We have one or more catch blocks... set the end of the last catch block as the first instruction of the *finally*
                exceptionHandlerTable[finallyEntryIndex - 1].HandlerEnd = finallyStartVar;
            }

            finallyBlockHandler(state);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Endfinally);
            
            Context.WriteNewLine();
            Context.WriteComment("finally end");
        }
        
        private struct ExceptionHandlerEntry
        {
            public Mono.Cecil.Cil.ExceptionHandlerType Kind;
            public ResolvedType CatchType;
            public string TryStart;
            public string TryEnd;
            public string HandlerStart;
            public string HandlerEnd;
        }
    }
}
