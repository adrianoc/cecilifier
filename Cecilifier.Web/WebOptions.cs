using System.Text.Json.Serialization;
using Cecilifier.Core.Naming;

namespace Cecilifier.Web
{
    /// <summary>
    /// Options that apply only to Cecilifier Web site. 
    /// </summary>
    public class WebOptions
    {
        [JsonPropertyName("deployKind")] public char DeployKind { get; set; }
    }

    public class ElementKindPrefix
    {
        [JsonPropertyName("prefix")] public string Prefix { get; set; }
        [JsonPropertyName("elementKind")] public ElementKind ElementKind { get; set; }
    }
    
    public class CecilifierSettings
    {
        [JsonPropertyName("elementKindPrefixes")] public ElementKindPrefix[] ElementKindPrefixes { get; set; }
        [JsonPropertyName("namingOptions")] public NamingOptions NamingOptions { get; set; }
    }
}
