using System.Text.Json;

namespace Logvu.Core.Config;

/// <summary>The JSON shape of a logvu configuration file. Every member is nullable: validation happens after deserialization.</summary>
internal sealed class LogvuConfigDocument
{
    public string? LogsDirectory { get; set; }

    public string? FilePrefix { get; set; }

    public string? FileNamePattern { get; set; }

    public string? ActiveFile { get; set; }

    public string? Format { get; set; }

    public string? TimeZone { get; set; }

    public int? RecentCapacity { get; set; }

    public string? MinLevel { get; set; }

    public List<SchemeDocument?>? Schemes { get; set; }

    public List<HighlightDocument?>? Highlights { get; set; }

    public Dictionary<string, StyleDocument?>? Styles { get; set; }

    public List<string?>? FrameworkPrefixes { get; set; }

    /// <summary>Each column as a string (its path) or an object, validated by the loader.</summary>
    public List<JsonElement>? Columns { get; set; }

    /// <summary>Each message field as a string, validated by the loader.</summary>
    public List<JsonElement>? MessageFields { get; set; }
}

/// <summary>The JSON shape of one configured text scheme.</summary>
internal sealed class SchemeDocument
{
    public string? Name { get; set; }

    public string? Header { get; set; }

    public List<string?>? TimeFormats { get; set; }
}

/// <summary>The JSON shape of one highlight rule.</summary>
internal sealed class HighlightDocument
{
    public string? Field { get; set; }

    public string? Pattern { get; set; }

    public string? Style { get; set; }
}

/// <summary>The JSON shape of one style.</summary>
internal sealed class StyleDocument
{
    public string? Foreground { get; set; }

    public string? Background { get; set; }

    public bool? Bold { get; set; }

    public bool? Italic { get; set; }

    public bool? Underline { get; set; }
}
