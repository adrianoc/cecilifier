using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using Cecilifier.Core.AST;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Services;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MethodAttributes=Mono.Cecil.MethodAttributes;

namespace Cecilifier.Core.Extensions
{
    public static class MethodExtensions
    {
        public static TypeParameterSyntax[] GetTypeParameterSyntax(this IMethodSymbol method)
        {
            if (!method.IsGenericMethod)
                return Array.Empty<TypeParameterSyntax>();
            
            var candidateDeclarationNode = method.DeclaringSyntaxReferences.SingleOrDefault();
            if (candidateDeclarationNode == null)
                return Array.Empty<TypeParameterSyntax>();

            return candidateDeclarationNode.GetSyntax() switch
            {
                MethodDeclarationSyntax methodDeclaration => methodDeclaration.TypeParameterList?.Parameters.ToArray(),
                LocalFunctionStatementSyntax localFunction => localFunction.TypeParameterList?.Parameters.ToArray(),
                TypeDeclarationSyntax typeDeclaration => typeDeclaration.TypeParameterList?.Parameters.ToArray(),
                
                _ => throw new InvalidOperationException($"Unexpected node type {candidateDeclarationNode.GetType().Name}.")
            };
        }
        
        /// <summary>
        /// Generates a string containing the code required to produce a `Mono.Cecil.MethodReference` for the specified <see cref="method"/>
        /// </summary>
        /// <param name="method"></param>
        /// <param name="ctx"></param>
        /// <returns>A string containing the code required to produce a `Mono.Cecil.MethodReference` for the specified <see cref="method"/></returns>
        /// <exception cref="ArgumentException"></exception>
        /// <remarks>
        /// If <see cref="method"/> represents a `method definition` the caller is responsible for taking the returned resolved `open generic method`
        /// and turning it into a `generic instance method` by providing the corresponding `type arguments`
        /// For `method references` the `type arguments` are encoded in the reference itself
        /// </remarks>
        public static string MethodResolverExpression(this IMethodSymbol method, IVisitorContext ctx) => ctx.MemberResolver.ResolveMethod(method);

        public static MethodDefinitionVariable AsMethodDefinitionVariable(this IMethodSymbol method, string variableName = null) => AsMethodVariable(method, VariableMemberKind.Method, variableName);
        
        public static MethodDefinitionVariable AsMethodVariable(this IMethodSymbol method, VariableMemberKind methodKind, string variableName = null)
        {
            return new MethodDefinitionVariable(
                methodKind,
                method.OriginalDefinition.ContainingType.ToDisplayString(),
                method.OriginalDefinition.Name,
                method.OriginalDefinition.Parameters.Select(p => p.Type.ToDisplayString()).ToArray(),
                method.TypeParameters.Length,
                variableName);
        }

