using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.Extensions
{
	public static class SyntaxNodeExtensions
	{
		public static T WithNewLine<T>(this T node) where T : SyntaxNode
		{
			return node.WithTrailingTrivia(Syntax.CarriageReturnLineFeed);
		}
	}
}
