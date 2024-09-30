using System.Collections.Generic;
using System.Text.Json.Serialization;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;

namespace Cecilifier.Web;

public class CecilifiedWebResult
{
    [JsonPropertyName("status")] public int Status { get; set; }
    [JsonPropertyName("data")] public string Data { get; set; }
    [JsonPropertyName("counter")] public int Counter { get; set; }
    [JsonPropertyName("clientsCounter")] public uint Clients { get; set; }
    [JsonPropertyName("maximumUnique")] public uint MaximumUnique { get; set; }
    [JsonPropertyName("kind")] public char Kind { get; set; }
    [JsonPropertyName("mappings")] public IList<Mapping> Mappings { get; set; }
    [JsonPropertyName("mainTypeName")] public string MainTypeName { get; set; }
    [JsonPropertyName("diagnostics")] public IList<CecilifierDiagnostic> Diagnostics { get; set; }
}
