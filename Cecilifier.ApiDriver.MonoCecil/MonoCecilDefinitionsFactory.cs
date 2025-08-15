using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Cecilifier.Core;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.ApiDriver.MonoCecil;

internal class MonoCecilDefinitionsFactory : IApiDriverDefinitionsFactory
{
    public string MappedTypeModifiersFor(INamedTypeSymbol type, SyntaxTokenList modifiers) => TypeModifiersToCecil(type, modifiers);

    public IEnumerable<string> Type(
        IVisitorContext context,
        string typeVar,
        string typeNamespace,
        string typeName,
        string attrs,
        string resolvedBaseType,
        DefinitionVariable outerTypeVariable,
        bool isStructWithNoFields,
        IEnumerable<ITypeSymbol> interfaces,
        IEnumerable<TypeParameterSyntax>? ownTypeParameters,
        IEnumerable<TypeParameterSyntax> outerTypeParameters,
        params string[] properties)
    {
        return CecilDefinitionsFactory.Type(context, typeVar, typeNamespace, typeName, attrs, resolvedBaseType, outerTypeVariable, isStructWithNoFields, interfaces, ownTypeParameters, outerTypeParameters,
            properties);
    }

    //TODO: Try to extract common code to be shared with SRM.
    private static string TypeModifiersToCecil(INamedTypeSymbol typeSymbol, SyntaxTokenList modifiers)
    {
        var hasStaticCtor = typeSymbol.Constructors.Any(ctor => ctor.IsStatic && !ctor.IsImplicitlyDeclared);
        var typeAttributes = new StringBuilder(CecilDefinitionsFactory.DefaultTypeAttributeFor(typeSymbol.TypeKind, hasStaticCtor));
        AppendStructLayoutTo(typeSymbol, typeAttributes);
        if (typeSymbol.ContainingType != null)
        {
            if (modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
            {
                typeAttributes = typeAttributes.AppendModifier(Constants.Cecil.StaticTypeAttributes);
                modifiers = modifiers.Remove(SyntaxFactory.Token(SyntaxKind.StaticKeyword));
            }

            return typeAttributes.AppendModifier(ModifiersToCecil(modifiers, m => "TypeAttributes.Nested" + m.ValueText.PascalCase())).ToString();
        }

        var convertedModifiers = ModifiersToCecil<TypeAttributes>(modifiers, "NotPublic", MapAttribute);
        return typeAttributes.AppendModifier(convertedModifiers).ToString();

        IEnumerable<string> MapAttribute(SyntaxToken token)
        {
            var isModifierWithNoILRepresentation =
                token.IsKind(SyntaxKind.PartialKeyword)
                || token.IsKind(SyntaxKind.VolatileKeyword)
                || token.IsKind(SyntaxKind.UnsafeKeyword)
                || token.IsKind(SyntaxKind.AsyncKeyword)
                || token.IsKind(SyntaxKind.ExternKeyword)
                || token.IsKind(SyntaxKind.ReadOnlyKeyword)
                || token.IsKind(SyntaxKind.RefKeyword);

            if (isModifierWithNoILRepresentation)
                return Array.Empty<string>();

            var mapped = token.Kind() switch
            {
                SyntaxKind.InternalKeyword => "NotPublic",
                SyntaxKind.ProtectedKeyword => "Family",
                SyntaxKind.PrivateKeyword => "Private",
                SyntaxKind.PublicKeyword => "Public",
                SyntaxKind.StaticKeyword => "Abstract | TypeAttributes.Sealed",
                SyntaxKind.AbstractKeyword => "Abstract",
                SyntaxKind.SealedKeyword => "Sealed",

                _ => throw new ArgumentException()
            };

            return new[] { mapped };
        }
    }

    private static void AppendStructLayoutTo(ITypeSymbol typeSymbol, StringBuilder typeAttributes)
    {
        if (typeSymbol.TypeKind != TypeKind.Struct)
            return;

        if (!typeSymbol.TryGetAttribute<StructLayoutAttribute>(out var structLayoutAttribute))
        {
            typeAttributes.AppendModifier("TypeAttributes.SequentialLayout");
        }
        else
        {
            var specifiedLayout = ((LayoutKind) structLayoutAttribute.ConstructorArguments.First().Value) switch
            {
                LayoutKind.Auto => "TypeAttributes.AutoLayout",
                LayoutKind.Explicit => "TypeAttributes.ExplicitLayout",
                LayoutKind.Sequential => "TypeAttributes.SequentialLayout",
                _ => throw new ArgumentException($"Invalid StructLayout value for {typeSymbol.Name}")
            };

            typeAttributes.AppendModifier(specifiedLayout);
        }
    }

    private static string ModifiersToCecil<TEnumAttr>(
        IEnumerable<SyntaxToken> modifiers,
        string defaultAccessibility,
        Func<SyntaxToken, IEnumerable<string>> mapAttribute) where TEnumAttr : Enum
    {
        var targetEnum = typeof(TEnumAttr).Name;

        var finalModifierList = new List<SyntaxToken>(modifiers);

        var accessibilityModifiers = string.Empty;
        IsInternalProtected(finalModifierList, ref accessibilityModifiers);
        IsPrivateProtected(finalModifierList, ref accessibilityModifiers);

        var modifierStr = finalModifierList
            .SelectMany(mapAttribute)
            .Where(attr => !string.IsNullOrEmpty(attr))
            .Aggregate(new StringBuilder(), (acc, curr) => acc.AppendModifier($"{targetEnum}.{curr}"));

        modifierStr.Append(accessibilityModifiers);

        if (!modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword) || m.IsKind(SyntaxKind.InternalKeyword) || m.IsKind(SyntaxKind.PrivateKeyword) || m.IsKind(SyntaxKind.PublicKeyword) ||
                                m.IsKind(SyntaxKind.ProtectedKeyword)))
            modifierStr.AppendModifier($"{targetEnum}.{defaultAccessibility}");

        return modifierStr.ToString();

        void IsInternalProtected(List<SyntaxToken> tokens, ref string s)
        {
            if (HandleModifiers(tokens, SyntaxKind.InternalKeyword, SyntaxKind.ProtectedKeyword))
                s = $"{targetEnum}.FamORAssem";
        }

        void IsPrivateProtected(List<SyntaxToken> tokens, ref string s)
        {
            if (HandleModifiers(tokens, SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword))
                s = $"{targetEnum}.FamANDAssem";
        }

        bool HandleModifiers(List<SyntaxToken> tokens, SyntaxKind first, SyntaxKind second)
        {
            if (tokens.Any(c => c.IsKind(first)) && tokens.Any(c => c.IsKind(second)))
            {
                tokens.RemoveAll(c => c.IsKind(first) || c.IsKind(second));
                return true;
            }

            return false;
        }
    }

    private static string ModifiersToCecil(IEnumerable<SyntaxToken> modifiers, Func<SyntaxToken, string> map)
    {
        var cecilModifierStr = modifiers.Aggregate(new StringBuilder(), (acc, token) =>
        {
            acc.AppendModifier(map(token));
            return acc;
        });

        return cecilModifierStr.ToString();
    }
}
