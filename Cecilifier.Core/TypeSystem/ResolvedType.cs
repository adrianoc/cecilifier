namespace Cecilifier.Core.TypeSystem;

public readonly record struct ResolvedType
{
    private readonly string _resolved;
        
    public ResolvedType(string resolved) => _resolved = resolved;
        
    public string Expression => _resolved;

    public static implicit operator string(ResolvedType type) => type._resolved;
    public static implicit operator ResolvedType(string typeName) => new(typeName);
        
    public override string ToString() => _resolved;
}
