using System.Text.Json.Nodes;
using Logvu.Core.Config;
using Logvu.Core.Tests.Config;

namespace Logvu.Core.Tests.Store;

/// <summary>Builds real configurations for the store tests: each is written as a JSON file and loaded, as the application does.</summary>
internal static class StoreTestConfig
{
    /// <summary>The numbered-rotation pattern of the SheetKraft examples.</summary>
    public const string SheetKraftPattern = @"^SheetKraft(?:[0-9]+)?[.]log$";

    /// <summary>
    /// Writes <c>logvu.json</c> in <paramref name="dir"/>, with the logs in <paramref name="logsDirectory"/> (relative to it; the same
    /// directory by default), and loads it.
    /// </summary>
    public static LogvuConfig Create(
        ConfigTestDirectory dir,
        string prefix = "SheetKraft",
        string pattern = SheetKraftPattern,
        string? activeFile = null,
        string? format = null,
        string timeZone = "utc",
        JsonArray? schemes = null,
        string logsDirectory = ".")
    {
        var document = new JsonObject
        {
            ["logsDirectory"] = logsDirectory,
            ["filePrefix"] = prefix,
            ["fileNamePattern"] = pattern,
            ["timeZone"] = timeZone,
        };

        if (activeFile is not null)
        {
            document["activeFile"] = activeFile;
        }

        if (format is not null)
        {
            document["format"] = format;
        }

        if (schemes is not null)
        {
            document["schemes"] = schemes;
        }

        return LogvuConfig.Load(dir.Write("logvu.json", document.ToJsonString()));
    }
}
