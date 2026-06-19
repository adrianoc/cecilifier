using System.Collections.Generic;
using Cecilifier.Core.ApiDriver;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.TypeSystem;

public interface IMemberResolver
{
    /// <summary>Returns an expression representing the resolve method.</summary>
    /// <remarks>
    /// The returned expression may be the name of a local variable that has been previously
    /// initialized with the correct expression to be used.
    /// Some Api Drivers (for example, Mono.Cecil) will generally return a 'real' expression,
    /// while others (like System.Reflection.Metadata) will store the final expression in a
    /// local variable (either to avoid noise/code duplication or due to the way it needs
    /// to emit the code) and return the name of the local variable.
    /// </remarks>
    /// <param name="method"></param>
    /// <returns>Returns an expression that represents the resolved method.</returns>
    string ResolveMethod(IMethodSymbol method);
    string ResolveMethod(string declaringTypeName, string declaringTypeVariable, string methodName, ResolvedType returnType, IReadOnlyList<ParameterSpec> parameters, IReadOnlyList<string> typeParameters, MemberOptions options);
    
    string ResolveDefaultConstructor(ITypeSymbol baseType, string derivedTypeVar);
    string ResolveField(IFieldSymbol field);
    string ResolveEventField(IEventSymbol aEvent);
    string ImportReference(string expression);
    
    string MakeGeneticInstanceMethod(string methodReferenceVariable, string methodName, IReadOnlyList<ResolvedType> resolvedTypeArguments);
}
