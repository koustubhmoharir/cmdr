using System.Text;
using Logvu.Core.Config;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Detail;

namespace Logvu.Tests;

/// <summary>Builds the detail fixtures of the view and controller tests: events parsed as the store parses them, and configurations.</summary>
internal static class DetailFixtures
{
    /// <summary>The file every fixture event is read from.</summary>
    public const string FilePath = "/var/log/app.log";

    /// <summary>A log4net-core error whose message has a .NET exception with a Runner frame and three framework frames.</summary>
    public const string StackEvent =
        "ERROR 2026-11-18 10:20:31,007 [Worker 3] SheetKraft.Engine.Runner - Run failed\n"
        + "System.InvalidOperationException: Sheet not found\n"
        + "   at SheetKraft.Engine.Runner.Execute(Int32 id) in C:\\src\\Engine\\Runner.cs:line 88\n"
        + "   at System.Threading.Tasks.Task.Execute()\n"
        + "   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext)\n"
        + "   at System.Threading.ExecutionContext.Run(ExecutionContext executionContext)";

    /// <summary>A Serilog JSON event with a nested object under <c>Properties</c>.</summary>
    public const string JsonEvent =
        "{\"Timestamp\":\"2026-11-18T10:20:31.007Z\",\"Level\":\"Error\",\"RenderedMessage\":\"Failed\","
        + "\"Properties\":{\"Scope\":\"orders\",\"Customer\":{\"Id\":7}}}";

    /// <summary>The configuration every fixture uses: the logs in the configuration's directory, with UTC times.</summary>
    public const string BaseMembers =
        "\"logsDirectory\": \".\", \"filePrefix\": \"app\", \"fileNamePattern\": \"^app[.]log$\", \"timeZone\": \"utc\", \"minLevel\": \"any\"";

    /// <summary>A log4net-core text event with the given message, read as the store reads it.</summary>
    public static EventDetail.Loaded Text(string text) => Loaded(text, BuiltInSchemes.Log4NetCore);

    /// <summary>A Serilog JSON event, read as the store reads it.</summary>
    public static EventDetail.Loaded Json(string json) => Loaded(json, BuiltInSchemes.SerilogJson);

    /// <summary>An event's detail: its text parsed with <paramref name="scheme"/>, read from <see cref="FilePath"/> at <paramref name="offset"/>.</summary>
    public static EventDetail.Loaded Loaded(string text, ILogScheme scheme, long offset = 4096)
    {
        LogEvent e = scheme.Parse(text, new ParseContext(TimeZoneInfo.Utc));
        long length = Encoding.UTF8.GetByteCount(text) + 1;
        string preview = text.Split('\n')[0];
        var row = new EventRow(
            new RowKey(1, offset),
            new EventExtent(offset, offset + length, offset + Encoding.UTF8.GetByteCount(preview) + 1, Provisional: false, Orphan: false),
            e.Time,
            e.TimeText,
            e.Level,
            e.LevelText,
            preview,
            e.IsRaw,
            Orphan: false,
            Provisional: false);
        return new EventDetail.Loaded(row, e, text, false, FilePath, offset, length);
    }

    /// <summary>Loads a configuration in a temporary directory, from the base members and the given extra members.</summary>
    /// <param name="extra">Further members of the configuration object, each ending with a comma or none.</param>
    public static LogvuConfig Config(string extra = "")
    {
        string directory = Path.Combine(Path.GetTempPath(), $"logvu-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "logvu.json");
            File.WriteAllText(path, "{ " + extra + BaseMembers + " }");
            return LogvuConfig.Load(path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
