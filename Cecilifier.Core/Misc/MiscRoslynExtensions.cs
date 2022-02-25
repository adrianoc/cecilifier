using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.Misc;

public static class MiscRoslynExtensions
{
    public static string AsParameterAttribute(this RefKind refKind) => refKind switch
    {
        RefKind.Out => Constants.ParameterAttributes.Out,
        RefKind.In => Constants.ParameterAttributes.In,
        _ => string.Empty,
    };
}
