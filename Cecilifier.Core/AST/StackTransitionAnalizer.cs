using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.AST
{
	class StackTransitionAnalizer : SyntaxWalker
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

		protected override void VisitExpressionStatement(ExpressionStatementSyntax node)
		{
			consumesStack = false;
		}

		private readonly SyntaxNode node;
		private bool consumesStack = true;
	}
}
