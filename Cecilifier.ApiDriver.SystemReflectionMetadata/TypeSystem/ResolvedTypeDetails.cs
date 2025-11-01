namespace Cecilifier.ApiDriver.SystemReflectionMetadata.TypeSystem;

public readonly record struct ResolvedTypeDetails(string TypeEncoderProvider, string MethodBuilder)
{
    public ResolvedTypeDetails WithTypeEncoder(string typeEncoderProvider) => new (typeEncoderProvider, MethodBuilder);
    public ResolvedTypeDetails WithMethodBuilder(string methodBuilder) => new (TypeEncoderProvider, methodBuilder);
    
    public override string ToString() => !string.IsNullOrWhiteSpace(TypeEncoderProvider) ? $"{TypeEncoderProvider}.{MethodBuilder}" : MethodBuilder;
}
