using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    internal class StatementVisitor : SyntaxWalkerBase
    {
        internal StatementVisitor(IVisitorContext ctx) : base(ctx)
        {
        }
		
        internal static void Visit(IVisitorContext context, string ilVar, StatementSyntax node)
        {
            _ilVar = ilVar;
            node.Accept(new StatementVisitor(context));
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
            var methodVar = Context.DefinitionVariables.GetLastOf(MemberKind.Method).VariableName;
            foreach(var localVar in node.Declaration.Variables)
            {
                AddLocalVariable(node.Declaration.Type, localVar, methodVar);
                ProcessVariableInitialization(localVar);
            }
        }

        public override void VisitTryStatement(TryStatementSyntax node)
        {
            var exceptionHandlerTable = new ExceptionHandlerEntry[node.Catches.Count + 1 + (node.Finally != null ? 1 : 0)];
            void SetTryStart(string instVar)
            {
                Context.InstructionAdded -= SetTryStart;
                exceptionHandlerTable[0].TryStart = instVar;
            }
            Context.InstructionAdded += SetTryStart;
            
            node.Block.Accept(this);
            
            var firstInstructionAfterTryCatchBlock = CreateCilInstruction(_ilVar, OpCodes.Nop);
            exceptionHandlerTable[exceptionHandlerTable.Length - 1].HandlerEnd = firstInstructionAfterTryCatchBlock; // sets up last handler end instruction
            
            using(Context.DefinitionVariables.WithCurrent(string.Empty, Context.DefinitionVariables.GetLastOf(MemberKind.Method), MemberKind.TryCatchLeaveTarget, firstInstructionAfterTryCatchBlock))
            {
                AddCilInstruction(_ilVar, OpCodes.Leave, firstInstructionAfterTryCatchBlock );
               
                for (int i =0; i < node.Catches.Count; i++)
                {
                    HandleCatchClause(node.Catches[i], exceptionHandlerTable, i + 1);
                }
    
                HandleFinallyClause(node.Finally, exceptionHandlerTable, node.Catches.Count);
            }
            
            AddCecilExpression($"{_ilVar}.Append({firstInstructionAfterTryCatchBlock});");
        }
        
        private void HandleCatchClause(CatchClauseSyntax node, ExceptionHandlerEntry[] exceptionHandlerTable, int currentIndex)
        {
            exceptionHandlerTable[currentIndex].HandlerStart = AddCilInstruction(_ilVar, OpCodes.Pop); // pops the exception object from stack...
            exceptionHandlerTable[currentIndex - 1].HandlerEnd = exceptionHandlerTable[currentIndex].HandlerStart;

            if (currentIndex == 1)
            {
                // The last instruction of the try block is the first instruction of the first catch block
                exceptionHandlerTable[0].TryEnd = exceptionHandlerTable[currentIndex].HandlerStart;
            }

            exceptionHandlerTable[currentIndex].TryStart = exceptionHandlerTable[0].TryStart;
            exceptionHandlerTable[currentIndex].TryEnd = exceptionHandlerTable[0].TryEnd;

            VisitCatchClause(node);

            /*var catchStart = AddCilInstruction(_ilVar, OpCodes.Pop); // pops the exception object from stack...
            if (_tryCatchState.TryEnd == null)
                _tryCatchState.TryEnd = catchStart;
            
            VisitCatchClause(node);

            AddCilInstruction(_ilVar, OpCodes.Leave, Context.DefinitionVariables.GetLastOf(MemberKind.TryCatchLeaveTarget).VariableName);
            var catchEnd= Context.DefinitionVariables.GetLastOf(MemberKind.TryCatchLeaveTarget).VariableName;

            string methodVar = Context.DefinitionVariables.GetLastOf(MemberKind.Method);

            AddCecilExpression($"{methodVar}.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)");
            AddCecilExpression("{");
            AddCecilExpression($"    CatchType = {ResolveType(node.Declaration.Type)},");
            AddCecilExpression($"    TryStart = {_tryCatchState.TryStart},");
            AddCecilExpression($"    TryEnd = {_tryCatchState.TryEnd},");
            AddCecilExpression($"    HandlerStart = {catchStart},");
            AddCecilExpression($"    HandlerEnd = {catchEnd}");
            AddCecilExpression("});");*/
        }

        private void HandleFinallyClause(FinallyClauseSyntax node, ExceptionHandlerEntry[] exceptionHandlerTable, int finallyEntryIndex)
        {
            if (node == null)
                return;

            exceptionHandlerTable[finallyEntryIndex].TryStart = exceptionHandlerTable[0].TryStart;
            exceptionHandlerTable[finallyEntryIndex].TryEnd = exceptionHandlerTable[0].TryEnd;

            var finallyStart = string.Empty;
            void SetFinallyStart(string instVar)
            {
                Context.InstructionAdded -= SetFinallyStart;
                exceptionHandlerTable[finallyEntryIndex].HandlerStart = instVar;
                exceptionHandlerTable[finallyEntryIndex - 1].HandlerEnd = instVar;

                if (finallyEntryIndex == 1)
                {
                    // We have only try/finally blocks, so the end of the try (index 0) is the first instruction of the *finally*
                    exceptionHandlerTable[0].TryEnd = instVar;
                }
            }
            Context.InstructionAdded += SetFinallyStart;
            
            base.VisitFinallyClause(node);
            AddCilInstruction(_ilVar, OpCodes.Leave, Context.DefinitionVariables.GetLastOf(MemberKind.TryCatchLeaveTarget).VariableName);
        }
        
//        public override void VisitFinallyClause(FinallyClauseSyntax node)
//        {
//            var finallyStart = string.Empty;
//            void SetFinallyStart(string instVar)
//            {
//                Context.InstructionAdded -= SetFinallyStart;
//                finallyStart = instVar;
//            }
//            Context.InstructionAdded += SetFinallyStart;
//            
//            base.VisitFinallyClause(node);
//            AddCilInstruction(_ilVar, OpCodes.Leave, Context.DefinitionVariables.GetLastOf(MemberKind.TryCatchLeaveTarget).VariableName);
//            var finallyEnd= AddCilInstruction(_ilVar, OpCodes.Nop);
//            
//            var methodVar = Context.DefinitionVariables.GetLastOf(MemberKind.Method).VariableName;
//            AddCecilExpression($"{methodVar}.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)");
//            AddCecilExpression("{");
//            AddCecilExpression($"    TryStart = {_tryCatchState.TryStart},");
//            AddCecilExpression($"    TryEnd = {_tryCatchState.TryEnd},");
//            AddCecilExpression($"    HandlerStart = {finallyStart},");
//            AddCecilExpression($"    HandlerEnd = {finallyEnd}");
//            AddCecilExpression("});");
//        }
//        
        struct ExceptionHandlerEntry
        {
            public ExceptionHandlerType Kind;
            public string CatchType;
            public string TryStart;
            public string TryEnd;
            public string HandlerStart;
            public string HandlerEnd;
        }
        
        private void AddLocalVariable(TypeSyntax type, VariableDeclaratorSyntax localVar, string methodVar)
        {
            string resolvedVarType = type.IsVar 
                ? ResolveExpressionType(localVar.Initializer.Value)
                : ResolveType(type);

            var cecilVarDeclName = TempLocalVar($"lv_{localVar.Identifier.ValueText}");
            AddCecilExpression("var {0} = new VariableDefinition({1});", cecilVarDeclName, resolvedVarType);
            AddCecilExpression("{0}.Body.Variables.Add({1});", methodVar, cecilVarDeclName);

            Context.DefinitionVariables.Register(string.Empty, localVar.Identifier.ValueText, MemberKind.LocalVariable, cecilVarDeclName);
        }

        private void ProcessVariableInitialization(VariableDeclaratorSyntax localVar)
        {
            if (ExpressionVisitor.Visit(Context, _ilVar, localVar.Initializer)) return;

            var localVarDef = Context.DefinitionVariables.GetVariable(localVar.Identifier.ValueText, MemberKind.LocalVariable);
            AddCilInstruction(_ilVar, OpCodes.Stloc, localVarDef.VariableName);
        }

        private static string _ilVar;
        private TryCatchState _tryCatchState;
    }
    
    struct TryCatchState
    {
        public string TryStart;
        public string TryEnd
        {
            get => _tryEnd;
            set 
            { 
                CatchStart = value;
                _tryEnd = value;
            }
        }
        public string CatchStart { get; set; }

        private string _tryEnd;
    }    
}