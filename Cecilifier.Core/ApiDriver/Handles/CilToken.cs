namespace Cecilifier.Core.ApiDriver.Handles;

public readonly struct CilToken(string handleVariable)
{
    public string VariableName { get; } = handleVariable;
    public static implicit operator string(CilToken handle) => handle.VariableName;
    public override string ToString() => VariableName;
}
