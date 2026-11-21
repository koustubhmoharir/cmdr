using System.Text.Json;
using System.Text.RegularExpressions;
using Logvu.Core.Filter;
using Logvu.Core.Parsing;
using Shared.Core;
using Shared.Processes;

namespace Logvu.Core.Config;

/// <summary>Reads, parses and validates a logvu configuration file. See <see cref="LogvuConfigFile.Read(string)"/>.</summary>
/// <remarks>
/// Validation runs once over the members in the order the configuration documents them, and reports the first error. A format or
/// highlight style that names a scheme or style defined further down is checked against the names alone, so that the error is
/// reported where the reference is. Nothing is run here: the environment script runs when the file is resolved.
/// </remarks>
internal static class LogvuConfigLoader
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>The timeout of an <c>envScript</c> that does not set <c>timeoutSeconds</c>.</summary>
    private const int DefaultScriptTimeoutSeconds = 30;

    /// <summary>The level names <c>minLevel</c> accepts, ignoring case, besides <c>"any"</c>.</summary>
    private static readonly (string Name, LogLevel Level)[] MinLevelNames =
    [
        ("trace", LogLevel.Trace),
        ("debug", LogLevel.Debug),
        ("info", LogLevel.Info),
        ("warn", LogLevel.Warn),
        ("warning", LogLevel.Warn),
        ("error", LogLevel.Error),
        ("fatal", LogLevel.Fatal),
    ];

    /// <summary>Reads, parses and validates the file at <paramref name="configPath"/>. <paramref name="runner"/> runs its script later.</summary>
    internal static LogvuConfigFile Read(string configPath, EnvScriptRunner runner)
    {
        ArgumentException.ThrowIfNullOrEmpty(configPath);

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(configPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new LogvuConfigException($"Invalid configuration path '{configPath}': {ex.Message}", ex);
        }

        LogvuConfigDocument document = Deserialize(fullPath);
        return new Builder(fullPath, runner).Build(document);
    }

    private static LogvuConfigDocument Deserialize(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new LogvuConfigException($"Could not read configuration file '{path}': {ex.Message}", ex);
        }

        if (bytes.AsSpan().StartsWith(Utf8Bom))
        {
            bytes = bytes[Utf8Bom.Length..];
        }

        LogvuConfigDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(bytes, LogvuConfigJsonContext.Default.LogvuConfigDocument);
        }
        catch (JsonException ex)
        {
            throw new LogvuConfigException(
                $"Malformed JSON in '{path}' at line {(ex.LineNumber ?? 0) + 1}, position {(ex.BytePositionInLine ?? 0) + 1}: " +
                $"{Detail(ex.Message)} (path: {ex.Path ?? "$"})",
                ex);
        }

        if (document is null)
        {
            throw new LogvuConfigException($"Configuration file '{path}': $: expected a JSON object, found null");
        }

        return document;
    }

    /// <summary>The message of a JSON exception without its trailing location suffix, which is reported separately.</summary>
    private static string Detail(string message)
    {
        int end = message.Length;
        foreach (string marker in new[] { " Path: ", " LineNumber: " })
        {
            int index = message.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0 && index < end)
            {
                end = index;
            }
        }

        return message[..end];
    }

    /// <summary>Validates one loaded document and builds the model. Holds the configuration path for the error messages.</summary>
    private sealed class Builder(string configPath, EnvScriptRunner runner)
    {
        private readonly string _configPath = configPath;
        private readonly EnvScriptRunner _runner = runner;

        internal LogvuConfigFile Build(LogvuConfigDocument document)
        {
            string configDirectory = Path.GetDirectoryName(_configPath)!;

            EnvScriptSpec? envScript = BuildEnvScript(document.EnvScript, configDirectory);

            // The directory is expanded and resolved once the script has run (see LogvuConfigFile.ResolveAsync).
            string logsDirectoryTemplate = RequireText(document.LogsDirectory, "logsDirectory");
            CheckPlaceholders(logsDirectoryTemplate, "logsDirectory");

            string filePrefix = RequireText(document.FilePrefix, "filePrefix");

            Regex fileNamePattern = BuildFileNamePattern(RequireText(document.FileNamePattern, "fileNamePattern"));

            string? activeFile = ValidateActiveFile(document.ActiveFile, filePrefix, fileNamePattern);

            string? formatName = document.Format;
            IEnumerable<string> configuredSchemeNames = (document.Schemes ?? [])
                .Where(scheme => scheme?.Name is not null)
                .Select(scheme => scheme!.Name!);
            if (formatName is not null && !IsAutomatic(formatName) && BuiltInSchemes.Find(formatName) is null
                && !configuredSchemeNames.Contains(formatName, StringComparer.OrdinalIgnoreCase))
            {
                throw Error("format", $"unknown scheme '{formatName}': not a built-in scheme and not defined under schemes");
            }

            TimeZoneInfo timeZone = ResolveTimeZone(document.TimeZone ?? "local");

            int recentCapacity = document.RecentCapacity ?? LogvuConfig.DefaultRecentCapacity;
            if (recentCapacity is < LogvuConfig.MinRecentCapacity or > LogvuConfig.MaxRecentCapacity)
            {
                throw Error(
                    "recentCapacity",
                    $"must be between {LogvuConfig.MinRecentCapacity} and {LogvuConfig.MaxRecentCapacity}, got {recentCapacity}");
            }

            LogLevel? minLevel = ParseMinLevel(document.MinLevel);

            IReadOnlyList<ColumnSpec> columns = BuildColumns(document.Columns);

            IReadOnlyList<string> messageFields = BuildMessageFields(document.MessageFields);

            Dictionary<string, ILogScheme> configuredSchemes = BuildSchemes(document.Schemes, out List<ILogScheme> schemes);

            IEnumerable<string> styleNames = document.Styles is null ? Enumerable.Empty<string>() : document.Styles.Keys;
            List<HighlightRule> highlights = BuildHighlights(document.Highlights, styleNames);

            Dictionary<string, StyleSpec> styles = BuildStyles(document.Styles);

            IReadOnlyList<string> frameworkPrefixes = BuildFrameworkPrefixes(document.FrameworkPrefixes);

            ILogScheme? format = null;
            if (formatName is not null && !IsAutomatic(formatName))
            {
                format = BuiltInSchemes.Find(formatName)
                    ?? (configuredSchemes.TryGetValue(formatName, out ILogScheme? configured)
                        ? configured
                        : throw Error("format", $"unknown scheme '{formatName}'"));
            }

            return new LogvuConfigFile(
                _configPath,
                logsDirectoryTemplate,
                envScript,
                logsDirectory => new LogvuConfig(
                    _configPath,
                    logsDirectory,
                    filePrefix,
                    fileNamePattern,
                    activeFile,
                    format,
                    schemes,
                    timeZone,
                    recentCapacity,
                    minLevel,
                    columns,
                    highlights,
                    styles,
                    frameworkPrefixes,
                    messageFields),
                _runner);
        }

        /// <summary>
        /// Validates <c>envScript</c>: its program, its arguments (no input placeholders, which cannot be asked for when the file loads),
        /// its working directory and its timeout. Null when the file has no script.
        /// </summary>
        private EnvScriptSpec? BuildEnvScript(EnvScriptDocument? document, string configDirectory)
        {
            if (document is null)
            {
                return null;
            }

            string program = RequireText(document.Program, "envScript.program");
            RejectNul(program, "envScript.program");

            var args = new List<string>();
            List<string?> argDocuments = document.Args ?? [];
            for (int i = 0; i < argDocuments.Count; i++)
            {
                string argPath = $"envScript.args[{i}]";
                string? arg = argDocuments[i];
                if (arg is null)
                {
                    throw Error(argPath, "must be a string, not null");
                }

                RejectNul(arg, argPath);
                CheckPlaceholders(arg, argPath);
                args.Add(arg);
            }

            string? cwd = document.Cwd;
            if (cwd is not null)
            {
                RejectNul(cwd, "envScript.cwd");
                CheckDirectory(cwd, configDirectory, "envScript.cwd");
            }

            int timeoutSeconds = document.TimeoutSeconds ?? DefaultScriptTimeoutSeconds;
            if (timeoutSeconds <= 0)
            {
                throw Error("envScript.timeoutSeconds", $"must be greater than 0, got {timeoutSeconds}");
            }

            return new EnvScriptSpec(program, args, cwd, TimeSpan.FromSeconds(timeoutSeconds));
        }

        private LogvuConfigException Error(string jsonPath, string problem) =>
            new($"Configuration file '{_configPath}': {jsonPath}: {problem}");

        /// <summary>Rejects a NUL character, which a process argument, a program or a path cannot carry.</summary>
        private void RejectNul(string text, string jsonPath)
        {
            if (text.Contains('\0'))
            {
                throw Error(jsonPath, "must not contain a NUL character");
            }
        }

        /// <summary>
        /// Rejects malformed placeholders and input placeholders in a setting that is expanded when it is used. The first malformed one is
        /// reported; an input placeholder is refused because nothing asks the user for input here.
        /// </summary>
        private void CheckPlaceholders(string text, string jsonPath)
        {
            IReadOnlyList<string> errors = Placeholders.Errors(text);
            if (errors.Count > 0)
            {
                throw Error(jsonPath, string.Join("; ", errors));
            }

            IReadOnlyList<InputRequest> requests = Placeholders.Requests([text]);
            if (requests.Count > 0)
            {
                throw Error(jsonPath, $"input placeholders are not supported here: '${{input:{requests[0].Name}}}'");
            }
        }

        private string RequireText(string? value, string jsonPath)
        {
            if (value is null)
            {
                throw Error(jsonPath, "is required");
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw Error(jsonPath, "must not be empty");
            }

            return value;
        }

        /// <summary>Parses <c>minLevel</c>: <c>"any"</c> is no minimum (<c>null</c>); an absent value is <see cref="LogvuConfig.DefaultMinLevel"/>.</summary>
        private LogLevel? ParseMinLevel(string? text)
        {
            if (text is null)
            {
                return LogvuConfig.DefaultMinLevel;
            }

            if (string.Equals(text, "any", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            foreach ((string name, LogLevel level) in MinLevelNames)
            {
                if (string.Equals(text, name, StringComparison.OrdinalIgnoreCase))
                {
                    return level;
                }
            }

            throw Error(
                "minLevel",
                $"unknown level '{text}': expected one of any, trace, debug, info, warn (or warning), error, fatal");
        }

        /// <summary>
        /// Validates <c>columns</c> in order. An item is a path string or an object with <c>path</c>, and optionally <c>title</c>,
        /// <c>width</c> and <c>keepEnd</c>. An absent value is an empty list.
        /// </summary>
        private IReadOnlyList<ColumnSpec> BuildColumns(List<JsonElement>? documents)
        {
            if (documents is null)
            {
                return [];
            }

            var columns = new List<ColumnSpec>(documents.Count);
            for (int i = 0; i < documents.Count; i++)
            {
                columns.Add(BuildColumn(documents[i], $"columns[{i}]"));
            }

            return columns;
        }

        /// <summary>
        /// Validates <c>messageFields</c> in order. Each item is a JSON path string, checked like a column's path. An absent value is an empty list.
        /// </summary>
        private IReadOnlyList<string> BuildMessageFields(List<JsonElement>? documents)
        {
            if (documents is null)
            {
                return [];
            }

            var paths = new List<string>(documents.Count);
            for (int i = 0; i < documents.Count; i++)
            {
                string itemPath = $"messageFields[{i}]";
                JsonElement item = documents[i];
                if (item.ValueKind != JsonValueKind.String)
                {
                    throw Error(itemPath, $"must be a string, found {item.ValueKind.ToString().ToLowerInvariant()}");
                }

                paths.Add(ValidPath(item.GetString()!, itemPath));
            }

            return paths;
        }

        private ColumnSpec BuildColumn(JsonElement item, string itemPath)
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.String:
                    return new ColumnSpec(ValidPath(item.GetString()!, itemPath));
                case JsonValueKind.Object:
                    return BuildColumnObject(item, itemPath);
                default:
                    throw Error(itemPath, $"must be a string or an object, found {item.ValueKind.ToString().ToLowerInvariant()}");
            }
        }

        /// <summary>Validates an object column: its members are checked as they are read, then its required path.</summary>
        private ColumnSpec BuildColumnObject(JsonElement item, string itemPath)
        {
            string? path = null;
            string? title = null;
            int width = ColumnSpec.DefaultMaxWidth;
            bool keepEnd = false;
            foreach (JsonProperty property in item.EnumerateObject())
            {
                string memberPath = $"{itemPath}.{property.Name}";
                JsonElement value = property.Value;
                switch (property.Name)
                {
                    case "path":
                        path = value.ValueKind == JsonValueKind.Null ? null : TextMember(value, memberPath);
                        break;
                    case "title":
                        title = value.ValueKind == JsonValueKind.Null ? null : TextMember(value, memberPath);
                        if (title is not null && string.IsNullOrWhiteSpace(title))
                        {
                            throw Error(memberPath, "must not be empty");
                        }

                        break;
                    case "width":
                        if (value.ValueKind != JsonValueKind.Null)
                        {
                            width = WidthMember(value, memberPath);
                        }

                        break;
                    case "keepEnd":
                        if (value.ValueKind != JsonValueKind.Null)
                        {
                            keepEnd = BoolMember(value, memberPath);
                        }

                        break;
                    default:
                        throw Error(memberPath, $"unknown member '{property.Name}': expected path, title, width or keepEnd");
                }
            }

            string pathPath = $"{itemPath}.path";
            return new ColumnSpec(ValidPath(RequireText(path, pathPath), pathPath), title, width, keepEnd);
        }

        private string TextMember(JsonElement value, string memberPath) =>
            value.ValueKind == JsonValueKind.String
                ? value.GetString()!
                : throw Error(memberPath, $"must be a string, found {value.ValueKind.ToString().ToLowerInvariant()}");

        private bool BoolMember(JsonElement value, string memberPath) =>
            value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : throw Error(memberPath, $"must be true or false, found {value.ValueKind.ToString().ToLowerInvariant()}");

        private int WidthMember(JsonElement value, string memberPath)
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int width))
            {
                throw Error(memberPath, $"must be an integer between {ColumnSpec.MinWidth} and {ColumnSpec.MaxAllowedWidth}");
            }

            if (width is < ColumnSpec.MinWidth or > ColumnSpec.MaxAllowedWidth)
            {
                throw Error(memberPath, $"must be between {ColumnSpec.MinWidth} and {ColumnSpec.MaxAllowedWidth}, got {width}");
            }

            return width;
        }

        /// <summary>Checks a column's or message field's path against the JSON path syntax, which a text event's field name also satisfies.</summary>
        private string ValidPath(string path, string jsonPath)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw Error(jsonPath, "must not be empty");
            }

            if (!JsonFieldPath.IsWellFormed(path))
            {
                throw Error(
                    jsonPath,
                    $"'{path}' is not a valid path: segments are separated by dots, and each is a name optionally followed by [n] indices");
            }

            return path;
        }

        /// <summary>Checks that a directory setting is a valid path once resolved against the configuration file's folder.</summary>
        private void CheckDirectory(string text, string configDirectory, string jsonPath)
        {
            try
            {
                _ = Path.GetFullPath(Path.Combine(configDirectory, text));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw Error(jsonPath, $"not a valid directory path: {ex.Message}");
            }
        }

        private Regex BuildFileNamePattern(string pattern)
        {
            CompileExpression("fileNamePattern", pattern, RegexOptions.None);

            // The pattern is validated alone first: wrapping an unbalanced pattern could otherwise yield a valid expression.
            RegexOptions options = RegexOptions.CultureInvariant;
            if (PathComparison.Comparer.Equals("a", "A"))
            {
                options |= RegexOptions.IgnoreCase;
            }

            return CompileExpression("fileNamePattern", @"\A(?:" + pattern + @")\z", options);
        }

        private string? ValidateActiveFile(string? activeFile, string filePrefix, Regex fileNamePattern)
        {
            if (activeFile is null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(activeFile))
            {
                throw Error("activeFile", "must not be empty");
            }

            if (Path.GetFileName(activeFile) != activeFile)
            {
                throw Error("activeFile", "must be a file name, not a path");
            }

            if (activeFile.Length < filePrefix.Length
                || !PathComparison.Comparer.Equals(activeFile[..filePrefix.Length], filePrefix))
            {
                throw Error("activeFile", $"'{activeFile}' does not start with filePrefix '{filePrefix}'");
            }

            if (!LogvuConfig.TryMatch(fileNamePattern, activeFile))
            {
                throw Error("activeFile", $"'{activeFile}' does not match fileNamePattern");
            }

            return activeFile;
        }

        private TimeZoneInfo ResolveTimeZone(string text)
        {
            if (string.Equals(text, "local", StringComparison.OrdinalIgnoreCase))
            {
                return TimeZoneInfo.Local;
            }

            if (string.Equals(text, "utc", StringComparison.OrdinalIgnoreCase))
            {
                return TimeZoneInfo.Utc;
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(text);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
            {
                throw Error("timeZone", $"unknown time zone '{text}'");
            }
        }

        /// <summary>Validates the configured schemes in order and builds them.</summary>
        /// <returns>The configured schemes by name, ignoring case.</returns>
        private Dictionary<string, ILogScheme> BuildSchemes(List<SchemeDocument?>? documents, out List<ILogScheme> schemes)
        {
            schemes = [.. BuiltInSchemes.All];
            var configured = new Dictionary<string, ILogScheme>(StringComparer.OrdinalIgnoreCase);
            if (documents is null)
            {
                return configured;
            }

            for (int i = 0; i < documents.Count; i++)
            {
                SchemeDocument? document = documents[i]
                    ?? throw Error($"schemes[{i}]", "must be an object, not null");
                string namePath = $"schemes[{i}].name";
                string headerPath = $"schemes[{i}].header";

                string name = RequireText(document.Name, namePath);
                if (string.Equals(name, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    throw Error(namePath, "'auto' is reserved for automatic detection");
                }

                if (BuiltInSchemes.Find(name) is not null)
                {
                    throw Error(namePath, $"'{name}' is the name of a built-in scheme");
                }

                if (configured.ContainsKey(name))
                {
                    throw Error(namePath, $"duplicate scheme name '{name}'");
                }

                string header = RequireText(document.Header, headerPath);
                Regex headerExpression = CompileExpression(headerPath, header, RegexOptions.None);
                if (!headerExpression.GetGroupNames().Contains("message"))
                {
                    throw Error(headerPath, "must have a named group 'message'");
                }

                var timeFormats = new List<string>();
                if (document.TimeFormats is not null)
                {
                    for (int j = 0; j < document.TimeFormats.Count; j++)
                    {
                        string? format = document.TimeFormats[j];
                        string formatPath = $"schemes[{i}].timeFormats[{j}]";
                        if (format is null)
                        {
                            throw Error(formatPath, "must not be null");
                        }

                        if (string.IsNullOrWhiteSpace(format))
                        {
                            throw Error(formatPath, "must not be empty");
                        }

                        timeFormats.Add(format);
                    }
                }

                TextScheme scheme;
                try
                {
                    scheme = new TextScheme(name, header, timeFormats);
                }
                catch (ArgumentException ex)
                {
                    throw Error(headerPath, ex.Message);
                }

                configured.Add(name, scheme);
                schemes.Add(scheme);
            }

            return configured;
        }

        private List<HighlightRule> BuildHighlights(List<HighlightDocument?>? documents, IEnumerable<string> styleNames)
        {
            var highlights = new List<HighlightRule>();
            if (documents is null)
            {
                return highlights;
            }

            var names = new HashSet<string>(styleNames, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < documents.Count; i++)
            {
                HighlightDocument document = documents[i]
                    ?? throw Error($"highlights[{i}]", "must be an object, not null");
                string fieldPath = $"highlights[{i}].field";
                string patternPath = $"highlights[{i}].pattern";
                string stylePath = $"highlights[{i}].style";

                string field = RequireText(document.Field, fieldPath);
                Regex pattern = CompileExpression(patternPath, RequireText(document.Pattern, patternPath), RegexOptions.None);
                string style = RequireText(document.Style, stylePath);
                if (!IsBuiltInStyle(style) && !names.Contains(style))
                {
                    throw Error(stylePath, $"unknown style '{style}'");
                }

                highlights.Add(new HighlightRule(field, pattern, style));
            }

            return highlights;
        }

        private Dictionary<string, StyleSpec> BuildStyles(Dictionary<string, StyleDocument?>? documents)
        {
            var styles = new Dictionary<string, StyleSpec>(StringComparer.OrdinalIgnoreCase);
            if (documents is null)
            {
                return styles;
            }

            foreach ((string name, StyleDocument? document) in documents)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw Error("styles", "a style name must not be empty");
                }

                string path = $"styles.{name}";
                if (IsBuiltInStyle(name))
                {
                    throw Error(path, $"'{name}' is a built-in style name");
                }

                if (styles.ContainsKey(name))
                {
                    throw Error(path, $"duplicate style name '{name}' (names are case-insensitive)");
                }

                if (document is null)
                {
                    throw Error(path, "must be an object, not null");
                }

                LogColor? foreground = ParseColor(document.Foreground, $"{path}.foreground");
                LogColor? background = ParseColor(document.Background, $"{path}.background");
                styles.Add(name, new StyleSpec(
                    foreground,
                    background,
                    document.Bold ?? false,
                    document.Italic ?? false,
                    document.Underline ?? false));
            }

            return styles;
        }

        private IReadOnlyList<string> BuildFrameworkPrefixes(List<string?>? documents)
        {
            if (documents is null)
            {
                return LogvuConfig.DefaultFrameworkPrefixes;
            }

            var prefixes = new List<string>(documents.Count);
            for (int i = 0; i < documents.Count; i++)
            {
                string path = $"frameworkPrefixes[{i}]";
                string? prefix = documents[i];
                if (prefix is null)
                {
                    throw Error(path, "must not be null");
                }

                if (string.IsNullOrEmpty(prefix))
                {
                    throw Error(path, "must not be empty: an empty prefix would match every frame");
                }

                prefixes.Add(prefix);
            }

            return prefixes;
        }

        private LogColor? ParseColor(string? text, string path)
        {
            if (text is null)
            {
                return null;
            }

            if (!LogColor.TryParse(text, out LogColor color))
            {
                throw Error(path, $"invalid color '{text}': expected one of the 16 ANSI names (black to brightWhite) or #rrggbb");
            }

            return color;
        }

        private Regex CompileExpression(string jsonPath, string expression, RegexOptions options)
        {
            try
            {
                return new Regex(expression, options | RegexOptions.CultureInvariant, LogvuConfig.MatchTimeout);
            }
            catch (ArgumentException ex)
            {
                throw Error(jsonPath, $"not a valid regular expression: {ex.Message}");
            }
        }

        private static bool IsAutomatic(string format) => string.Equals(format, "auto", StringComparison.OrdinalIgnoreCase);

        private static bool IsBuiltInStyle(string name) =>
            LogvuConfig.BuiltInStyles.Contains(name, StringComparer.OrdinalIgnoreCase);
    }
}
