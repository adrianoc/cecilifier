using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Cecilifier.Core.Extensions
{
    public static class SyntaxNodeExtensions
    {
        public static T WithNewLine<T>(this T node) where T : SyntaxNode
        {
            return node.WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
        }

        public static string HumanReadableSummary(this SyntaxNode node)
        {
            var found = true;
            var nodesAndTokens = node.ChildNodesAndTokens().ToArray().TakeWhile( c =>
            {
                var previous = found;
                found = !c.HasTrailingTrivia || !c.GetTrailingTrivia().Any(t => t.IsKind(SyntaxKind.EndOfLineTrivia));
                return previous;
            }).ToArray();

            var s = new StringBuilder();
            
            // remove leading trivias of first node/token...
            if (nodesAndTokens[0].IsNode)
            {
                s.Append(nodesAndTokens[0].AsNode().WithoutLeadingTrivia().ToFullString());
            }
            else
            {
                s.Append(nodesAndTokens[0]);
                foreach(var ld in nodesAndTokens[0].GetTrailingTrivia())
                    s.Append(ld);
            }

            foreach (var item in nodesAndTokens.Skip(1))
            {
                s.Append(item.ToFullString());
            }

            return s.ToString();
        }
    }
}
