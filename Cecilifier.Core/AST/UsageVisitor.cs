using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST;

internal class UsageVisitor : CSharpSyntaxVisitor<UsageResult>
{
    public static UsageVisitor GetInstance(IVisitorContext context)
    {
        if (_instance == null)
            _instance = new UsageVisitor(context);
        else if (_instance.context != context)
            throw new ArgumentException($"{nameof(UsageVisitor)} has been initialized with a different context.");

        return _instance;
    }

    public UsageVisitor WithTargetNode(CSharpSyntaxNode node)
    {
        _targetNode = node;
        return this;
    } 
    
    internal static void ResetInstance() => _instance = null;

    private static UsageVisitor _instance;

    private readonly IVisitorContext context;
    private CSharpSyntaxNode _targetNode;

    private UsageVisitor(IVisitorContext context)
    {
        this.context = context;
    }

    public override UsageResult VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var t = context.SemanticModel.GetSymbolInfo(node);
        if (node.Parent.IsKind(SyntaxKind.InvocationExpression))
            return new UsageResult(UsageKind.CallTarget, t.Symbol);

        var kind = t.Symbol?.Kind is SymbolKind.Property or SymbolKind.Event or SymbolKind.Method
            ? UsageKind.CallTarget
            : UsageKind.None;

        return NewUsageResult(kind, t.Symbol);
    }

    public override UsageResult VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(node).Symbol as IPropertySymbol;
        var kind = symbol?.IsIndexer == true ? UsageKind.CallTarget : UsageKind.None;
        return NewUsageResult(kind, symbol);
    }

    public override UsageResult VisitForEachStatement(ForEachStatementSyntax node)
    {
        if (node.Expression != _targetNode)
            return NewUsageResult(UsageKind.None, null);

        // if _targetNode is the enumerable (i.e, the `Expression`) in the foreach
        // it means we will end up calling `GetEnumerator()` on it.
        var symbol = context.SemanticModel.GetForEachStatementInfo(node);
        return NewUsageResult(UsageKind.CallTarget, symbol.GetEnumeratorMethod);
    }
    
    private UsageResult NewUsageResult(UsageKind kind, ISymbol symbol)
    {
        _targetNode = null;
        return new UsageResult(kind, symbol);
    }
}
