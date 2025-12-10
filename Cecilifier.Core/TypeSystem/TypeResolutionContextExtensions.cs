#nullable enable
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.TypeSystem;

public static class TypeResolutionContextExtensions
{
    public static TypeResolutionContext ToTypeResolutionContext(this ResolveTargetKind kind, string? typeParameterProviderVar = null) => new(kind, TypeResolutionOptions.None, typeParameterProviderVar);
    public static TypeResolutionContext ToTypeResolutionContext(this IMethodSymbol methodSymbol, string? typeParameterProviderVar = null) => new(
        ResolveTargetKind.ReturnType, 
        (methodSymbol.ReturnsByRef ? TypeResolutionOptions.IsByRef : TypeResolutionOptions.None) | 
        (methodSymbol.ReturnType.IsValueType ? TypeResolutionOptions.IsValueType : TypeResolutionOptions.None),
        typeParameterProviderVar);
}
