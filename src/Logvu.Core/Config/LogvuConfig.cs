using System.Text.RegularExpressions;
using Logvu.Core.Parsing;

namespace Logvu.Core.Config;

/// <summary>
/// A validated, immutable logvu configuration: one set of files from one directory, and the settings that read and show them.
/// Create it with <see cref="Load"/>, or with <see cref="LogvuConfigFile"/> to run the script with cancellation.
/// </summary>
public sealed class LogvuConfig
{
    /// <summary>The longest time any configured regular expression may run on one input before the match counts as no match.</summary>
    internal static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>The recent-history capacity used when the configuration does not set one.</summary>
    public const int DefaultRecentCapacity = 20000;

    /// <summary>The minimum level the timeline starts with when the configuration does not set <c>minLevel</c>.</summary>
    public const LogLevel DefaultMinLevel = LogLevel.Info;

    /// <summary>The smallest allowed <c>recentCapacity</c>.</summary>
    public const int MinRecentCapacity = 100;

    /// <summary>The largest allowed <c>recentCapacity</c>.</summary>
    public const int MaxRecentCapacity = 1000000;

    /// <summary>The built-in style names, which a highlight rule may use without defining them under <c>styles</c>.</summary>
    public static IReadOnlyList<string> BuiltInStyles { get; } =
        ["error", "warning", "info", "debug", "accent", "emphasis", "dim", "success"];

    /// <summary>The namespace or package prefixes whose stack frames are dimmed, used when the configuration does not set them.</summary>
    public static IReadOnlyList<string> DefaultFrameworkPrefixes { get; } =
        ["System.", "Microsoft.", "Newtonsoft.", "log4net.", "Serilog.", "runtime.", "net/http.", "github.com/labstack/"];

    internal LogvuConfig(
        string configPath,
        string logsDirectory,
        string filePrefix,
        Regex fileNamePattern,
        string? activeFile,
        ILogScheme? format,
        IReadOnlyList<ILogScheme> schemes,
        TimeZoneInfo timeZone,
        int recentCapacity,
        LogLevel? minLevel,
        IReadOnlyList<ColumnSpec> columns,
        IReadOnlyList<HighlightRule> highlights,
        IReadOnlyDictionary<string, StyleSpec> styles,
        IReadOnlyList<string> frameworkPrefixes,
        IReadOnlyList<string>? messageFields = null)
    {
        ConfigPath = configPath;
        LogsDirectory = logsDirectory;
        FilePrefix = filePrefix;
        FileNamePattern = fileNamePattern;
        ActiveFile = activeFile;
        Format = format;
        Schemes = schemes;
        TimeZone = timeZone;
        RecentCapacity = recentCapacity;
        MinLevel = minLevel;
        Columns = columns;
        MessageFields = messageFields ?? [];
        Highlights = highlights;
        Styles = styles;
        FrameworkPrefixes = frameworkPrefixes;
    }

    /// <summary>The full path of the configuration file.</summary>
    public string ConfigPath { get; }

    /// <summary>The full path of the directory holding the set. A relative setting is resolved against the configuration file's directory.</summary>
    public string LogsDirectory { get; }

    /// <summary>The literal start of every member's basename.</summary>
    public string FilePrefix { get; }

    /// <summary>
    /// The pattern a member's whole basename must match. It is wrapped as <c>\A(?:…)\z</c>, case-insensitive on Windows and macOS, and
    /// runs with a 250 ms match timeout.
    /// </summary>
    public Regex FileNamePattern { get; }

    /// <summary>The basename of the fixed current file, or <c>null</c> when the set has none.</summary>
    public string? ActiveFile { get; }

    /// <summary>The scheme every file is read with, or <c>null</c> to detect the scheme of each file from its content.</summary>
    public ILogScheme? Format { get; }

    /// <summary>All candidate schemes: the built-in schemes, then the configured ones in configuration order.</summary>
    public IReadOnlyList<ILogScheme> Schemes { get; }

    /// <summary>The zone in which timestamps without an offset are read.</summary>
    public TimeZoneInfo TimeZone { get; }

    /// <summary>The number of events kept in the recent-history ring.</summary>
    public int RecentCapacity { get; }

    /// <summary>
    /// The minimum level the timeline starts with, or <c>null</c> for no minimum (<c>"any"</c>). Set by <c>minLevel</c>; the default is
    /// <see cref="DefaultMinLevel"/>. Events below it are not shown until the user changes the filter.
    /// </summary>
    public LogLevel? MinLevel { get; }

    /// <summary>
    /// The extra columns of the timeline table, in configuration order. Empty when the configuration names none. Each row carries one value
    /// per column; see <see cref="Store.EventRow.Columns"/>.
    /// </summary>
    public IReadOnlyList<ColumnSpec> Columns { get; }

    /// <summary>
    /// The JSON paths whose values a JSON event's timeline preview shows after its message as <c>path=value</c>, in configuration order. Each
    /// path is resolved on the event's JSON only; a path that does not resolve is skipped. Empty when the configuration names none.
    /// </summary>
    public IReadOnlyList<string> MessageFields { get; }

    /// <summary>The highlight rules, in configuration order.</summary>
    public IReadOnlyList<HighlightRule> Highlights { get; }

    /// <summary>The configured styles, keyed by name ignoring case. Built-in style names are not included; see <see cref="BuiltInStyles"/>.</summary>
    public IReadOnlyDictionary<string, StyleSpec> Styles { get; }

    /// <summary>The framework prefixes whose stack frames are dimmed and foldable. An empty list means none.</summary>
    public IReadOnlyList<string> FrameworkPrefixes { get; }

    /// <summary>
    /// Reads, parses and validates the configuration file at <paramref name="configPath"/>, runs its <c>envScript</c> if it has one, and
    /// resolves <c>logsDirectory</c>: a relative directory is resolved against the file's own directory.
    /// </summary>
    /// <remarks>
    /// This blocks the calling thread while the script runs, and the script cannot be cancelled. The application uses
    /// <see cref="LogvuConfigFile.Read(string)"/> and <see cref="LogvuConfigFile.ResolveAsync"/> instead, so that it can show the
    /// script's progress and cancel it.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="configPath"/> is <c>null</c> or empty.</exception>
    /// <exception cref="LogvuConfigException">
    /// The file cannot be read or is malformed JSON, a setting is missing, of the wrong type, unknown or invalid, the script fails, or a
    /// variable of <c>logsDirectory</c> is not set. The message names the file and the JSON path of the setting.
    /// </exception>
    public static LogvuConfig Load(string configPath) =>
        LogvuConfigFile.Read(configPath).ResolveAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Tells whether <paramref name="regex"/> matches <paramref name="text"/>, treating a match timeout as no match.</summary>
    internal static bool TryMatch(Regex regex, string text)
    {
        try
        {
            return regex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
