#nullable enable
namespace Cecilifier.Core.ApiDriver.Attributes;

public class CustomAttributeNamedArgument : CustomAttributeArgument
{
    public required string Name { get; init; }
    public NamedArgumentKind Kind { get; init; }
    public required string ResolvedType { get; init; }
}
