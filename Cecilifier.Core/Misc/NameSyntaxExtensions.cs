using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

#nullable enable annotations

namespace Cecilifier.Core.Misc
{
    public static class NameSyntaxExtensions
    {
        public static string ToSimpleName(this NameSyntax nameSyntax)
        {
            return nameSyntax.Accept(new SimpleNameExtractor());
        }
    }

    public class SimpleNameExtractor : CSharpSyntaxVisitor<string>
    {
        public override string? VisitQualifiedName(QualifiedNameSyntax node)
        {
            return node.Right.Accept(this);
        }

        public override string? VisitIdentifierName(IdentifierNameSyntax node)
        {
            return node.Identifier.Text;
        }

        public override string VisitGenericName(GenericNameSyntax node)
        {
            return $"{node.Identifier.Text}_{node.Arity}";
        }
    }
}
