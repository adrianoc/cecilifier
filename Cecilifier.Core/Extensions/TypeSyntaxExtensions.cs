using System;
using System.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.Extensions;

internal static class TypesExtensions
{
    public static string NameFrom(this TypeSyntax type)
    {
        return type switch
        {
            ArrayTypeSyntax arrayTypeSyntax => NameFrom(arrayTypeSyntax.ElementType),
            AliasQualifiedNameSyntax aliasQualifiedNameSyntax => throw new NotImplementedException(),
            FunctionPointerTypeSyntax functionPointerTypeSyntax => functionPointerTypeSyntax.ToString(),
            GenericNameSyntax genericNameSyntax => genericNameSyntax.Identifier.Text,
            IdentifierNameSyntax identifierNameSyntax => identifierNameSyntax.Identifier.Text,
            QualifiedNameSyntax qualifiedNameSyntax => qualifiedNameSyntax.ToString(),
            SimpleNameSyntax simpleNameSyntax => simpleNameSyntax.Identifier.Text,
            NullableTypeSyntax nullableTypeSyntax => NameFrom(nullableTypeSyntax.ElementType),
            OmittedTypeArgumentSyntax omittedTypeArgumentSyntax => throw new NotImplementedException(),
            PointerTypeSyntax pointerTypeSyntax => NameFrom(pointerTypeSyntax.ElementType),
            PredefinedTypeSyntax predefinedTypeSyntax => predefinedTypeSyntax.Keyword.Text,
            RefTypeSyntax refTypeSyntax => NameFrom(refTypeSyntax.Type),
            TupleTypeSyntax tupleTypeSyntax => tupleTypeSyntax.ToString(),

            NameSyntax nameSyntax => throw new InvalidOperationException($"Unexpected name syntax: {nameSyntax}"),
        };
    }

    public static string NameFrom(this BaseTypeDeclarationSyntax typeDeclaration)
    {
        var sb = new StringBuilder();
        var parent = typeDeclaration.Parent;
        while (parent != null && parent is NamespaceDeclarationSyntax namespaceDeclaration)
        {
            sb.AppendFormat(namespaceDeclaration.Name.ToString());
            sb.Append('.');

            parent = parent.Parent;
        }

        sb.Append(typeDeclaration.Identifier.Text);

        return sb.ToString();
    }
}
