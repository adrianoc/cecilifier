using System;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.Misc;

public struct StaticDelegateCacheContext
{
    public string CacheBackingField;
    public IMethodSymbol Method;
    internal IVisitorContext context;
    public bool IsStaticDelegate { get; init; }

    private string DeclaringTypeName => Method.ContainingType.Name;
    private string DeclaringTypeNamespace => Method.ContainingType.ContainingNamespace?.FullyQualifiedName() ?? Method.ContainingNamespace?.FullyQualifiedName() ?? string.Empty;

    /// <summary>
    /// Ensures that there's a static field declared inside an inner type of the current type being processed
    /// used to cache 'static method -> delegate' conversion.
    /// </summary>
    /// <remarks>
    /// Note that this caching mechanism is a Roslyn implementation detail (to be more precise, a Roslyn code
    /// generation detail) and may change; This class helps generating code that resembles code generated by
    /// Roslyn as close as possible but not 100% (for instance, we only handle conversions of methods from
    /// the same type as the one being processed; having a class with methods with references to 2 methods
    /// with the same name (but from different types) will produce the wrong code. 
    /// </remarks>
    /// <param name="delegateType"></param>
    /// <returns>name of the declared variable that holds the static field definition.</returns>

    public string EnsureCacheBackingFieldIsEmitted(string delegateType)
    {
        if (Method == null)
            throw new InvalidOperationException($"Unless 'IsStaticDelegate' is set to false {GetType().Name} needs to be fully initialized.");

        if (CacheBackingField != null)
            return CacheBackingField;

        var cacheTypeName = "<>O";
        var cacheInnerTypeName = $"{DeclaringTypeName}.{cacheTypeName}";
        var cacheTypeVar = context.DefinitionVariables.GetVariable(cacheTypeName, VariableMemberKind.Type, DeclaringTypeName);
        if (!cacheTypeVar.IsValid)
        {
            cacheTypeVar = EmitCacheType(cacheTypeName);
        }

        const string counterName = "StaticMethodToDelegateConversionBackingFieldCount";
        var staticMethodToDelegateConversionCount = cacheTypeVar.Properties.TryGetValue(counterName, out var boxedCounter) ? (int) boxedCounter : 0;
        var existingVarIndex = -1;
        string backingFieldName;

        while (++existingVarIndex < staticMethodToDelegateConversionCount)
        {
            backingFieldName = $"<{existingVarIndex}>__{Method.Name}";
            var cacheBackingFieldForStaticMethodVariable = context.DefinitionVariables.GetVariable(backingFieldName, VariableMemberKind.Field, cacheInnerTypeName);
            if (cacheBackingFieldForStaticMethodVariable.IsValid)
            {
                CacheBackingField = cacheBackingFieldForStaticMethodVariable.VariableName;
                return CacheBackingField;
            }
        }

        backingFieldName = $"<{staticMethodToDelegateConversionCount}>__{Method.Name}";
        cacheTypeVar.Properties[counterName] = ++staticMethodToDelegateConversionCount;

        CacheBackingField = context.Naming.SyntheticVariable("cachedDelegate", ElementKind.Field);
        var fieldExps = CecilDefinitionsFactory.Field(context, cacheInnerTypeName, cacheTypeVar, CacheBackingField, backingFieldName, delegateType, Constants.Cecil.StaticFieldAttributes);
        context.WriteCecilExpressions(fieldExps);

        return CacheBackingField;
    }

    private DefinitionVariable EmitCacheType(string cacheTypeName)
    {
        var cachedTypeVar = context.Naming.Type("", ElementKind.Class);
        var outerTypeVariable = context.DefinitionVariables.GetVariable(Method.ContainingType.ToDisplayString(), VariableMemberKind.Type, Method.ContainingType.ContainingSymbol.ToDisplayString());

        var cacheTypeExps = CecilDefinitionsFactory.Type(
            context,
            cachedTypeVar,
            DeclaringTypeNamespace,
            cacheTypeName,
            Constants.Cecil.StaticClassAttributes.AppendModifier("TypeAttributes.NestedPrivate"),
            context.TypeResolver.Bcl.System.Object,
            outerTypeVariable,
            isStructWithNoFields: false,
            Array.Empty<ITypeSymbol>(),
            [], 
            []);

        context.WriteCecilExpressions(cacheTypeExps);

        return context.DefinitionVariables.RegisterNonMethod(DeclaringTypeName, cacheTypeName, VariableMemberKind.Type, cachedTypeVar);
    }
}
