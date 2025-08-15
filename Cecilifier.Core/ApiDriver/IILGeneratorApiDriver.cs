#nullable enable
using System.Collections.Generic;
using Cecilifier.Core.AST;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.ApiDriver;


/// <summary>
/// Interface modeling libraries that emit IL the main one being Mono.Cecil (original target of the project). 
/// First attempt to extract a common interface to add support for System.Reflection.Metadata  
/// </summary>
public interface IILGeneratorApiDriver
{
    string AsCecilApplication(string cecilifiedCode, string mainTypeName, string? entryPointVar);
    int PreambleLineCount { get; }
    IReadOnlyCollection<string> AssemblyReferences { get; }
    
    IApiDriverDefinitionsFactory CreateDefinitionsFactory();
}

public interface IApiDriverDefinitionsFactory
{
    public string MappedTypeModifiersFor(INamedTypeSymbol type, SyntaxTokenList modifiers);
    
    public IEnumerable<string> Type(
        IVisitorContext context,
        string typeVar,
        string typeNamespace,
        string typeName,
        string attrs,
        string resolvedBaseType,
        DefinitionVariable outerTypeVariable,
        bool isStructWithNoFields,
        IEnumerable<ITypeSymbol> interfaces,
        IEnumerable<TypeParameterSyntax>? ownTypeParameters,
        IEnumerable<TypeParameterSyntax> outerTypeParameters,
        params string[] properties);
}
