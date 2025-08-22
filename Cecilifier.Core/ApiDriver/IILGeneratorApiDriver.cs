#nullable enable
using System;
using System.Collections.Generic;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;

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

public record ParameterSpec(string Name, string ElementType, RefKind RefKind, string Attributes, string? DefaultValue = null, Func<IVisitorContext, string, string>? ElementTypeResolver = null)
{
    public string? RegistrationTypeName { get; init; }
}
