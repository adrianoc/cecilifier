using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Cecilifier.Core.AST;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil;
using static Cecilifier.Core.Misc.Utils;
using MethodAttributes = Mono.Cecil.MethodAttributes;

namespace Cecilifier.Core.Extensions
{
    internal static class MethodExtensions
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
        
        private static string ReflectionBindingsFlags(this IMethodSymbol method)
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
                var exps = found.VariableName.CloneMethodReferenceOverriding(ctx, new() { ["DeclaringType"] = declaringTypeResolveExp }, method, out var variable);
                foreach (var expression in exps)
                {
                    ctx.WriteCecilExpression(expression);
                    ctx.WriteNewLine();
                }

                return variable;
            }

            var declaringTypeName = method.ContainingType.FullyQualifiedName();
            
            // invocation on non value type virtual methods must be dispatched to the original method (i.e, the virtual method definition, not the overridden one)
            // note that in Roslyn, IMethodSymbol.OriginalMethod is the method implementation and IMethodSymbol.OverridenMethod is the actual original virtual method.  
            if (!method.ContainingType.IsValueType)
            {
                method = method.OverriddenMethod ?? method;
                declaringTypeName = method.ContainingType.FullyQualifiedName();
            }

            if (method.IsGenericMethod)
            {
                var parameters = $$"""new ParamData[] { {{ string.Join(',', method.Parameters.Select(p => $$"""new ParamData { FullName="{{p.Type.ElementTypeSymbolOf().FullyQualifiedName()}}", IsArray={{(p.Type.TypeKind == TypeKind.Array ? "true" : "false")}}, IsTypeParameter={{(p.Type.TypeKind == TypeKind.TypeParameter ? "true" : "false") }} } """)) }} }""";
                var genericTypeParameters = $$"""new [] { {{ string.Join(',', method.TypeArguments.Select(TypeNameFrom)) }} }""";
                
                return ImportFromMainModule(
                    $"""
                      TypeHelpers.ResolveGenericMethodInstance(typeof({declaringTypeName}).AssemblyQualifiedName, "{method.Name}", {method.ReflectionBindingsFlags()}, {parameters}, {genericTypeParameters}) 
                      """);
            }
            
            if (method.Parameters.Any(p => p.Type.IsTypeParameterOrIsGenericTypeReferencingTypeParameter()) || method.ReturnType.IsTypeParameterOrIsGenericTypeReferencingTypeParameter())
            {
                return ResolveMethodFromGenericType(method, ctx);
            }

            return ImportFromMainModule($"TypeHelpers.ResolveMethod(typeof({declaringTypeName}), \"{method.Name}\",{method.ReflectionBindingsFlags()}{method.Parameters.Aggregate("", (acc, curr) => acc + ", \"" + curr.Type.FullyQualifiedName() + "\"")})");
            
            static string TypeNameFrom(ITypeSymbol typeSymbol) => typeSymbol.TypeKind == TypeKind.TypeParameter 
                ? $"\"{typeSymbol.Name}\"" 
                : $"typeof({typeSymbol.ElementTypeSymbolOf().FullyQualifiedName()}).AssemblyQualifiedName";
        }

        private static string ResolveMethodFromGenericType(IMethodSymbol method, IVisitorContext ctx)
        {
            // resolve declaring type of the method.
            var targetTypeVarName = ctx.Naming.SyntheticVariable($"{method.ContainingType.Name}", ElementKind.LocalVariable);
            var resolvedTargetTypeExp = ctx.TypeResolver.Resolve(method.ContainingType.OriginalDefinition).MakeGenericInstanceType(method.ContainingType.TypeArguments.Select(t => ctx.TypeResolver.Resolve(t)));
            ctx.WriteCecilExpression($"var {targetTypeVarName} = {resolvedTargetTypeExp};");
            ctx.WriteNewLine();

            // find the original method.
            var originalMethodVar = ctx.Naming.SyntheticVariable($"open{method.Name}", ElementKind.LocalVariable);
            // TODO: handle overloads
            ctx.WriteCecilExpression($"""var {originalMethodVar} = {ctx.TypeResolver.Resolve(method.ContainingType.OriginalDefinition)}.Resolve().Methods.First(m => m.Name == "{method.Name}");""");
            ctx.WriteNewLine();

            // Instantiates a MethodReference representing the called method.
            var targetMethodVar = ctx.Naming.SyntheticVariable($"{method.Name}", ElementKind.MemberReference);
            ctx.WriteCecilExpression(
                $$"""
                  var {{targetMethodVar}} = new MethodReference("{{method.Name}}", assembly.MainModule.ImportReference({{originalMethodVar}}).ReturnType)
                              {
                                   DeclaringType = {{targetTypeVarName}},
                                   HasThis = {{originalMethodVar}}.HasThis,
                                   ExplicitThis = {{originalMethodVar}}.ExplicitThis,
                                   CallingConvention = {{originalMethodVar}}.CallingConvention,
                              };
                  """);
            ctx.WriteNewLine();

            // Add original parameters to the MethodReference
            foreach (var parameter in method.Parameters)
            {
                //TODO: pass the correct ParameterAttributes.None
                ctx.WriteCecilExpression($"""{targetMethodVar}.Parameters.Add(new ParameterDefinition("{parameter.Name}", ParameterAttributes.None, {originalMethodVar}.Parameters[{parameter.Ordinal}].ParameterType));""");
                ctx.WriteNewLine();
            }
            
            return targetMethodVar;
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

            //TODO: This is not taking into account static abstract methods. We need to pass whether the method is static or not as a parameter
            //      and in case it is static, do not add NewSlot (which is part of InterfaceMethodDefinitionAttributes)
            if (declaringType.TypeKind == TypeKind.Interface)
                cecilModifiersStr.AppendModifier(Constants.Cecil.InterfaceMethodDefinitionAttributes).AppendModifier("MethodAttributes.Abstract");

            return cecilModifiersStr.ToString();
        }

        public static bool HasCovariantReturnType(this IMethodSymbol method) => method is { IsOverride: true } && !SymbolEqualityComparer.Default.Equals(method.ReturnType, method.OverriddenMethod?.ReturnType);

        public static bool IsExplicitMethodImplementation(this IMethodSymbol methodSymbol)
        {
            return methodSymbol.ExplicitInterfaceImplementations.Any();
        }

        private static string MapExplicitModifiers(SyntaxTokenList modifiers, TypeKind typeKind)
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
