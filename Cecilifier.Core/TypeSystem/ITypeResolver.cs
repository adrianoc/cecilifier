#nullable enable

using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.TypeSystem;

public interface ITypeResolver
{
    ResolvedType ResolveAny(ITypeSymbol type, in TypeResolutionContext resolutionContext);
    ResolvedType ResolvePredefinedType(ITypeSymbol type, in TypeResolutionContext resolutionContext);
    ResolvedType ResolveLocalVariableType(ITypeSymbol type, in TypeResolutionContext context);
    ResolvedType Resolve(string typeName, in TypeResolutionContext resolutionContext);
    ResolvedType Resolve(ITypeSymbol type, in TypeResolutionContext resolutionContext);
    
    /// <summary>
    /// Some Api drivers may use different syntaxes depending on the usage (i.e. when being used to declare
    /// a local variable, when being used as the base type of a class, when being used as a type parameter, etc).
    ///
    /// Some Api drivers will simply return the <paramref name="variableName"/> as is."/> 
    /// </summary>
    /// <param name="variableName">the name of the variable representing a type reference.</param>
    /// <param name="resolutionContext">context to be considered when applying syntax</param>
    /// <returns>an expression valid to be used in the specified <paramref name="resolutionContext"/></returns>
    ResolvedType ApplySpecificSyntax(string variableName, in TypeResolutionContext resolutionContext);
    
    ResolvedType MakeArrayType(ITypeSymbol elementType, in TypeResolutionContext resolutionContext);

    Bcl Bcl { get; }
}
