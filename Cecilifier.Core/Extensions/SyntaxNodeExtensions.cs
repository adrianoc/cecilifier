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

        /// <summary>
        /// Returns a human readable summary of the <paramref name="node"/> containing nodes/tokens until (including) first one with a new line trivia.
        /// </summary>
        /// <param name="node"></param>
        /// <returns>human readable summary of the <paramref name="node"/></returns>
        /// <remarks>
        /// Any leading / trailing new lines are removed
        /// </remarks>
        public static string HumanReadableSummary(this SyntaxNode node)
        {
            // ignores attribute lists since they are the parent of ParameterLists (this is odd. I'd expect the parent node of a ParameterList to be a method/property/event declaration)
            var found = true;
            var nodesAndTokens = node.ChildNodesAndTokens().ToArray().Where(t => !t.IsKind(SyntaxKind.AttributeList)).TakeWhile( c =>
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
                var leading = item.GetLeadingTrivia().Where(t => !t.IsKind(SyntaxKind.EndOfLineTrivia)).ToSyntaxTriviaList();
                s.Append(leading);
                s.Append(item);
                var trailing = item.GetTrailingTrivia().Where(t => !t.IsKind(SyntaxKind.EndOfLineTrivia)).ToSyntaxTriviaList();
                s.Append(trailing);
            }

            return s.ToString();
        }
        
        public static string SourceDetails(this SyntaxNode node) => $"{node} ({node.SyntaxTree.GetMappedLineSpan(node.Span).Span})";
    }
}
