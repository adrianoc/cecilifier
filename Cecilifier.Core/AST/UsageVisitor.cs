using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST;

internal class UsageVisitor : CSharpSyntaxVisitor<UsageKind>
{
    public static UsageVisitor GetInstance(IVisitorContext context)
    {
        if (_instance == null)
            _instance = new UsageVisitor(context);
        else if (_instance.context != context)
            throw new ArgumentException($"{nameof(UsageVisitor)} has been initialized with a different context.");

        return _instance;
    }

    internal static void ResetInstance() => _instance = null;

    private static UsageVisitor _instance;

    private readonly IVisitorContext context;

    private UsageVisitor(IVisitorContext context)
    {
        this.context = context;
    }

    public override UsageKind VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        if (node?.Parent.IsKind(SyntaxKind.InvocationExpression) == true)
            return UsageKind.CallTarget;

        var t = context.SemanticModel.GetSymbolInfo(node);
        return t.Symbol?.Kind is SymbolKind.Property or SymbolKind.Event or SymbolKind.Method
            ? UsageKind.CallTarget
            : UsageKind.None;
    }

    public override UsageKind VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(node).Symbol as IPropertySymbol;
        return symbol?.IsIndexer == true ? UsageKind.CallTarget : UsageKind.None;
    }
}
