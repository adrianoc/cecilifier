#nullable enable
using System;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.ApiDriver;

public record ParameterSpec(string Name, string ElementType, RefKind RefKind, string Attributes, string? DefaultValue = null, Func<IVisitorContext, string, string>? ElementTypeResolver = null)
{
    public string? RegistrationTypeName { get; init; }
}
