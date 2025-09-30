namespace Cecilifier.Core.ApiDriver.Handles;

public readonly record struct CilLocalVariableHandle(string Value)
{
    public string Value { get; } = Value;
}