        public static string MethodModifiersToCecil(this IEnumerable<SyntaxToken> modifiers, string specificModifiers = null, IMethodSymbol methodSymbol = null)
        {
            var lastDeclaredIn = methodSymbol.FindLastDefinition();
            var modifiersStr = MapExplicitModifiers(modifiers, lastDeclaredIn.ContainingType.TypeKind);

            var defaultAccessibility = lastDeclaredIn.ContainingType.TypeKind == TypeKind.Interface ? "Public" : "Private";
            if (modifiersStr == string.Empty && methodSymbol != null)
            {
                if (methodSymbol.IsExplicitMethodImplementation())
                {
                    modifiersStr = Constants.Cecil.InterfaceMethodDefinitionAttributes.AppendModifier("MethodAttributes.Final");
                }
                else if (lastDeclaredIn.ContainingType.TypeKind == TypeKind.Interface && !methodSymbol.IsStatic)
                {
                    modifiersStr = Constants.Cecil.InterfaceMethodDefinitionAttributes.AppendModifier(
                                       SymbolEqualityComparer.Default.Equals(lastDeclaredIn.ContainingType, methodSymbol.ContainingType)
                                           ? "MethodAttributes.Abstract"
                                           : "MethodAttributes.Final");
                }
                else if (lastDeclaredIn.ContainingType.TypeKind == TypeKind.Interface && methodSymbol.IsStatic)
                {
                    modifiersStr = string.Empty;
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

            if (declaringType.TypeKind == TypeKind.Interface)
                cecilModifiersStr.AppendModifier(Constants.Cecil.InterfaceMethodDefinitionAttributes).AppendModifier("MethodAttributes.Abstract");

            return cecilModifiersStr.ToString();
        }

        public static bool HasCovariantReturnType(this IMethodSymbol method) => method is { IsOverride: true } && !SymbolEqualityComparer.Default.Equals(method.ReturnType, method.OverriddenMethod?.ReturnType);

        public static IEnumerable<string> MakeGenericInstanceMethod(this string methodReferenceVariable, IVisitorContext context, string methodName, IReadOnlyList<ResolvedType> resolvedTypeArguments, out string varName)
        {
            var hash = new HashCode();
            hash.Add(methodReferenceVariable);
            hash.Add(resolvedTypeArguments.Count);
            foreach (var t in resolvedTypeArguments)
                hash.Add(t);

            List<string> exps = new();
            varName = context.Services.Get<GenericInstanceMethodCacheService<int, string>>().GetOrCreate(hash.ToHashCode(), (context, methodName, resolvedTypeArguments, methodReferenceVariable, exps),
                static (hashCode, state) =>
                {
                    var genericInstanceVarName = state.context.Naming.SyntheticVariable(state.methodName, ElementKind.GenericInstance);

                    state.exps.Add($"var {genericInstanceVarName} = new GenericInstanceMethod({state.methodReferenceVariable});");
                    foreach (var t in state.resolvedTypeArguments)
                    {
                        state.exps.Add($"{genericInstanceVarName}.GenericArguments.Add({t});");
                    }
                    return genericInstanceVarName;
                });

            return exps;
        }
        
        public static string MakeGenericInstanceMethod(this string methodReferenceVariable, IVisitorContext context, string methodName, IReadOnlyList<ResolvedType> resolvedTypeArguments)
        {
            var exps = methodReferenceVariable.MakeGenericInstanceMethod(context, methodName, resolvedTypeArguments, out var genericInstanceVarName);
            context.Generate(exps);

            return genericInstanceVarName;
        }
        
        public static string MakeGenericInstanceMethod(this string methodReferenceVariable, IVisitorContext context, IMethodSymbol method)
        {
            if (method.IsGenericMethod is false)
                return methodReferenceVariable;
            
            var exps = methodReferenceVariable.MakeGenericInstanceMethod(context, method.Name, method.TypeArguments.Select(t => context.TypeResolver.ResolveAny(t)).ToList(), out var genericInstanceVarName);
            context.Generate(exps);

            return genericInstanceVarName;
        }

        private static bool IsExplicitMethodImplementation(this IMethodSymbol methodSymbol)
        {
            return methodSymbol.ExplicitInterfaceImplementations.Any();
        }

        private static string MapExplicitModifiers(IEnumerable<SyntaxToken> modifiers, TypeKind typeKind)
        {
            foreach (var mod in modifiers)
            {
                switch (mod.Kind())
                {
                    case SyntaxKind.VirtualKeyword:
                        return "MethodAttributes.Virtual | MethodAttributes.NewSlot";
                    case SyntaxKind.OverrideKeyword:
                        return "MethodAttributes.Virtual";
                    case SyntaxKind.AbstractKeyword:
                        return "MethodAttributes.Virtual | MethodAttributes.Abstract".AppendModifier(typeKind != TypeKind.Interface || !modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)) ? "MethodAttributes.NewSlot" : string.Empty);
                    case SyntaxKind.SealedKeyword:
                        return "MethodAttributes.Final";
                    case SyntaxKind.NewKeyword:
                        return "??? new ??? dont know yet!";
                }
            }

            return string.Empty;
        }

        private static IEnumerable<SyntaxToken> RemoveSourceModifiersWithNoILEquivalent(IEnumerable<SyntaxToken> modifiers)
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
        
        public static IMethodSymbol FindLastDefinition(this IMethodSymbol self)
        {
            if (self == null)
            {
                return null;
            }

            return FindLastDefinition(self, self.ContainingType) ?? self;
        }
        
        public static IMethodSymbol FindLastDefinition(this IMethodSymbol method, ImmutableArray<INamedTypeSymbol> implementedItfs)
        {
            foreach (var itf in implementedItfs)
            {
                var found = FindLastDefinition(method, itf);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static IMethodSymbol FindLastDefinition(IMethodSymbol method, INamedTypeSymbol toBeChecked)
        {
            if (toBeChecked == null)
            {
                return null;
            }

            var found = toBeChecked.GetMembers().OfType<IMethodSymbol>().SingleOrDefault(candidate => CompareMethods(candidate, method));
            if (SymbolEqualityComparer.Default.Equals(found, method) || found == null)
            {
                found = FindLastDefinition(method, toBeChecked.Interfaces);
                found ??= FindLastDefinition(method, toBeChecked.BaseType);
            }

            return found;
        }
        
        private static bool CompareMethods(IMethodSymbol lhs, IMethodSymbol rhs)
        {
            if (lhs.Name != rhs.Name)
                return false;

            if (!SymbolEqualityComparer.Default.Equals(lhs.ReturnType, rhs.ReturnType))
                return false;

            if (lhs.Parameters.Count() != rhs.Parameters.Count())
                return false;

            for (var i = 0; i < lhs.Parameters.Count(); i++)
            {
                if (!SymbolEqualityComparer.Default.Equals(lhs.Parameters[i].Type, rhs.Parameters[i].Type))
                    return false;
            }

            if (lhs.TypeParameters.Count() != rhs.TypeParameters.Count())
                return false;
            
            for (var i = 0; i < lhs.TypeParameters.Count(); i++)
            {
                if (!SymbolEqualityComparer.Default.Equals(lhs.TypeParameters[i], rhs.TypeParameters[i]))
                    return false;
            }

            return true;
        }
    }
}
