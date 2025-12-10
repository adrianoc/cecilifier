namespace Cecilifier.ApiDriver.SystemReflectionMetadata.TypeSystem;

public readonly record struct ResolvedTypeDetails(string TypeEncoderProvider, string MethodBuilder)
{
    private readonly string _typeEncoderProvider = TypeEncoderProvider;
    
    public ResolvedTypeDetails WithTypeEncoder(string typeEncoderProvider) => new (typeEncoderProvider, MethodBuilder);
    public ResolvedTypeDetails WithMethodBuilder(string methodBuilder) => new (_typeEncoderProvider, methodBuilder);
    public string TypeEncoderProvider => _typeEncoderProvider.Replace("%", "");
    public override string ToString() => !string.IsNullOrWhiteSpace(_typeEncoderProvider) ? _typeEncoderProvider.Replace("%", $".{MethodBuilder}") : MethodBuilder;
}
