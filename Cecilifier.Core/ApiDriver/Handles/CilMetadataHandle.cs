namespace Cecilifier.Core.ApiDriver.Handles;

public readonly struct CilMetadataHandle(string handleVariable)
{
    public string VariableName { get; } = handleVariable;
    public static implicit operator string(CilMetadataHandle handle) => handle.VariableName;
    public override string ToString() => VariableName;
}
