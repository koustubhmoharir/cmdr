using Cfgedit.Core.Json;

namespace Cfgedit.Core.Model;

/// <summary>Finds the format for a file name, or for a format name such as the command line's <c>--format</c>.</summary>
public static class Formats
{
    private static readonly IConfigFormat StrictJson = new JsonFormat(JsonDialect.Json);
    private static readonly IConfigFormat Jsonc = new JsonFormat(JsonDialect.Jsonc);
    private static readonly IConfigFormat Json5 = new JsonFormat(JsonDialect.Json5);
    private static readonly IConfigFormat Xml = new XmlFormat();

    private static readonly HashSet<string> XmlExtensions = new(StringComparer.Ordinal)
    {
        ".xml", ".config", ".csproj", ".props", ".targets", ".xaml", ".resx", ".nuspec", ".xsd", ".xslt", ".svg",
    };

    /// <summary>
    /// The format named by a format name: <c>json</c> (strict), <c>jsonc</c>, <c>json5</c> or <c>xml</c>. The name is not
    /// case-sensitive. Returns null for any other name.
    /// </summary>
    public static IConfigFormat? FromName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.ToLowerInvariant() switch
        {
            "json" => StrictJson,
            "jsonc" => Jsonc,
            "json5" => Json5,
            "xml" => Xml,
            _ => null,
        };
    }

    /// <summary>
    /// The format for a file's extension, which is not case-sensitive. <c>.json5</c> is JSON5. <c>.json</c> and <c>.jsonc</c>
    /// are JSONC. The XML extensions are <c>.xml .config .csproj .props .targets .xaml .resx .nuspec .xsd .xslt .svg</c>.
    /// Returns null for any other extension, or for a path with none.
    /// </summary>
    public static IConfigFormat? ForPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".json5")
        {
            return Json5;
        }

        if (extension is ".json" or ".jsonc")
        {
            return Jsonc;
        }

        return XmlExtensions.Contains(extension) ? Xml : null;
    }
}
