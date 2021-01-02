using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.AST
{
    internal interface ITypeResolver
    {
        string Resolve(ITypeSymbol type);
        string Resolve(string typeName);

        string ResolvePredefinedType(ITypeSymbol type);
        string ResolveTypeLocalVariable(ITypeSymbol type);
    }
}
