using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST
{
	class StackTransitionAnalizer : CSharpSyntaxWalker
	{
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

		private readonly SyntaxNode node;
		private bool consumesStack = true;
	}
}
