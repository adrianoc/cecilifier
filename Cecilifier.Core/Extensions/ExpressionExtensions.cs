using System.Linq;
using System.Text;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.Extensions
{
    public static class ExpressionExtensions
    {
        internal static string EvaluateAsCustomAttributeArgument(this ExpressionSyntax expression, IVisitorContext context)
        {
            return expression.Accept(new CustomAttributeArgumentEvaluator(context));
        }
    }

    public class CustomAttributeArgumentEvaluator : CSharpSyntaxVisitor<string>
    {
        private readonly IVisitorContext _context;

        internal CustomAttributeArgumentEvaluator(IVisitorContext context)
        {
            _context = context;
        }

        public override string VisitTypeOfExpression(TypeOfExpressionSyntax node)
        {
            var typeSymbol = _context.SemanticModel.GetTypeInfo(node.Type);
            return _context.TypeResolver.Resolve(typeSymbol.Type);
        }

        public override string VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var type = _context.SemanticModel.GetTypeInfo(node.Expression);
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

        public override string VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
        {
            if (node.Initializer == null)
            {
                return $"new CustomAttributeArgument[{node.Type.RankSpecifiers[0].Sizes[0]}]";
            }
            
            var elementType = _context.TypeResolver.Resolve(_context.SemanticModel.GetTypeInfo(node.Type.ElementType).Type);
            return CustomAttributeArgumentArray(node.Initializer, elementType);
        }

        public override string VisitImplicitArrayCreationExpression(ImplicitArrayCreationExpressionSyntax node)
        {
            var elementType = _context.TypeResolver.Resolve(_context.SemanticModel.GetTypeInfo(node.Initializer.Expressions[0]).Type);
            return CustomAttributeArgumentArray(node.Initializer, elementType);
        }

        private string CustomAttributeArgumentArray(InitializerExpressionSyntax initializer, string elementType)
        {
            var sb = new StringBuilder($"new CustomAttributeArgument[] {{");
            foreach (var exp in initializer.Expressions)
            {
                sb.Append($"new CustomAttributeArgument({elementType}, {exp.Accept(this)}),");
            }

            sb.Append("}");
            return sb.ToString();
        }
    }
}
