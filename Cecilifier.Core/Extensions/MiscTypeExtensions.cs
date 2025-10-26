namespace Cecilifier.Core.Extensions;

public static class MiscTypeExtensions
{
    public static string ToKeyword(this bool value) => value ? "true" : "false";
}
