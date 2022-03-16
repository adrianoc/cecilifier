using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Cecilifier.Core.Misc.Utils;

namespace Cecilifier.Core.Extensions
{
    internal static class MethodExtensions
    {
        public static string Modifiers(this IMethodSymbol method)
        {
            var bindingFlags = method.IsStatic ? BindingFlags.Static : BindingFlags.Instance;
            bindingFlags |= method.DeclaredAccessibility == Accessibility.Public ? BindingFlags.Public : BindingFlags.NonPublic;

            var res = "";
            var enumType = typeof(BindingFlags);
            foreach (BindingFlags flag in Enum.GetValues(enumType))
            {
                if (bindingFlags.HasFlag(flag))
                {
                    res = res + "|" + enumType.FullName + "." + flag;
                }
            }

            return res.Length > 0 ? res.Substring(1) : string.Empty;
        }

        public static string MethodResolverExpression(this IMethodSymbol method, IVisitorContext ctx)
        {
            if (method.IsDefinedInCurrentType(ctx))
            {
                var tbf = (method.OverriddenMethod ?? method).AsMethodDefinitionVariable();
                var found = ctx.DefinitionVariables.GetMethodVariable(tbf);
                if (!found.IsValid)
                    throw new ArgumentException($"Could not find variable declaration for method {method.Name}.");
                
                if (!method.ContainingType.IsGenericType)
                    return found.VariableName;
                
                var declaringTypeResolveExp = ctx.TypeResolver.Resolve(method.ContainingType);
                var exps = found.VariableName.CloneMethodReferenceOverriding(ctx, new() { ["DeclaringType"] =  declaringTypeResolveExp }, method.Parameters.Length > 0, out var variable);
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

            return ImportFromMainModule(string.Format("TypeHelpers.ResolveMethod(\"{0}\", \"{1}\",{2},\"{3}\"{4})",
                declaringTypeName,
                method.Name,
                method.Modifiers(),
                string.Join(',', typeParameters),
                method.Parameters.Aggregate("", (acc, curr) => acc + ", \"" + curr.Type.AssemblyQualifiedName() + "\"")));
        }

        public static MethodDefinitionVariable AsMethodDefinitionVariable(this IMethodSymbol method, string variableName = null)
        {
            return new MethodDefinitionVariable(
                method.OriginalDefinition.ContainingType.Name,
                method.OriginalDefinition.Name,
                method.OriginalDefinition.Parameters.Select(p => p.Type.ToDisplayString()).ToArray(),
                variableName);
        }

        public static string MethodModifiersToCecil(this SyntaxTokenList modifiers, Func<string, IReadOnlyList<SyntaxToken>, string, string> modifiersToCecil, string specificModifiers = null, IMethodSymbol methodSymbol = null)
        {
            var modifiersStr = MapExplicitModifiers(modifiers);

            var defaultAccessibility = "Private";
            if (modifiersStr == string.Empty && methodSymbol != null)
            {
                if (IsExplicitMethodImplementation(methodSymbol))
                {
                    modifiersStr = "MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.Final";
                }
                else
                {
                    var lastDeclaredIn = methodSymbol.FindLastDefinition();
                    if (lastDeclaredIn.ContainingType.TypeKind == TypeKind.Interface)
                    {
                        modifiersStr = "MethodAttributes.Virtual | MethodAttributes.NewSlot | " +
                                       (ReferenceEquals(lastDeclaredIn.ContainingType, methodSymbol.ContainingType) ? "MethodAttributes.Abstract" : "MethodAttributes.Final");
                        defaultAccessibility = ReferenceEquals(lastDeclaredIn.ContainingType, methodSymbol.ContainingType) ? "Public" : "Private";
                    }
                }
            }

            var validModifiers = RemoveSourceModifiersWithNoILEquivalent(modifiers);

            var cecilModifiersStr = modifiersToCecil("MethodAttributes", validModifiers.ToList(), defaultAccessibility);

            if (specificModifiers != null)
            {
                cecilModifiersStr += $"| {specificModifiers}";
            }

            return cecilModifiersStr + " | MethodAttributes.HideBySig".AppendModifier(modifiersStr);
        }

        private static bool IsExplicitMethodImplementation(IMethodSymbol methodSymbol)
        {
            return methodSymbol.ExplicitInterfaceImplementations.Any();
        }

        private static string MapExplicitModifiers(SyntaxTokenList modifiers)
        {
            foreach (var mod in modifiers)
            {
                switch (mod.Kind())
                {
                    case SyntaxKind.VirtualKeyword: return "MethodAttributes.Virtual | MethodAttributes.NewSlot";
                    case SyntaxKind.OverrideKeyword: return "MethodAttributes.Virtual";
                    case SyntaxKind.AbstractKeyword: return "MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.Abstract";
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
    }
}
