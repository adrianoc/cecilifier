using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Cecilifier.Core.Misc.Utils;
using MethodAttributes = Mono.Cecil.MethodAttributes;

namespace Cecilifier.Core.Extensions
{
    internal static class MethodExtensions
    {
        private static string Modifiers(this IMethodSymbol method)
        {
            var bindingFlags = method.IsStatic ? BindingFlags.Static : BindingFlags.Instance;
            bindingFlags |= method.DeclaredAccessibility == Accessibility.Public ? BindingFlags.Public : BindingFlags.NonPublic;

            var res = new StringBuilder();
            var enumType = typeof(BindingFlags);
            foreach (BindingFlags flag in Enum.GetValues(enumType))
            {
                if (bindingFlags.HasFlag(flag))
                {
                    res.Append($"|{enumType.FullName}.{flag}");
                }
            }

            return res.Length > 0 ? res.Remove(0, 1).ToString() : string.Empty;
        }

        public static string MethodResolverExpression(this IMethodSymbol method, IVisitorContext ctx)
        {
            if (method.IsDefinedInCurrentAssembly(ctx))
            {
                var tbf = method.AsMethodDefinitionVariable();
                var found = ctx.DefinitionVariables.GetMethodVariable(tbf);
                if (!found.IsValid)
                    throw new ArgumentException($"Could not find variable declaration for method {method.Name}.");
                
                if (!method.ContainingType.IsGenericType)
                    return found.VariableName;
                
                var declaringTypeResolveExp = ctx.TypeResolver.Resolve(method.ContainingType);
                var exps = found.VariableName.CloneMethodReferenceOverriding(ctx, new() { ["DeclaringType"] =  declaringTypeResolveExp }, method, out var variable);
                foreach (var expression in exps)
                {
                    ctx.WriteCecilExpression(expression);
                    ctx.WriteNewLine();
                }
                
                return variable;
            }
            
            var declaringTypeName = method.ContainingType.ReflectionTypeName(out var typeParameters);
            if (method.IsGenericMethod)
            {
                var paramTypes = method.ConstructedFrom.Parameters.AsStringNewArrayExpression();
                var typeArguments = method.TypeArguments.AsStringNewArrayExpression();
                return Utils.ImportFromMainModule($"TypeHelpers.ResolveGenericMethod(\"{declaringTypeName}\", \"{method.Name}\",{method.Modifiers()}, {typeArguments}, {paramTypes})");
            }

            if (!method.ContainingType.IsValueType && !method.ContainingType.IsGenericType)
                declaringTypeName = (method.OverriddenMethod ?? method).ContainingType.AssemblyQualifiedName();

            return ImportFromMainModule($"TypeHelpers.ResolveMethod(\"{declaringTypeName}\", \"{method.Name}\",{method.Modifiers()},\"{string.Join(',', typeParameters)}\"{method.Parameters.Aggregate("", (acc, curr) => acc + ", \"" + curr.Type.AssemblyQualifiedName() + "\"")})");
        }

        public static MethodDefinitionVariable AsMethodDefinitionVariable(this IMethodSymbol method, string variableName = null)
        {
            return new MethodDefinitionVariable(
                method.OriginalDefinition.ContainingType.Name,
                method.OriginalDefinition.Name,
                method.OriginalDefinition.Parameters.Select(p => p.Type.ToDisplayString()).ToArray(),
                variableName);
        }

        public static string MethodModifiersToCecil(this SyntaxTokenList modifiers, string specificModifiers = null, IMethodSymbol methodSymbol = null)
        {
            var lastDeclaredIn = methodSymbol.FindLastDefinition();
            var modifiersStr = MapExplicitModifiers(modifiers, lastDeclaredIn.ContainingType.TypeKind);
            
            var defaultAccessibility = lastDeclaredIn.ContainingType.TypeKind == TypeKind.Interface ? "Public" : "Private";
            if (modifiersStr == string.Empty && methodSymbol != null)
            {
                if (IsExplicitMethodImplementation(methodSymbol))
                {
                    modifiersStr = Constants.Cecil.InterfaceMethodDefinitionAttributes.AppendModifier("MethodAttributes.Final");
                }
                else if (lastDeclaredIn.ContainingType.TypeKind == TypeKind.Interface)
                {
                    modifiersStr = Constants.Cecil.InterfaceMethodDefinitionAttributes.AppendModifier(
                                       SymbolEqualityComparer.Default.Equals(lastDeclaredIn.ContainingType, methodSymbol.ContainingType) 
                                           ? "MethodAttributes.Abstract" 
                                           : "MethodAttributes.Final");
                }
            }

            var validModifiers = RemoveSourceModifiersWithNoILEquivalent(modifiers);

            var cecilModifiersStr = new StringBuilder(SyntaxWalkerBase.ModifiersToCecil<MethodAttributes>(validModifiers.ToList(), defaultAccessibility, MapMethodAttributeFor));
            if (specificModifiers != null)
            {
                cecilModifiersStr.AppendModifier(specificModifiers);
            }

            cecilModifiersStr.AppendModifier("MethodAttributes.HideBySig").AppendModifier(modifiersStr);
            if (methodSymbol.HasCovariantReturnType())
            {
                cecilModifiersStr.AppendModifier("MethodAttributes.NewSlot");
            }
            return cecilModifiersStr.ToString();
        }        
        
        public static string ModifiersForSyntheticMethod(this SyntaxTokenList modifiers, string specificModifiers, ITypeSymbol declaringType)
        {
            var modifiersStr = MapExplicitModifiers(modifiers, declaringType.TypeKind);
            var defaultAccessibility = declaringType.TypeKind == TypeKind.Interface ? "Public" : "Private";

            var validModifiers = RemoveSourceModifiersWithNoILEquivalent(modifiers);

            var cecilModifiersStr = new StringBuilder(SyntaxWalkerBase.ModifiersToCecil<MethodAttributes>(validModifiers.ToList(), defaultAccessibility, MapMethodAttributeFor));
            cecilModifiersStr.AppendModifier(specificModifiers);
            cecilModifiersStr.AppendModifier("MethodAttributes.HideBySig").AppendModifier(modifiersStr);

            //TODO: This is not taking into account static abstract methods. We need to pass whether the method is static or not as a parameter
            //      and in case it is static, do not add NewSlot (which is part of InterfaceMethodDefinitionAttributes)
            if (declaringType.TypeKind == TypeKind.Interface)
                cecilModifiersStr.AppendModifier(Constants.Cecil.InterfaceMethodDefinitionAttributes).AppendModifier("MethodAttributes.Abstract");
            
            return cecilModifiersStr.ToString();
        }

        public static bool HasCovariantReturnType(this IMethodSymbol method) => method is { IsOverride: true } && !method.ReturnType.Equals(method.OverriddenMethod.ReturnType);

        private static bool IsExplicitMethodImplementation(IMethodSymbol methodSymbol)
        {
            return methodSymbol.ExplicitInterfaceImplementations.Any();
        }

        private static string MapExplicitModifiers(SyntaxTokenList modifiers, TypeKind typeKind)
        {
            foreach (var mod in modifiers)
            {
                switch (mod.Kind())
                {
                    case SyntaxKind.VirtualKeyword: return "MethodAttributes.Virtual | MethodAttributes.NewSlot";
                    case SyntaxKind.OverrideKeyword: return "MethodAttributes.Virtual";
                    case SyntaxKind.AbstractKeyword: return "MethodAttributes.Virtual | MethodAttributes.Abstract".AppendModifier(typeKind != TypeKind.Interface || !modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)) ? "MethodAttributes.NewSlot" : string.Empty);
                    case SyntaxKind.SealedKeyword: return "MethodAttributes.Final";
                    case SyntaxKind.NewKeyword: return "??? new ??? dont know yet!";
                }
            }

            return string.Empty;
        }

        private static IEnumerable<SyntaxToken> RemoveSourceModifiersWithNoILEquivalent(SyntaxTokenList modifiers)
        {
            return modifiers.Where(
                mod => !mod.IsKind(SyntaxKind.OverrideKeyword) 
                       && !mod.IsKind(SyntaxKind.AbstractKeyword)
                       && !mod.IsKind(SyntaxKind.VirtualKeyword)
                       && !mod.IsKind(SyntaxKind.SealedKeyword)
                       && !mod.IsKind(SyntaxKind.UnsafeKeyword));
        }

        private static IEnumerable<string> MapMethodAttributeFor(SyntaxToken token) =>
            token.Kind() switch
            {
                SyntaxKind.InternalKeyword => new[] { "Assembly" },
                SyntaxKind.ProtectedKeyword => new[] { "Family" },
                SyntaxKind.PrivateKeyword => new[] { "Private" },
                SyntaxKind.PublicKeyword => new[] { "Public" },
                SyntaxKind.StaticKeyword => new[] { "Static" },
                SyntaxKind.AbstractKeyword => new[] { "Abstract" },
               
                SyntaxKind.AsyncKeyword => Array.Empty<string>(),
                SyntaxKind.UnsafeKeyword => Array.Empty<string>(),
                SyntaxKind.PartialKeyword => Array.Empty<string>(),
                SyntaxKind.VolatileKeyword => Array.Empty<string>(),
                SyntaxKind.ExternKeyword => Array.Empty<string>(),
                SyntaxKind.ConstKeyword => Array.Empty<string>(),
                SyntaxKind.ReadOnlyKeyword => Array.Empty<string>(),
                _ => throw new ArgumentException($"Unsupported attribute name: {token.Kind().ToString()}")
            };
    }
}
