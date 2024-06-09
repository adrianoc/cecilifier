using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST;

internal static class NoCapturedVariableValidator 
{
    public static bool IsValid(IVisitorContext context, SyntaxNode nodeToValidate)
    {
        var (kind, captures) = CapturesFrom(context, nodeToValidate);
        if (!captures.Any())
            return true;
            
        context.WriteComment($"{kind} that captures context are not supported. Node '{nodeToValidate.HumanReadableSummary()}' captures {string.Join(",", captures)}");
        context.WriteComment("The generated code will most likely not work.");
        return false;
    }
        
    private static (string, IList<string>) CapturesFrom(IVisitorContext context, SyntaxNode node)
    {
        var method = (context.SemanticModel.GetSymbolInfo(node).Symbol ?? context.SemanticModel.GetDeclaredSymbol(node)).EnsureNotNull<ISymbol, IMethodSymbol>();
        var captured = new List<string>();
        foreach (var identifier in node.DescendantNodes().OfType<IdentifierNameSyntax>().Where(identifier => identifier?.Parent.IsKind(SyntaxKind.SimpleMemberAccessExpression) == false))
        {
            var symbolInfo = ModelExtensions.GetSymbolInfo(context.SemanticModel, identifier);
            if ((symbolInfo.Symbol?.Kind == SymbolKind.Parameter || symbolInfo.Symbol?.Kind == SymbolKind.Local) && !SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol?.ContainingSymbol, method))
            {
                captured.Add(identifier.Identifier.Text);
            }
            else if (symbolInfo.Symbol?.Kind == SymbolKind.Field && symbolInfo.Symbol?.ContainingSymbol is ITypeSymbol containingSymbol)
            {
                if (method.MethodKind != MethodKind.LocalFunction || !SymbolEqualityComparer.Default.Equals(method.ContainingSymbol?.ContainingSymbol, containingSymbol))
                    captured.Add(identifier.Identifier.Text);
            }
        }

        var memberDescription = method.MethodKind switch
        {
            MethodKind.AnonymousFunction => "Anonymous method / lambda",
            MethodKind.LocalFunction => "Local function",
            _ => method.MethodKind.ToString()
        };
        
        return (memberDescription, captured);
    }
}
