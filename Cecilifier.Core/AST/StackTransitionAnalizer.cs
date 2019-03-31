using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST
{
    internal class StackTransitionAnalizer : CSharpSyntaxWalker
    {
        private readonly SyntaxNode node;
        private bool consumesStack = true;

        public StackTransitionAnalizer(SyntaxNode node)
        {
            this.node = node;
        }

        public bool ConsumesStack()
        {
            Visit(node);
            return consumesStack;
        }

        public override void VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            consumesStack = false;
        }
    }
}
