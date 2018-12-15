using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.Extensions
{
    public static class ExpressionExtensions
    {
        public static string EvaluateConstantExpression(this ExpressionSyntax expression, SemanticModel semanticModel)
        {
            return expression.Accept(new ConstantEvaluator(semanticModel));
        }
    }

    public class ConstantEvaluator : CSharpSyntaxVisitor<string>
    {
        private readonly SemanticModel _semanticModel;

        public ConstantEvaluator(SemanticModel semanticModel)
        {
            _semanticModel = semanticModel;
        }

        public override string VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var type = _semanticModel.GetTypeInfo(node.Expression);
            if (type.Type != null && type.Type.TypeKind == TypeKind.Enum)
            {
                // if this is a enum member reference, return the enum member value.
                var enumMember = (IFieldSymbol) type.Type.GetMembers().Single(member => member.Kind == SymbolKind.Field && member.IsStatic && member.Name == node.Name.Identifier.ValueText);
                return enumMember.ConstantValue.ToString();
            }
            
            return $"{node.Expression.ToString()}.{node.Name.Identifier.ValueText}";
        }

        public override string VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            switch (node.Kind())
            {
                case SyntaxKind.StringLiteralExpression:
                    return $"\"{node.Token.ValueText}\"";
                
                case SyntaxKind.NumericLiteralExpression:
                case SyntaxKind.TrueLiteralExpression:
                case SyntaxKind.FalseLiteralExpression:
                    return node.Token.ValueText;
            }
            return base.VisitLiteralExpression(node);
        }
    }
}