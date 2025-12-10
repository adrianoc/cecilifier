#nullable enable
using System;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.TypeSystem;

public record ParameterSpec(string Name, ResolvedType ElementType, RefKind RefKind, string Attributes, string? DefaultValue = null, Func<IVisitorContext, string, string>? ElementTypeResolver = null)
{
    public virtual ResolvedType ElementType { get; } = ElementType;
    public string? RegistrationTypeName { get; init; }
    public virtual string? ParamsAttributeName { get; init; }
}

public record ParameterSymbolParameterSpec(IParameterSymbol Parameter, IVisitorContext Context) : ParameterSpec(Parameter.Name, string.Empty, Parameter.RefKind, Constants.ParameterAttributes.None)
{
    public override ResolvedType ElementType => Context.TypeResolver.ResolveAny(Parameter.Type, ResolveTargetKind.Parameter);

    public override string? ParamsAttributeName  => Parameter.ParamsAttributeMatchingType(); 
}
