using System;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.Extensions;

internal static class TypesExtensions
{
    public static string NameFrom(this TypeSyntax type, bool expandAttributeName = false)
    {
        return type switch
        {
            ArrayTypeSyntax arrayTypeSyntax => NameFrom(arrayTypeSyntax.ElementType),
            AliasQualifiedNameSyntax aliasQualifiedNameSyntax => throw new NotImplementedException(),
            FunctionPointerTypeSyntax functionPointerTypeSyntax => functionPointerTypeSyntax.ToString(),
            GenericNameSyntax genericNameSyntax => NameFromIdentifier(genericNameSyntax.Identifier, expandAttributeName),
            IdentifierNameSyntax identifierNameSyntax => NameFromIdentifier(identifierNameSyntax.Identifier, expandAttributeName),
            QualifiedNameSyntax qualifiedNameSyntax => qualifiedNameSyntax.ToString(),
            SimpleNameSyntax simpleNameSyntax => simpleNameSyntax.Identifier.Text,
            NullableTypeSyntax nullableTypeSyntax => NameFrom(nullableTypeSyntax.ElementType),
            OmittedTypeArgumentSyntax omittedTypeArgumentSyntax => throw new NotImplementedException(),
            PointerTypeSyntax pointerTypeSyntax => NameFrom(pointerTypeSyntax.ElementType),
            PredefinedTypeSyntax predefinedTypeSyntax => predefinedTypeSyntax.Keyword.Text,
            RefTypeSyntax refTypeSyntax => NameFrom(refTypeSyntax.Type),
            TupleTypeSyntax tupleTypeSyntax => tupleTypeSyntax.ToString(),
            
            _ => throw new InvalidOperationException($"Unexpected syntax: {type}"), 
        };

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
