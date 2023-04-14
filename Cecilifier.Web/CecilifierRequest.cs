using System.Text.Json.Serialization;

namespace Cecilifier.Web
{
    public class AssemblyReference
    {
        [JsonPropertyName("assemblyName")] public string AssemblyName { get; set; }
        [JsonPropertyName("base64Contents")] public string Base64Contents { get; set; }
        [JsonPropertyName("assemblyHash")] public string AssemblyHash { get; set; }
    }

    public class CecilifierRequest
    {
        [JsonPropertyName("options")] public WebOptions WebOptions { get; set; }
        [JsonPropertyName("code")] public string Code { get; set; }
        [JsonPropertyName("settings")] public CecilifierSettings Settings { get; set; }
        [JsonPropertyName("assemblyReferences")] public AssemblyReference[] AssemblyReferences { get; set; }
    }

    internal class AssemblyReferenceList
    {
        [JsonPropertyName("assemblyReferences")] public AssemblyReference[] AssemblyReferences { get; set; }
    }
}
