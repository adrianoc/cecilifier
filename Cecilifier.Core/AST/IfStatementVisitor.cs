using Microsoft.CodeAnalysis.CSharp.Syntax;

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

            var elsePrologVarName = TempLocalVar("esp");
            WriteCecilExpression(Context, $"var {elsePrologVarName} = {_ilVar}.Create(OpCodes.Nop);");
            WriteCecilExpression(Context, $"{_ilVar}.Append({_ilVar}.Create(OpCodes.Brfalse, {elsePrologVarName}));");

            WriteCecilExpression(Context, "//if body");
            StatementVisitor.Visit(Context, _ilVar, node.Statement);

            var elseEndTargetVarName = TempLocalVar("ese");
            WriteCecilExpression(Context, $"var {elseEndTargetVarName} = {_ilVar}.Create(OpCodes.Nop);");
            if (node.Else != null)
            {
                var branchToEndOfIfStatementVarName = LocalVariableNameForId(NextLocalVariableTypeId());
                WriteCecilExpression(Context, $"var {branchToEndOfIfStatementVarName} = {_ilVar}.Create(OpCodes.Br, {elseEndTargetVarName});");
                WriteCecilExpression(Context, $"{_ilVar}.Append({branchToEndOfIfStatementVarName});");

                WriteCecilExpression(Context, $"{_ilVar}.Append({elsePrologVarName});");
                ExpressionVisitor.Visit(Context, _ilVar, node.Else);
            }
            else
            {
                WriteCecilExpression(Context, $"{_ilVar}.Append({elsePrologVarName});");
            }

            WriteCecilExpression(Context, $"{_ilVar}.Append({elseEndTargetVarName});");
            WriteCecilExpression(Context, $"{Context.DefinitionVariables.GetLastOf(MemberKind.Method).VariableName}.Body.OptimizeMacros();");
            WriteCecilExpression(Context, $"// end if ({node.Condition})");
        }
    }
}
