using System.Text.Json;
using System.Text.RegularExpressions;
using Logvu.Core.Parsing;
using Shared.Core;

namespace Logvu.Core.Config;

/// <summary>Reads, parses and validates a logvu configuration file. See <see cref="LogvuConfig.Load"/>.</summary>
/// <remarks>
/// Validation runs once over the members in the order the configuration documents them, and reports the first error. A format or
/// highlight style that names a scheme or style defined further down is checked against the names alone, so that the error is
/// reported where the reference is.
/// </remarks>
internal static class LogvuConfigLoader
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>Loads and validates the file at <paramref name="configPath"/>.</summary>
    internal static LogvuConfig Load(string configPath)
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
        return new Builder(fullPath).Build(document);
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
    private sealed class Builder(string configPath)
    {
        private readonly string _configPath = configPath;

        internal LogvuConfig Build(LogvuConfigDocument document)
        {
            string configDirectory = Path.GetDirectoryName(_configPath)!;

            string logsDirectory = ResolveDirectory(RequireText(document.LogsDirectory, "logsDirectory"), configDirectory);

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

            return new LogvuConfig(
                _configPath,
                logsDirectory,
                filePrefix,
                fileNamePattern,
                activeFile,
                format,
                schemes,
                timeZone,
                recentCapacity,
                highlights,
                styles,
                frameworkPrefixes);
        }

        private LogvuConfigException Error(string jsonPath, string problem) =>
            new($"Configuration file '{_configPath}': {jsonPath}: {problem}");

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

        private string ResolveDirectory(string text, string configDirectory)
        {
            try
            {
                return Path.GetFullPath(Path.Combine(configDirectory, text));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw Error("logsDirectory", $"not a valid directory path: {ex.Message}");
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
