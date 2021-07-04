using System.Text.Json.Serialization;

namespace Cecilifier.Web
{
    /// <summary>
    /// Options that apply only to Cecilifier Web site. 
    /// </summary>
    public class WebOptions
    {
        [JsonPropertyName("deployKind")] public char DeployKind { get; set; }
        [JsonPropertyName("publishSourcePolicy")] public char PublishSourcePolicy { get; set; }
    }
}
