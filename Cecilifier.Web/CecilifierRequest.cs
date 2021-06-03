using System.Text.Json.Serialization;

namespace Cecilifier.Web
{
    public class CecilifierRequest
    {
        [JsonPropertyName("options")] public WebOptions WebOptions { get; set; }
        [JsonPropertyName("code")] public string Code { get; set; }
        [JsonPropertyName("settings")] public CecilifierSettings Settings { get; set; }
    }
}
