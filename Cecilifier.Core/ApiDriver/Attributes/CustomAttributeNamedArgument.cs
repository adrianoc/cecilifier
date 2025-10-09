#nullable enable
namespace Cecilifier.Core.ApiDriver;

internal class CustomAttributeNamedArgument : CustomAttributeArgument
{
    public string Name;
    public NamedArgumentKind Kind { get; init; }
    public string ResolvedType { get; init; }
}
