using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.Extensions;

public static class PropertyExtensions
{
    public static bool HasCovariantGetter(this IPropertySymbol property) => property.IsOverride && !SymbolEqualityComparer.Default.Equals(property?.OverriddenProperty?.Type, property.Type);
}
