#nullable enable

using System;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.TypeSystem;

public interface ITypeResolver
{
    ResolvedType Resolve(ITypeSymbol type, in TypeResolutionContext resolutionContext);
    ResolvedType ResolvePredefinedType(ITypeSymbol type, in TypeResolutionContext resolutionContext);
    ResolvedType ResolveLocalVariableType(ITypeSymbol type, in TypeResolutionContext context);

    /// <summary>
    /// Resolves a generic type parameter from a <see cref="ResolvedType"/>
    /// </summary>
    /// <param name="genericTypeParameter"></param>
    /// <param name="typeParameterKind"></param>
    /// <param name="resolutionContext"></param>
    /// <returns>
    /// Api Drivers which represents 'Generic Type Parameters' as instances of types this method must simply return <param name="genericTypeParameter"></param>
    /// Others must return a valid expression (variable name, method call, etc) representing the generic type parameter. 
    /// </returns>
    ResolvedType ResolveTypeParameter(ResolvedType genericTypeParameter, TypeParameterKind typeParameterKind, in TypeResolutionContext resolutionContext);
    
    /// <summary>
    /// Some Api drivers may use different syntaxes depending on the usage (i.e. when being used to declare
    /// a local variable, as the base type of classes, as a type parameter, etc).
    /// </summary>
    /// <param name="variableName">the name of the variable representing a type reference.</param>
    /// <param name="resolutionContext">context to be considered when applying syntax</param>
    /// <remarks>Some Api drivers will simply return the <paramref name="variableName"/> as is."/></remarks>
    /// <returns>an expression valid to be used in the specified <paramref name="resolutionContext"/></returns>
    ResolvedType ApplySpecificSyntax(string variableName, in TypeResolutionContext resolutionContext);
    ResolvedType MakeArrayType(ITypeSymbol elementType, in TypeResolutionContext resolutionContext);
    ResolvedType MakeGenericInstanceType(string typeName, ResolvedType openGenericType, INamedTypeSymbol genericTypeSymbol, in TypeResolutionContext resolutionContext);
    ResolvedType MakeGenericInstanceType(string typeName, ResolvedType openGenericType, Span<ResolvedType> typeArguments, in TypeResolutionContext resolutionContext);
    ResolvedType MakeByRefType(in ResolvedType resolvedType);

    Bcl Bcl { get; }
}
