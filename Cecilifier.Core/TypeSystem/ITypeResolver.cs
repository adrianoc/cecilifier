using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.TypeSystem
{
    internal interface ITypeResolver
    {
        string Resolve(ITypeSymbol type, string cecilTypeParameterProviderVar = null);
        string Resolve(string typeName);

        string ResolvePredefinedType(ITypeSymbol type);
        string ResolveLocalVariableType(ITypeSymbol type);

        Bcl Bcl { get; }
    }
}
