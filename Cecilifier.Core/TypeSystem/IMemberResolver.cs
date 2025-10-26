using System.Collections.Generic;
using Cecilifier.Core.ApiDriver;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.TypeSystem;

public interface IMemberResolver
{
    // Returns an expression? that represents the resolved method
    string ResolveMethod(IMethodSymbol method);
    string ResolveMethod(string declaringTypeName, string declaringTypeVariable, string methodNameForVariableRegistration, string resolvedReturnType, IReadOnlyList<ParameterSpec> parameters, int typeParameterCountCount, MemberOptions options);
    
    string ResolveDefaultConstructor(ITypeSymbol baseType, string derivedTypeVar);
    string ResolveField(IFieldSymbol field);
}
