using Cecilifier.Core.Extensions;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST;

internal class DefaultParameterExtractorVisitor : CSharpSyntaxVisitor<DefaultValue>
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

    public override DefaultValue VisitParameter(ParameterSyntax node)
    {
        if (node.Default == null)
            return default;

        return node.Default.Value.Accept(this);
    }

    public override DefaultValue VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        return $"{node.OperatorToken}{node.Operand.Accept(this).Value}";
    }

    public override DefaultValue VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.DefaultLiteralExpression))
            return context.GetTypeInfo(node).Type.ValueForDefaultLiteral() ?? "null";

        var literalValue = node.ValueText();
        if (node.IsKind(SyntaxKind.NumericLiteralExpression) && literalValue.Contains('.') && context.GetTypeInfo(node).Type?.MetadataToken == context.RoslynTypeSystem.SystemSingle.MetadataToken)
            literalValue += "f";

        return literalValue;
    }

    public override DefaultValue VisitDefaultExpression(DefaultExpressionSyntax node) => context.GetTypeInfo(node.Type).Type.ValueForDefaultLiteral();

    private readonly IVisitorContext context;
}
