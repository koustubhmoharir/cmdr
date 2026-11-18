using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.Core;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(RecentFilesData))]
internal sealed partial class RecentFilesJsonContext : JsonSerializerContext
{
}
