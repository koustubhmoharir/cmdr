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
[JsonSerializable(typeof(GroupSpec))]
[JsonSerializable(typeof(CommandSpec))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext
{
}
