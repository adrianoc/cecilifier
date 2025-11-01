#nullable enable
using Cecilifier.Core.TypeSystem;

namespace Cecilifier.Core.ApiDriver.Attributes;

public class CustomAttributeNamedArgument : CustomAttributeArgument
{
    public required string Name { get; init; }
    public NamedArgumentKind Kind { get; init; }
    public required ResolvedType ResolvedType { get; init; }
}
