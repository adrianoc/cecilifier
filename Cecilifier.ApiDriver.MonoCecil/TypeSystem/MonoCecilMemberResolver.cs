using System.Reflection;
using System.Text;
using Cecilifier.ApiDriver.MonoCecil.Extensions;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.MonoCecil.TypeSystem;

public class MonoCecilMemberResolver(MonoCecilContext context) : IMemberResolver
{
    public string ResolveMethod(IMethodSymbol method)
    {
        if (method.IsDefinedInCurrentAssembly(context))
        {
            context.EnsureForwardedMethod(method);
            
            var tbf = method.AsMethodDefinitionVariable();
            var found = context.DefinitionVariables.GetMethodVariable(tbf);
            if (!found.IsValid)
                throw new ArgumentException($"Could not find variable declaration for method {method.Name}.");

            if (!method.ContainingType.IsGenericType)
                return found.VariableName.MakeGenericInstanceMethod(context, method);

            var declaringType = context.TypeResolver.Resolve(method.ContainingType, ResolveTargetKind.TypeReference);
            var exps = found.VariableName.CloneMethodReferenceOverriding(context, new() { ["DeclaringType"] = declaringType.Expression }, method, out var variable);
            context.Generate(exps);
            return variable.MakeGenericInstanceMethod(context, method);
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
            var returnType = !returnReferencesTypeParameter ? context.TypeResolver.Resolve(method.ReturnType, ResolveTargetKind.TypeReference) : context.TypeResolver.Bcl.System.Void;
            var tempMethodVar = context.Naming.SyntheticVariable(method.Name, ElementKind.MemberReference);
            context.Generate($$"""
                                       var {{tempMethodVar}} = new MethodReference("{{method.Name}}", {{returnType}}, {{context.TypeResolver.Resolve(method.ContainingType, ResolveTargetKind.TypeReference)}})
                                                   {
                                                       HasThis = {{(!method.IsStatic).ToKeyword()}},
                                                       ExplicitThis = false,
                                                       CallingConvention = {{(int) method.CallingConvention}},
                                                   };
                                       """);
            context.WriteNewLine();

            List<ScopedDefinitionVariable> toDispose = new();
            foreach (var typeParameter in method.OriginalDefinition.TypeParameters)
            {
                if (referencedMethodTypeParameters.Contains(typeParameter))
                {
                    // method signature *does* reference this type parameter; store the `GenericParameter` instance in a variable
                    // to reference it later.
                    var genericVar = context.Naming.SyntheticVariable(typeParameter.Name, ElementKind.GenericInstance);
                    context.Generate($"""var {genericVar} = new GenericParameter("{typeParameter.Name}", {tempMethodVar});""");
                    context.WriteNewLine();
                    context.Generate($"""{tempMethodVar}.GenericParameters.Add({genericVar});""");
                    context.WriteNewLine();

                    toDispose.Add(context.DefinitionVariables.WithCurrent(method.OriginalDefinition.ToDisplayString(), typeParameter.Name, VariableMemberKind.TypeParameter, genericVar));
                }
                else
                {
                    // method signature does not reference this type parameter so no need to store the `GenericParameter` instance in a variable
                    // (we will not reference it later anyway)
                    context.Generate($"""{tempMethodVar}.GenericParameters.Add(new GenericParameter("{typeParameter.Name}", {tempMethodVar}));""");
                    context.WriteNewLine();
                }
            }

            if (returnReferencesTypeParameter)
            {
                var resolvedReturnType = context.TypeResolver.Resolve(method.OriginalDefinition.ReturnType, method.OriginalDefinition.ToTypeResolutionContext(tempMethodVar));

                // the information about the type being passed as `ref` is not in the ITypeSymbol so we need to check and produce
                // a Mono.Cecil.ByReferenceType
                if (method.ReturnsByRef || method.ReturnsByRefReadonly)
                    resolvedReturnType = context.TypeResolver.MakeByRefType(resolvedReturnType);

                context.Generate($"{tempMethodVar}.ReturnType = {resolvedReturnType};");
                context.WriteNewLine();
            }

            foreach (var parameter in method.Parameters)
            {
                var tempParamVar = context.Naming.SyntheticVariable(parameter.Name, ElementKind.Parameter);
                var exps = CecilDefinitionsFactory.Parameter(context, parameter.OriginalDefinition, tempMethodVar, tempParamVar);
                context.Generate(exps);
            }

            toDispose.ForEach(v => v.Dispose());

            return method.IsDefinition
                ? tempMethodVar
                : tempMethodVar.MakeGenericInstanceMethod(context, method.Name, method.TypeArguments.Select(t => context.TypeResolver.Resolve(t, ResolveTargetKind.TypeReference)).ToList());
        }

        if (method.Parameters.Any(p => p.Type.IsTypeParameterOrIsGenericTypeReferencingTypeParameter())
            || method.ReturnType.IsTypeParameterOrIsGenericTypeReferencingTypeParameter()
            || method.ContainingType.HasTypeArgumentOfTypeFromCecilifiedCodeTransitive(context))
        {
            return ResolveMethodFromGenericType(method, context);
        }

        return ((MonoCecilTypeResolver) context.TypeResolver).ImportReference(
            $"TypeHelpers.ResolveMethod(typeof({declaringTypeName}), \"{method.Name}\",{ReflectionBindingsFlags(method)}{method.Parameters.Aggregate("", (acc, curr) => acc + ", \"" + curr.Type.GetReflectionName() + "\"")})").Expression;
    }

    public string ResolveMethod(string declaringTypeName, string declaringTypeVariable, string methodNameForVariableRegistration, ResolvedType returnType, IReadOnlyList<ParameterSpec> parameters, int typeParameterCountCount, MemberOptions options)
    {
        throw new NotImplementedException();
    }

    public string ResolveDefaultConstructor(ITypeSymbol baseType, string derivedTypeVar)
    {
        var baseTypeVarDef = context.TypeResolver.ResolveLocalVariableType(baseType, ResolveTargetKind.TypeReference);
        if (baseTypeVarDef)
        {
            return $"new MethodReference(\".ctor\", {context.TypeResolver.Bcl.System.Void} ,{baseTypeVarDef}) {{ HasThis = true }}";
        }

        return ImportReference($"TypeHelpers.DefaultCtorFor({derivedTypeVar}.BaseType)");
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

    private static string ResolveMethodFromGenericType(IMethodSymbol method, IVisitorContext context)
    {
        // resolve declaring type of the method.
        var targetTypeVarName = context.Naming.SyntheticVariable($"{method.ContainingType.Name}", ElementKind.LocalVariable);
        var resolvedTargetTypeExp = context.TypeResolver.MakeGenericInstanceType(
                                                context.TypeResolver.Resolve(method.ContainingType.OriginalDefinition, ResolveTargetKind.TypeReference),
                                                method.ContainingType, 
                                                ResolveTargetKind.None);
        
        context.Generate($"var {targetTypeVarName} = {resolvedTargetTypeExp};");
        context.WriteNewLine();

        // find the original method.
        var originalMethodVar = context.Naming.SyntheticVariable($"open{method.Name}", ElementKind.LocalVariable);

        var methodParameterNames = method.Parameters.Aggregate(new StringBuilder(), (acc, curr) => acc.Append($"""
                                                                                                               "{curr.Type.FullyQualifiedName()}",
                                                                                                               """));
        var parameterTypesCheck = method.Parameters.Length > 0
            ? $" && !m.Parameters.Select(p => p.ParameterType.FullName).Except([{methodParameterNames}]).Any()"
            : string.Empty;

        context.Generate(
            $"""var {originalMethodVar} = {context.TypeResolver.Resolve(method.ContainingType.OriginalDefinition, ResolveTargetKind.TypeReference)}.Resolve().Methods.First(m => m.Name == "{method.Name}" && m.Parameters.Count == {method.Parameters.Length}{parameterTypesCheck});""");
        context.WriteNewLine();

        // Instantiates a MethodReference representing the called method.
        var targetMethodVar = context.Naming.SyntheticVariable($"{method.Name}", ElementKind.MemberReference);
        context.Generate(
            $$"""
              var {{targetMethodVar}} = new MethodReference("{{method.Name}}", assembly.MainModule.ImportReference({{originalMethodVar}}).ReturnType)
                          {
                               DeclaringType = {{targetTypeVarName}},
                               HasThis = {{originalMethodVar}}.HasThis,
                               ExplicitThis = {{originalMethodVar}}.ExplicitThis,
                               CallingConvention = {{originalMethodVar}}.CallingConvention,
                          };
              """);
        context.WriteNewLine();

        // Add original parameters to the MethodReference
        foreach (var parameter in method.Parameters)
        {
            context.Generate(
                $"""{targetMethodVar}.Parameters.Add(new ParameterDefinition("{parameter.Name}", {originalMethodVar}.Parameters[{parameter.Ordinal}].Attributes, {originalMethodVar}.Parameters[{parameter.Ordinal}].ParameterType));""");
            context.WriteNewLine();
        }

        return targetMethodVar;
    }

    private static string ReflectionBindingsFlags(IMethodSymbol method)
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

    public string ResolveField(IFieldSymbol field)
    {
        if(field.IsDefinedInCurrentAssembly(context))
        {
            var found = context.DefinitionVariables.GetVariable(field.Name, VariableMemberKind.Field, field.ContainingType.OriginalDefinition.ToDisplayString());
            found.ThrowIfVariableIsNotValid();

            var resolvedField = field.ContainingType.IsGenericType
                ? $$"""new FieldReference({{found.VariableName}}.Name, {{found.VariableName}}.FieldType, {{context.TypeResolver.Resolve(field.ContainingType, ResolveTargetKind.TypeReference)}})""" 
                : found.VariableName;
                
            return resolvedField;
        }

        var declaringTypeName = field.ContainingType.FullyQualifiedName();
        return ImportReference($"TypeHelpers.ResolveField(\"{declaringTypeName}\",\"{field.Name}\")");
    }

    public string ResolveEventField(IEventSymbol eventSymbol)
    {
        if (!eventSymbol.IsDefinedInCurrentAssembly(context))
            throw new InvalidOperationException($"Event field {eventSymbol.Name} must be defined in the current assembly.");
        
        var found = context.DefinitionVariables.GetVariable(eventSymbol.Name, VariableMemberKind.Field, eventSymbol.ContainingType.OriginalDefinition.ToDisplayString());
        found.ThrowIfVariableIsNotValid();

        var resolvedField = eventSymbol.ContainingType.IsGenericType
            ? $$"""new FieldReference({{found.VariableName}}.Name, {{found.VariableName}}.FieldType, {{context.TypeResolver.Resolve(eventSymbol.ContainingType, ResolveTargetKind.TypeReference)}})""" 
            : found.VariableName;
                
        return resolvedField;
    }

    public string ImportReference(string expression) => $"assembly.MainModule.ImportReference({expression})";
}
