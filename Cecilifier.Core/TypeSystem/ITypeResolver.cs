using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.TypeSystem
{
    public interface ITypeResolver
    {
        string Resolve(ITypeSymbol type, string cecilTypeParameterProviderVar = null);

        string ResolvePredefinedType(ITypeSymbol type);
        string ResolveLocalVariableType(ITypeSymbol type);

        Bcl Bcl { get; }
        string Resolve(string typeName);
    }
}
