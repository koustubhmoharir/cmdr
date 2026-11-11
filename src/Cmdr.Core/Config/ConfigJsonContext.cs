using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cmdr.Core.Config;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(CmdrConfig))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext
{
}
