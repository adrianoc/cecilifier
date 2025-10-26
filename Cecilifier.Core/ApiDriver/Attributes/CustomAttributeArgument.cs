#nullable enable
namespace Cecilifier.Core.ApiDriver.Attributes;

public class CustomAttributeArgument
{
    public object? Value { get; set; }
    public CustomAttributeArgument[]? Values { get; set; }
}
