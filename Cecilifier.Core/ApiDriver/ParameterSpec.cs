#nullable enable
using System;
using System.Reflection.Metadata;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.ApiDriver;

public record ParameterSpec(string Name, string ElementType, RefKind RefKind, string Attributes, string? DefaultValue = null, Func<IVisitorContext, string, string>? ElementTypeResolver = null)
{
    public virtual string ElementType { get; } = ElementType;
    public string? RegistrationTypeName { get; init; }
    public virtual string? ParamsAttributeName { get; init; }
}

public record ParameterSymbolParameterSpec(IParameterSymbol Parameter, IVisitorContext Context) : ParameterSpec(Parameter.Name, string.Empty, Parameter.RefKind, Constants.ParameterAttributes.None)
{
    public override string ElementType => Context.TypeResolver.ResolveAny(Parameter.Type, ResolveTargetKind.Parameter);

    public override string? ParamsAttributeName  => Parameter.ParamsAttributeMatchingType(); 
}
