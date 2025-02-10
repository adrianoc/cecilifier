using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.Extensions;

internal static class TypeSyntaxExtensions
{
    public static string NameFrom(this TypeSyntax type, bool expandAttributeName = false)
    {
        // Note that we don´t expect `type` to ever be a `SimpleNameSyntax` since this type is abstract.
        return type switch
        {
            ArrayTypeSyntax arrayTypeSyntax => NameFrom(arrayTypeSyntax.ElementType),
            AliasQualifiedNameSyntax aliasQualifiedNameSyntax => aliasQualifiedNameSyntax.ToString(),
            FunctionPointerTypeSyntax functionPointerTypeSyntax => functionPointerTypeSyntax.ToString(),
            GenericNameSyntax genericNameSyntax => NameFromIdentifier(genericNameSyntax.Identifier, expandAttributeName),
            IdentifierNameSyntax identifierNameSyntax => NameFromIdentifier(identifierNameSyntax.Identifier, expandAttributeName),
            QualifiedNameSyntax qualifiedNameSyntax => qualifiedNameSyntax.ToString(),
            NullableTypeSyntax nullableTypeSyntax => NameFrom(nullableTypeSyntax.ElementType),
            OmittedTypeArgumentSyntax omittedTypeArgumentSyntax => omittedTypeArgumentSyntax.Parent?.Parent?.ToString(),
            PointerTypeSyntax pointerTypeSyntax => NameFrom(pointerTypeSyntax.ElementType),
            PredefinedTypeSyntax predefinedTypeSyntax => predefinedTypeSyntax.Keyword.Text,
            RefTypeSyntax refTypeSyntax => NameFrom(refTypeSyntax.Type),
            TupleTypeSyntax tupleTypeSyntax => tupleTypeSyntax.ToString(),
            _ => ThrowCannotHappen() 
        };

        [ExcludeFromCodeCoverage]
        string ThrowCannotHappen()
        {
            throw new InvalidOperationException($"Unexpected syntax: {type} ({type.GetType().Name})");
        }

        static string NameFromIdentifier(SyntaxToken identifierToken, bool expandAttributeName)
        {
            var typeName = identifierToken.Text;
            if (identifierToken.Parent!.Parent.IsKind(SyntaxKind.Attribute) && expandAttributeName && !typeName.EndsWith("Attribute"))
            {
                return $"{typeName}Attribute";
            }
            
            return typeName;
        }
    }

    public static string NameFrom(this BaseTypeDeclarationSyntax typeDeclaration)
    {
        var sb = new StringBuilder();
        var parent = typeDeclaration.Parent;
        while (parent != null && parent is NamespaceDeclarationSyntax namespaceDeclaration)
        {
            sb.Append($"{namespaceDeclaration.Name}.");
            parent = parent.Parent;
        }

        sb.Append(typeDeclaration.Identifier.Text);

        return sb.ToString();
    }
}
