using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Services;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MethodAttributes=Mono.Cecil.MethodAttributes;
using static Cecilifier.Core.Misc.Utils;

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
        public static string MethodResolverExpression(this IMethodSymbol method, IVisitorContext ctx)
        {
            if (method.IsDefinedInCurrentAssembly(ctx))
            {
                var tbf = method.AsMethodDefinitionVariable();
                var found = ctx.DefinitionVariables.GetMethodVariable(tbf);
                if (!found.IsValid)
                    throw new ArgumentException($"Could not find variable declaration for method {method.Name}.");

                if (!method.ContainingType.IsGenericType)
                    return found.VariableName.MakeGenericInstanceMethod(ctx, method);

                var declaringTypeResolveExp = ctx.TypeResolver.Resolve(method.ContainingType);
                var exps = found.VariableName.CloneMethodReferenceOverriding(ctx, new() { ["DeclaringType"] = declaringTypeResolveExp }, method, out var variable);
                ctx.WriteCecilExpressions(exps);
                return variable.MakeGenericInstanceMethod(ctx, method);
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
                var (referencedMethodTypeParameters, returnReferencesTypeParameter) = CollectReferencedMethodTypeParameters(method);
                var returnType = !returnReferencesTypeParameter ? ctx.TypeResolver.Resolve(method.ReturnType) : ctx.TypeResolver.Bcl.System.Void;
                var tempMethodVar = ctx.Naming.SyntheticVariable(method.Name, ElementKind.MemberReference);
                ctx.WriteCecilExpression($$"""
                                           var {{tempMethodVar}} = new MethodReference("{{method.Name}}", {{ returnType }}, {{ctx.TypeResolver.Resolve(method.ContainingType)}})
                                                       {
                                                           HasThis = {{(method.IsStatic ? "false" : "true")}},
                                                           ExplicitThis = false,
                                                           CallingConvention = {{ (int) method.CallingConvention}},
                                                       };
                                           """);
                ctx.WriteNewLine();

                List<ScopedDefinitionVariable> toDispose = new();
                foreach (var typeParameter in method.OriginalDefinition.TypeParameters)
                {
                    if (referencedMethodTypeParameters.Contains(typeParameter))
                    {
                        // method signature *does* reference this type parameter; store the `GenericParameter` instance in a variable
                        // to reference it later.
                        var genericVar = ctx.Naming.SyntheticVariable(typeParameter.Name, ElementKind.GenericInstance);
                        ctx.WriteCecilExpression($"""var {genericVar} = new GenericParameter("{typeParameter.Name}", {tempMethodVar});""");
                        ctx.WriteNewLine();
                        ctx.WriteCecilExpression($"""{tempMethodVar}.GenericParameters.Add({genericVar});""");
                        ctx.WriteNewLine();
                            
                        toDispose.Add(ctx.DefinitionVariables.WithCurrent(method.OriginalDefinition.FullyQualifiedName(false), typeParameter.Name, VariableMemberKind.TypeParameter, genericVar ));
                    }
                    else
                    {
                        // method signature does not reference this type parameter so no need to store the `GenericParameter` instance in a variable
                        // (we will not reference it later anyway)
                        ctx.WriteCecilExpression($"""{tempMethodVar}.GenericParameters.Add(new GenericParameter("{typeParameter.Name}", {tempMethodVar}));""");
                        ctx.WriteNewLine();
                    }
                }

                if (returnReferencesTypeParameter)
                {
                    var resolvedReturnType = ctx.TypeResolver.Resolve(method.OriginalDefinition.ReturnType, tempMethodVar);
                    
                    // the information about the type being passed as `ref` is not in the ITypeSymbol so we need to check and produce
                    // a Mono.Cecil.ByReferenceType
                    if (method.ReturnsByRef || method.ReturnsByRefReadonly)
                        resolvedReturnType = resolvedReturnType.MakeByReferenceType();
                        
                    ctx.WriteCecilExpression($"{tempMethodVar}.ReturnType = {resolvedReturnType};");
                    ctx.WriteNewLine();
                }
                    
                foreach (var parameter in method.Parameters)
                {
                    var tempParamVar = ctx.Naming.SyntheticVariable(parameter.Name, ElementKind.Parameter);
                    var exps = CecilDefinitionsFactory.Parameter(ctx, parameter.OriginalDefinition, tempMethodVar, tempParamVar);
                    ctx.WriteCecilExpressions(exps);
                }
                    
                toDispose.ForEach(v => v.Dispose());
                
                return method.IsDefinition 
                        ? tempMethodVar 
                        : tempMethodVar.MakeGenericInstanceMethod(ctx, method.Name, method.TypeArguments.Select(t => ctx.TypeResolver.Resolve(t)).ToList());
            }

            if (method.Parameters.Any(p => p.Type.IsTypeParameterOrIsGenericTypeReferencingTypeParameter()) 
                || method.ReturnType.IsTypeParameterOrIsGenericTypeReferencingTypeParameter()
                || method.ContainingType.TypeArguments.Any(t => t.IsDefinedInCurrentAssembly(ctx))
                || method.ContainingType.HasTypeArgumentOfTypeFromCecilifiedCodeTransitive(ctx))
            {
                return ResolveMethodFromGenericType(method, ctx);
            }

            return ImportFromMainModule($"TypeHelpers.ResolveMethod(typeof({declaringTypeName}), \"{method.Name}\",{method.ReflectionBindingsFlags()}{method.Parameters.Aggregate("", (acc, curr) => acc + ", \"" + curr.Type.FullyQualifiedName() + "\"")})");
        }

        private static (HashSet<ITypeParameterSymbol>, bool) CollectReferencedMethodTypeParameters(IMethodSymbol method)
        {
            HashSet<ITypeParameterSymbol> ret = new(method.TypeParameters.Length, SymbolEqualityComparer.Default);
            var referencedTypeParameter = method.OriginalDefinition.TypeParameters.FirstOrDefault(y => ReferencesTypeParameter(method.OriginalDefinition.ReturnType, y));
            var returnReferencesTypeParameter = false;
            if (referencedTypeParameter is not null)
            {
                // method return type has a reference to method's type parameter.
                ret.Add(referencedTypeParameter);
                returnReferencesTypeParameter = true;
            }

            // check if method parameters references any of the method's type parameters...
            foreach (var parameter in method.OriginalDefinition.Parameters)
            {
                referencedTypeParameter = method.OriginalDefinition.TypeParameters.FirstOrDefault(y => ReferencesTypeParameter(parameter.Type, y));
                if (referencedTypeParameter is not null)
                    ret.Add(referencedTypeParameter);
            }

            return (ret, returnReferencesTypeParameter);

            // checks whether `toCheck` references (directly or indirectly) the `typeParameter` 
            static bool ReferencesTypeParameter(ITypeSymbol toCheck, ITypeParameterSymbol typeParameter)
            {
                if (SymbolEqualityComparer.Default.Equals(toCheck, typeParameter))
                    return true;

                return toCheck switch
                {
                    INamedTypeSymbol { IsGenericType: true } toCheckGeneric => toCheckGeneric.TypeArguments.Any(t => ReferencesTypeParameter(t, typeParameter)),
                    IArrayTypeSymbol arrayType => ReferencesTypeParameter(arrayType.ElementType, typeParameter),
                    _ => false
                };
            }
        }

        private static string ResolveMethodFromGenericType(IMethodSymbol method, IVisitorContext ctx)
        {
            // resolve declaring type of the method.
            var targetTypeVarName = ctx.Naming.SyntheticVariable($"{method.ContainingType.Name}", ElementKind.LocalVariable);
            var resolvedTargetTypeExp = ctx.TypeResolver.Resolve(method.ContainingType.OriginalDefinition).MakeGenericInstanceType(method.ContainingType.GetAllTypeArguments().Select(t => ctx.TypeResolver.Resolve(t)));
            ctx.WriteCecilExpression($"var {targetTypeVarName} = {resolvedTargetTypeExp};");
            ctx.WriteNewLine();

            // find the original method.
            var originalMethodVar = ctx.Naming.SyntheticVariable($"open{method.Name}", ElementKind.LocalVariable);
            // TODO: handle overloads
            ctx.WriteCecilExpression($"""var {originalMethodVar} = {ctx.TypeResolver.Resolve(method.ContainingType.OriginalDefinition)}.Resolve().Methods.First(m => m.Name == "{method.Name}" && m.Parameters.Count == {method.Parameters.Length} );""");
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

            //TODO: This is not taking into account static abstract methods. We need to pass whether the method is static or not as a parameter
            //      and in case it is static, do not add NewSlot (which is part of InterfaceMethodDefinitionAttributes)
            if (declaringType.TypeKind == TypeKind.Interface)
                cecilModifiersStr.AppendModifier(Constants.Cecil.InterfaceMethodDefinitionAttributes).AppendModifier("MethodAttributes.Abstract");

            return cecilModifiersStr.ToString();
        }

        public static bool HasCovariantReturnType(this IMethodSymbol method) => method is { IsOverride: true } && !SymbolEqualityComparer.Default.Equals(method.ReturnType, method.OverriddenMethod?.ReturnType);

        public static IEnumerable<string> MakeGenericInstanceMethod(this string methodReferenceVariable, IVisitorContext context, string methodName, IReadOnlyList<string> resolvedTypeArguments, out string varName)
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
        
        public static string MakeGenericInstanceMethod(this string methodReferenceVariable, IVisitorContext context, string methodName, IReadOnlyList<string> resolvedTypeArguments)
        {
            var exps = methodReferenceVariable.MakeGenericInstanceMethod(context, methodName, resolvedTypeArguments, out var genericInstanceVarName);
            context.WriteCecilExpressions(exps);

            return genericInstanceVarName;
        }
        
        public static string MakeGenericInstanceMethod(this string methodReferenceVariable, IVisitorContext context, IMethodSymbol method)
        {
            if (method.IsGenericMethod is false)
                return methodReferenceVariable;
            
            var exps = methodReferenceVariable.MakeGenericInstanceMethod(context, method.Name, method.TypeArguments.Select(t => context.TypeResolver.Resolve(t)).ToList(), out var genericInstanceVarName);
            context.WriteCecilExpressions(exps);

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
