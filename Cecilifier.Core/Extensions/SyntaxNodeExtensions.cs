using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.Extensions
{
    public static class SyntaxNodeExtensions
    {
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

        public static bool IsOperatorOnCustomUserType(this SyntaxNode self, SemanticModel semanticModel, out IMethodSymbol method)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(self);
            method = symbolInfo.Symbol as IMethodSymbol;

            // Unmanaged types https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/unmanaged-types operators are handled by the VM 
            // (as opposed to their own operator overloads) whence the check for IsUnmanagedType; the extra check (SpecialType) is meant to filter out
            // custom structs (non primitives) which may be deemed as unmanaged (see the link above for more details) 
            return method is { Parameters: { Length: > 0 } } 
                   && ((!method.Parameters[0].Type.IsUnmanagedType && method.Parameters[0].Type.SpecialType != SpecialType.System_String)|| (method.Parameters[0].Type.SpecialType == SpecialType.None && method.Parameters[0].Type.Kind != SymbolKind.PointerType));
        }

        public static IList<ParameterSyntax> ParameterList(this LambdaExpressionSyntax lambdaExpressionSyntax) => lambdaExpressionSyntax switch
        {
            SimpleLambdaExpressionSyntax simple => new [] { simple.Parameter },
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters.ToArray(),
            _ => throw new NotSupportedException($"Lambda type `{lambdaExpressionSyntax.GetType().Name}` is not supported: {lambdaExpressionSyntax}")
        };
        
        public static IList<TypeParameterSyntax> CollectOuterTypeArguments(this TypeDeclarationSyntax typeArgumentProvider)
        {
            var typeArgs = new List<TypeParameterSyntax>();
            CollectOuterTypeArguments(typeArgumentProvider, typeArgs);
            return typeArgs;
        }

        private static void CollectOuterTypeArguments(TypeDeclarationSyntax typeArgumentProvider, List<TypeParameterSyntax> collectTo)
        {
            if (typeArgumentProvider?.Parent is TypeDeclarationSyntax outer)
            {
                if (outer.TypeParameterList != null)
                    collectTo.AddRange(outer.TypeParameterList.Parameters);
                CollectOuterTypeArguments(outer, collectTo);
            }
        }
    }
}
