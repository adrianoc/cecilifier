#nullable enable
using System;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.TypeSystem;

public record ParameterSpec(string Name, ResolvedType ElementType, RefKind RefKind, string Attributes, DefaultValue DefaultValue = default, Func<IVisitorContext, ParameterSpec, string>? ElementTypeResolver = null)
{
    public virtual ResolvedType ElementType { get; } = ElementType;
    public string? RegistrationTypeName { get; init; }
    public virtual string? ParamsAttributeName { get; init; }
}

public record ParameterSymbolParameterSpec(IParameterSymbol Parameter, IVisitorContext Context, string MethodVariable) : ParameterSpec(Parameter.Name, string.Empty, Parameter.RefKind, Constants.ParameterAttributes.None)
{
    public override ResolvedType ElementType => Context.TypeResolver.Resolve(Parameter.Type, new TypeResolutionContext(ResolveTargetKind.Parameter, TypeResolutionOptions.None, MethodVariable));

    public override string? ParamsAttributeName  => Parameter.ParamsAttributeMatchingType(); 
}

public record struct DefaultValue(string? Value, bool Present)
{
    public static implicit operator DefaultValue(string value) => new DefaultValue(value, true);
}
