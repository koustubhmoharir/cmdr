using Cfgedit.Core.Json;

namespace Cfgedit.Core.Model;

/// <summary>Finds the format for a file name, or for a format name such as the command line's <c>--format</c>.</summary>
public static class Formats
{
    private static readonly IConfigFormat StrictJson = new JsonFormat(JsonDialect.Json);
    private static readonly IConfigFormat Jsonc = new JsonFormat(JsonDialect.Jsonc);
    private static readonly IConfigFormat Json5 = new JsonFormat(JsonDialect.Json5);
    private static readonly IConfigFormat Xml = new XmlFormat();
    private static readonly IConfigFormat Env = new EnvFormat();

    private static readonly HashSet<string> XmlExtensions = new(StringComparer.Ordinal)
    {
        ".xml", ".config", ".csproj", ".props", ".targets", ".xaml", ".resx", ".nuspec", ".xsd", ".xslt", ".svg",
    };

    /// <summary>
    /// The file extensions that <see cref="ForPath"/> recognises, lowercase with the dot: the JSON family first, then <c>.env</c>, then
    /// the XML extensions. A file named <c>.env.local</c> or <c>prod.env</c> is a .env file too, but its extension is not in this list.
    /// </summary>
    public static IReadOnlyList<string> KnownExtensions { get; } =
        [".json", ".jsonc", ".json5", ".env", .. XmlExtensions.Order(StringComparer.Ordinal)];

    /// <summary>
    /// The format named by a format name: <c>json</c> (strict), <c>jsonc</c>, <c>json5</c>, <c>xml</c> or <c>env</c>. The name is not
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
            "env" => Env,
            _ => null,
        };
    }

    /// <summary>
    /// The format for a file's name, which is not case-sensitive. The extension rules come first: <c>.json5</c> is JSON5,
    /// <c>.json</c> and <c>.jsonc</c> are JSONC, and the XML extensions are <c>.xml .config .csproj .props .targets .xaml .resx
    /// .nuspec .xsd .xslt .svg</c>. Only when no extension rule applies is the name checked for .env: <c>.env</c>, a name that starts
    /// with <c>.env.</c> (such as <c>.env.local</c>), or a name that ends with <c>.env</c> (such as <c>prod.env</c>). So
    /// <c>.env.json</c> is JSONC and <c>.env.xml</c> is XML. Returns null for any other name.
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

        if (XmlExtensions.Contains(extension))
        {
            return Xml;
        }

        string fileName = Path.GetFileName(path);
        if (fileName.Equals(".env", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".env", StringComparison.OrdinalIgnoreCase))
        {
            return Env;
        }

        return null;
    }
}
