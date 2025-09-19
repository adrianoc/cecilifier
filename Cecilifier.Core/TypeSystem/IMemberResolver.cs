using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.TypeSystem;

public interface IMemberResolver
{
    // Returns an expression? that represents the resolved method
    string ResolveMethod(IMethodSymbol method);
    string ResolveDefaultConstructor(ITypeSymbol baseType, string derivedTypeVar);
    string ResolveField(IFieldSymbol field);
}
