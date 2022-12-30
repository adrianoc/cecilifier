using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    internal class IfStatementVisitor : SyntaxWalkerBase
    {
        private static string _ilVar;

        private IfStatementVisitor(IVisitorContext ctx) : base(ctx)
        {
        }

        internal static void Visit(IVisitorContext context, string ilVar, IfStatementSyntax node)
        {
            _ilVar = ilVar;
            node.Accept(new IfStatementVisitor(context));
        }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            ExpressionVisitor.Visit(Context, _ilVar, node.Condition);

            var elsePrologVarName = Context.Naming.Label("elseEntryPoint");
            WriteCecilExpression(Context, $"var {elsePrologVarName} = {_ilVar}.Create(OpCodes.Nop);");
            Context.EmitCilInstruction(_ilVar, OpCodes.Brfalse, elsePrologVarName);

            Context.WriteComment("if body");
            StatementVisitor.Visit(Context, _ilVar, node.Statement);

            var elseEndTargetVarName = Context.Naming.Label("elseEnd");
            WriteCecilExpression(Context, $"var {elseEndTargetVarName} = {_ilVar}.Create(OpCodes.Nop);");
            if (node.Else != null)
            {
                using var _ = LineInformationTracker.Track(Context, node.Else);
                var branchToEndOfIfStatementVarName = Context.Naming.Label("endOfIf");
                WriteCecilExpression(Context, $"var {branchToEndOfIfStatementVarName} = {_ilVar}.Create(OpCodes.Br, {elseEndTargetVarName});");
                WriteCecilExpression(Context, $"{_ilVar}.Append({branchToEndOfIfStatementVarName});");

                WriteCecilExpression(Context, $"{_ilVar}.Append({elsePrologVarName});");
                StatementVisitor.Visit(Context, _ilVar, node.Else.Statement);
            }
            else
            {
                WriteCecilExpression(Context, $"{_ilVar}.Append({elsePrologVarName});");
            }

            WriteCecilExpression(Context, $"{_ilVar}.Append({elseEndTargetVarName});");
            WriteCecilExpression(Context, $"{Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method).VariableName}.Body.OptimizeMacros();");
            Context.WriteComment($" end if ({node.HumanReadableSummary()})");
        }
    }
}
