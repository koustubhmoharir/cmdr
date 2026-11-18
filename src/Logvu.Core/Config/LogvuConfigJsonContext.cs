using System.Text.Json;
using System.Text.Json.Serialization;

namespace Logvu.Core.Config;

/// <summary>
/// Source-generated JSON for the logvu configuration. Member names are camelCase, comments and trailing commas are allowed, and an
/// unknown member is an error so that a misspelled setting is reported rather than ignored.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    PropertyNameCaseInsensitive = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(LogvuConfigDocument))]
internal sealed partial class LogvuConfigJsonContext : JsonSerializerContext
{
}
