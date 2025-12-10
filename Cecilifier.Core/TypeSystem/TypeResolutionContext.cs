#nullable enable
namespace Cecilifier.Core.TypeSystem;

public record struct TypeResolutionContext(ResolveTargetKind TargetKind, TypeResolutionOptions Options, string? TypeParameterProviderVar)
{
    public TypeResolutionContext(ResolveTargetKind targetKind, TypeResolutionOptions options) : this(targetKind, options, null)
    {
    }
    
    public static implicit operator TypeResolutionContext(ResolveTargetKind kind) => new(kind, TypeResolutionOptions.None);
}
