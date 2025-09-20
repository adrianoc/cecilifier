using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.TypeSystem
{
    public interface ITypeResolver
    {
        string ResolveAny(ITypeSymbol type, string cecilTypeParameterProviderVar = null);
        string ResolvePredefinedType(ITypeSymbol type);
        string ResolveLocalVariableType(ITypeSymbol type);
        string Resolve(string typeName);
        string Resolve(ITypeSymbol type);
        string MakeArrayType(ITypeSymbol elementType);

        Bcl Bcl { get; }
    }
}
