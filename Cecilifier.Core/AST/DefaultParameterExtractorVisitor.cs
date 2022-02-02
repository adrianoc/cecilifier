using System;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST;

internal class DefaultParameterExtractorVisitor : CSharpSyntaxVisitor<string>
{
    public static DefaultParameterExtractorVisitor Instance { get; private set; }

    internal static void Initialize(IVisitorContext context)
    {
        if (Instance?.context != context)
            Instance = new DefaultParameterExtractorVisitor(context);
    }

    private DefaultParameterExtractorVisitor(IVisitorContext context)
    {
        this.context = context;
    }

    public override string VisitParameter(ParameterSyntax node)
    {
        if (node.Default == null)
            return null;
            
        return node.Default.Value.Accept(this);
    }

    public override string VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        return $"{node.OperatorToken}{node.Operand.Accept(this)}";
    }

    public override string VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        var literalValue = node.ValueText();
        if (node.IsKind(SyntaxKind.NumericLiteralExpression) && literalValue.Contains('.') && context.GetTypeInfo(node).Type?.MetadataToken == context.RoslynTypeSystem.SystemSingle.MetadataToken)
            literalValue += "f";
        
        return literalValue;
    }

    public override string? VisitTypeOfExpression(TypeOfExpressionSyntax node)
    {
        throw new NotImplementedException();
    }
    
    private readonly IVisitorContext context;
}
