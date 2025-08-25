namespace Cecilifier.Core.ApiDriver;

public struct CilMetadataHandle
{
    public CilMetadataHandle(string handleVariable) => VariableName = handleVariable;
    public string VariableName { get; }

    public static implicit operator string(CilMetadataHandle handle) => handle.VariableName;
    public override string ToString() => VariableName;
}
