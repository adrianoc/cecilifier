using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST;

internal class UsageVisitor : CSharpSyntaxVisitor<UsageKind>
{
    private readonly IVisitorContext context;

    public UsageVisitor(IVisitorContext context)
    {
        this.context = context;
    }

    public override UsageKind VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        if (node?.Parent.IsKind(SyntaxKind.InvocationExpression) == true)
            return UsageKind.CallTarget;

        return UsageKind.None;
    }

    public override UsageKind VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(node).Symbol as IPropertySymbol;
        return symbol?.IsIndexer == true ? UsageKind.CallTarget : UsageKind.None;
    }
}
