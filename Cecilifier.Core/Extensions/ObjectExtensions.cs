using System.Text;

namespace Cecilifier.Core.Extensions;

public static class ObjectExtensions
{
    public static string ValueText(this object value, bool nullLiteralAsString = false) => value switch
    {
        string s => $"\"{s}\"",
        StringBuilder sb => ValueText(sb.ToString()),
        bool b => b ? "true" : "false",
        null => nullLiteralAsString ? "null" : null,
        _ => value.ToString()
    };
}
