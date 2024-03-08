using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;
using static Cecilifier.Core.Misc.CodeGenerationHelpers;

namespace Cecilifier.Core.AST
{
    internal partial class StatementVisitor : SyntaxWalkerBase
    {
        private static string _ilVar;

        private StatementVisitor(IVisitorContext ctx) : base(ctx)
        {
        }

        internal static void Visit(IVisitorContext context, string ilVar, CSharpSyntaxNode node)
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
            breakToInstructionVars.Push(forEndLabel);

            var forConditionLabel = AddCilInstructionWithLocalVariable(_ilVar, OpCodes.Nop);

            // Condition
            ExpressionVisitor.Visit(Context, _ilVar, node.Condition);
            Context.EmitCilInstruction(_ilVar, OpCodes.Brfalse, forEndLabel);

            // Body
            node.Statement.Accept(this);

            // Increment
            foreach (var incrementExpression in node.Incrementors)
            {
                ExpressionVisitor.VisitAndPopIfNotConsumed(Context, _ilVar, incrementExpression);
            }

            Context.EmitCilInstruction(_ilVar, OpCodes.Br, forConditionLabel);
            WriteCecilExpression(Context, $"{_ilVar}.Append({forEndLabel});");
            breakToInstructionVars.Pop();
        }

        public override void VisitSwitchStatement(SwitchStatementSyntax node)
        {
            var switchExpressionType = ResolveExpressionType(node.Expression);
            var evaluatedExpressionVariable = AddLocalVariableToCurrentMethod(Context, "switchCondition", switchExpressionType).VariableName;

            ExpressionVisitor.Visit(Context, _ilVar, node.Expression);
            Context.EmitCilInstruction(_ilVar, OpCodes.Stloc, evaluatedExpressionVariable); // stores evaluated expression in local var

            // Add label to end of switch
            var endOfSwitchLabel = CreateCilInstruction(_ilVar, Context.Naming.Label("endOfSwitch"), OpCodes.Nop);
            breakToInstructionVars.Push(endOfSwitchLabel);

            // Write the switch conditions.
            var nextTestLabels = node.Sections.Select( (_, index) =>
            {
                var labelName = Context.Naming.Label($"caseCode_{index}");
                CreateCilInstruction(_ilVar, labelName, OpCodes.Nop);
                return labelName;
            }).ToArray();

            var hasDefault = false;
            var currentLabelIndex = 0;
            foreach (var switchSection in node.Sections)
            {
                if (switchSection.Labels.First().Kind() == SyntaxKind.DefaultSwitchLabel)
                {
                    Context.EmitCilInstruction(_ilVar, OpCodes.Br, nextTestLabels[currentLabelIndex]);
                    hasDefault = true;
                    continue;
                }

                foreach (var sectionLabel in switchSection.Labels)
                {
                    using var _ = LineInformationTracker.Track(Context, sectionLabel);
                    
                    Context.WriteNewLine();
                    Context.WriteComment($"{sectionLabel.ToString()} (condition)");
                    Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, evaluatedExpressionVariable);
                    ExpressionVisitor.Visit(Context, _ilVar, sectionLabel);
                    Context.EmitCilInstruction(_ilVar, OpCodes.Beq_S, nextTestLabels[currentLabelIndex]);
                }
                currentLabelIndex++;
            }
            
            // if at runtime the code hits this point and the switch does not have a default section
            // it means none of the labels matched so just jump to the end of the switch.
            if (!hasDefault)
                Context.EmitCilInstruction(_ilVar, OpCodes.Br, endOfSwitchLabel);

            // Write the statements for each switch section...
            currentLabelIndex = 0;
            foreach (var switchSection in node.Sections)
            {
                using var _ = LineInformationTracker.Track(Context, switchSection);
                Context.WriteNewLine();
                Context.WriteComment($"{switchSection.Labels.First().ToString()} (code)");
                AddCecilExpression($"{_ilVar}.Append({nextTestLabels[currentLabelIndex]});");
                foreach (var statement in switchSection.Statements)
                {
                    statement.Accept(this);
                }
                currentLabelIndex++;
            }

            Context.WriteNewLine();
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

