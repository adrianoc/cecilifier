using System.Text;

namespace Cecilifier.Core.Extensions;

public static class ObjectExtensions
{
    public static string ValueText(this object value) => value switch
    { 
        string s => $"\"{s}\"",
        StringBuilder sb => ValueText(sb.ToString()),
        null => null,
        _ => value.ToString()
    };
}
