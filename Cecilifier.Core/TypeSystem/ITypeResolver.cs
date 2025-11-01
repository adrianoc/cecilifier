using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.TypeSystem
{
    public interface ITypeResolver
    {
        ResolvedType ResolveAny(ITypeSymbol type, ResolveTargetKind targetKind = ResolveTargetKind.None, string cecilTypeParameterProviderVar = null);
        ResolvedType ResolvePredefinedType(ITypeSymbol type);
        ResolvedType ResolveLocalVariableType(ITypeSymbol type);
        ResolvedType Resolve(string typeName);
        ResolvedType Resolve(ITypeSymbol type);
        ResolvedType MakeArrayType(ITypeSymbol elementType);

        Bcl Bcl { get; }
    }
}