            Context.EmitCilInstruction(_ilVar, OpCodes.Br, breakToInstructionVars.Peek());
        }

        public override void VisitFixedStatement(FixedStatementSyntax node)
        {
            using (Context.WithFlag<ContextFlagReseter>(Constants.ContextFlags.Fixed))
            {
                var declaredType = Context.GetTypeInfo((PointerTypeSyntax) node.Declaration.Type).Type;
                var pointerType = (IPointerTypeSymbol) declaredType.EnsureNotNull();

                var currentMethodVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
                var localVar = node.Declaration.Variables[0];
                var resolvedVarType = Context.TypeResolver.Resolve(pointerType.PointedAtType).MakeByReferenceType();
                AddLocalVariableWithResolvedType(Context, localVar.Identifier.Text, currentMethodVar, resolvedVarType);
                ProcessVariableInitialization(localVar, declaredType);
            }

            Visit(node.Statement);
        }

        public override void VisitReturnStatement(ReturnStatementSyntax node)
        {
            ExpressionVisitor.Visit(Context, _ilVar, node);
            Context.EmitCilInstruction(_ilVar, OpCodes.Ret);
        }

        public override void VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            ExpressionVisitor.Visit(Context, _ilVar, node);
        }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            ExpressionVisitor.Visit(Context, _ilVar, node.Condition);

            var elsePrologVarName = Context.Naming.Label("elseEntryPoint");
            WriteCecilExpression(Context, $"var {elsePrologVarName} = {_ilVar}.Create(OpCodes.Nop);");
            Context.EmitCilInstruction(_ilVar, OpCodes.Brfalse, elsePrologVarName);

            Context.WriteComment("if body");
            node.Statement.Accept(this);

            var elseEndTargetVarName = Context.Naming.Label("elseEnd");
            WriteCecilExpression(Context, $"var {elseEndTargetVarName} = {_ilVar}.Create(OpCodes.Nop);");
            if (node.Else != null)
            {
                using var _ = LineInformationTracker.Track(Context, node.Else);
                var branchToEndOfIfStatementVarName = Context.Naming.Label("endOfIf");
                WriteCecilExpression(Context, $"var {branchToEndOfIfStatementVarName} = {_ilVar}.Create(OpCodes.Br, {elseEndTargetVarName});");
                WriteCecilExpression(Context, $"{_ilVar}.Append({branchToEndOfIfStatementVarName});");

                WriteCecilExpression(Context, $"{_ilVar}.Append({elsePrologVarName});");
                node.Else.Statement.Accept(this);
            }
            else
            {
                WriteCecilExpression(Context, $"{_ilVar}.Append({elsePrologVarName});");
            }

            WriteCecilExpression(Context, $"{_ilVar}.Append({elseEndTargetVarName});");
            WriteCecilExpression(Context, $"{Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method).VariableName}.Body.OptimizeMacros();");
            Context.WriteComment($" end if ({node.HumanReadableSummary()})");
        }

        public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            HandleVariableDeclaration(node.Declaration);
        }

        public override void VisitTryStatement(TryStatementSyntax node)
        {

            var finallyBlockHandler = node.Finally == null ?
                                (Action<object>) null :
                                _ => node.Finally.Accept(this);

            ProcessTryCatchFinallyBlock(_ilVar, node.Block, node.Catches.ToArray(), finallyBlockHandler);
        }

        public override void VisitThrowStatement(ThrowStatementSyntax node)
        {
            CecilExpressionFactory.EmitThrow(Context, _ilVar, node.Expression);
        }

        public override void VisitUsingStatement(UsingStatementSyntax node)
        {
            //https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/statements.md#1214-the-using-statement

            ExpressionVisitor.Visit(Context, _ilVar, node.Expression);
            var localVarDef = string.Empty;

            ITypeSymbol usingType;
            if (node.Declaration != null)
            {
                usingType = (ITypeSymbol) Context.SemanticModel.GetSymbolInfo(node.Declaration.Type).Symbol;
                HandleVariableDeclaration(node.Declaration);
                localVarDef = Context.DefinitionVariables.GetVariable(node.Declaration.Variables[0].Identifier.ValueText, VariableMemberKind.LocalVariable);
            }
            else
            {
                usingType = Context.SemanticModel.GetTypeInfo(node.Expression).Type;
                localVarDef = StoreTopOfStackInLocalVariable(Context, _ilVar, "tmp", usingType).VariableName;
            }

            void FinallyBlockHandler(object _)
            {
                string? lastFinallyInstructionLabel = null;
                if (usingType.TypeKind == TypeKind.TypeParameter || usingType.IsValueType)
                {
                    Context.EmitCilInstruction(_ilVar, OpCodes.Ldloca, localVarDef);
                    Context.EmitCilInstruction(_ilVar, OpCodes.Constrained, $"{localVarDef}.VariableType");
                }
                else
                {
                    lastFinallyInstructionLabel = Context.Naming.SyntheticVariable("endFinally", ElementKind.Label);

                    Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, localVarDef);
                    CreateCilInstruction(_ilVar, lastFinallyInstructionLabel, OpCodes.Nop);
                    Context.EmitCilInstruction(_ilVar, OpCodes.Brfalse, lastFinallyInstructionLabel, "check if the disposable is not null");
                    Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, localVarDef);
                }

                Context.EmitCilInstruction(_ilVar, OpCodes.Callvirt, Context.RoslynTypeSystem.SystemIDisposable.GetMembers("Dispose").OfType<IMethodSymbol>().Single().MethodResolverExpression(Context));
                if (lastFinallyInstructionLabel != null)
                    AddCecilExpression($"{_ilVar}.Append({lastFinallyInstructionLabel});");
            }

            ProcessTryCatchFinallyBlock(_ilVar, node.Statement, Array.Empty<CatchClauseSyntax>(), FinallyBlockHandler);
        }

        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) => node.Accept(new MethodDeclarationVisitor(Context));
        public override void VisitWhileStatement(WhileStatementSyntax node) { LogUnsupportedSyntax(node); }
        public override void VisitLockStatement(LockStatementSyntax node) { LogUnsupportedSyntax(node); }
        public override void VisitUnsafeStatement(UnsafeStatementSyntax node) { LogUnsupportedSyntax(node); }
        public override void VisitCheckedStatement(CheckedStatementSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitContinueStatement(ContinueStatementSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitDoStatement(DoStatementSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitGotoStatement(GotoStatementSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitYieldStatement(YieldStatementSyntax node) { LogUnsupportedSyntax(node); }

        private DefinitionVariable AddLocalVariable(TypeSyntax type, VariableDeclaratorSyntax localVar, DefinitionVariable methodVar)
        {
            var resolvedVarType = type.IsVar
                ? ResolveExpressionType(localVar.Initializer?.Value)
                : ResolveType(type);

            return AddLocalVariableWithResolvedType(Context, localVar.Identifier.Text, methodVar, resolvedVarType);
        }

        private void ProcessVariableInitialization(VariableDeclaratorSyntax localVar, ITypeSymbol variableType)
        {
            if (localVar.Initializer == null)
                return;

            var localVarDef = Context.DefinitionVariables.GetVariable(localVar.Identifier.ValueText, VariableMemberKind.LocalVariable);

            // if code is something like `Index field = ^5`; 
            // then we need to load the address of the field since the expression ^5 (IndexerExpression) will result in a call to System.Index ctor (which is a value type and expects
            // the address of the value type to be in the top of the stack
            var isIndexExpression = localVar.Initializer.Value.IsKind(SyntaxKind.IndexExpression);

            // the same applies if...
            var isDefaultLiteralExpressionOnNonPrimitiveValueType = localVar.Initializer.Value.IsKind(SyntaxKind.DefaultLiteralExpression) && variableType.IsValueType && !variableType.IsPrimitiveType();
            if (isIndexExpression || isDefaultLiteralExpressionOnNonPrimitiveValueType)
            {
                Context.EmitCilInstruction(_ilVar, OpCodes.Ldloca, localVarDef.VariableName);
            }

            if (ExpressionVisitor.Visit(Context, _ilVar, localVar.Initializer))
            {
                return;
            }

            var valueBeingAssignedIsByRef = Context.SemanticModel.GetSymbolInfo(localVar.Initializer.Value).Symbol.IsByRef();
            if (!variableType.IsByRef() && valueBeingAssignedIsByRef)
            {
                OpCode opCode = variableType.LdindOpCodeFor();
                Context.EmitCilInstruction(_ilVar, opCode);
            }

            Context.EmitCilInstruction(_ilVar, OpCodes.Stloc, localVarDef.VariableName);
        }

        private void HandleVariableDeclaration(VariableDeclarationSyntax declaration)
        {
            var variableType = Context.SemanticModel.GetTypeInfo(declaration.Type);
            var methodVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
            foreach (var localVar in declaration.Variables)
            {
                var declaredVariable = AddLocalVariable(declaration.Type, localVar, methodVar);
                using var _ = Context.DefinitionVariables.WithVariable(declaredVariable);
                ProcessVariableInitialization(localVar, variableType.Type);
            }
        }

        // Stack with name of variables that holds instructions that a *break statement* 
        // will jump to. Each statement that supports *breaking* must push the instruction
        // target of the break and pop it back when it gets out of scope.
        private Stack<string> breakToInstructionVars = new Stack<string>();
    }
}
