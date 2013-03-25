using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.Extensions
{
	public static class SyntaxNodeExtensions
	{
		public static SyntaxNode WithNewLine(this SyntaxNode node)
		{
			return node.WithTrailingTrivia(Syntax.CarriageReturnLineFeed);
		}
	}
}
