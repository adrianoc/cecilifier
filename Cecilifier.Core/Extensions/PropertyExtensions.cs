using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.Extensions;

public static class PropertyExtensions
{
    public static bool HasCovariantGetter(this IPropertySymbol property) => property.IsOverride && property?.OverriddenProperty?.Type.Equals(property.Type) == false;
}
