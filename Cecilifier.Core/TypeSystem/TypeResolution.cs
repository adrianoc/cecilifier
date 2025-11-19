#nullable enable
namespace Cecilifier.Core.TypeSystem;

public static class TypeResolution
{
    public static TypeResolutionContext DefaultContext = new(ResolveTargetKind.None, TypeResolutionOptions.None);
}
