using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.TypeSystem;

public interface IMemberResolver
{
    // Returns an expression? that represents the resolved method
    string ResolveMethod(IMethodSymbol method);
    string ResolveDefaultConstructor(ITypeSymbol type, string derivedTypeVar);
}
