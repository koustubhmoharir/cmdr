using System.Globalization;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;

namespace Cmdr.Tui;

/// <summary>The scroll position of the details strip: the top line, the visible height and the number of wrapped lines.</summary>
internal readonly record struct DetailsScroll(int Offset, int Height, int Total);

/// <summary>
/// The text of the details strip: its title, its styled lines and its scroll position. Pure: the process state and the
/// environment are passed in. The plain text of the lines is the copyable text; the styles only colour it.
/// </summary>
internal static class DetailsText
{
    /// <summary>The title: the full path of the focused item, e.g. "go-apps / nav / build". Empty without a row.</summary>
    public static string Title(ListRow? row) => row switch
    {
        CommandRow command => GroupTree.PathText(command.Entry, " / "),
        GroupRow group => GroupTree.PathText(group.Group, " / "),
        _ => string.Empty,
    };

    /// <summary>
    /// The frame title: <paramref name="path"/>, followed by the range shown when the content does not fit, e.g.
    /// "go-apps / nav / run  (1–4 of 11 ↓)". The arrows point at the content that is out of view: ↑ above, ↓ below, ↕ both.
    /// </summary>
    public static string FrameTitle(string path, DetailsScroll scroll)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (scroll.Height <= 0 || scroll.Total <= scroll.Height)
        {
            return path;
        }

        int first = scroll.Offset + 1;
        int last = Math.Min(scroll.Total, scroll.Offset + scroll.Height);
        bool above = scroll.Offset > 0;
        bool below = last < scroll.Total;
        string arrow = above && below ? "↕" : above ? "↑" : "↓";
        string range = $"({first}–{last} of {scroll.Total} {arrow})";
        return path.Length == 0 ? range : path + "  " + range;
    }

    /// <summary>
    /// The top line that keeps the content in view: clamped to zero and to the last page, so the last page is full whenever the
    /// content is longer than the height.
    /// </summary>
    public static int ClampOffset(int offset, int total, int height)
    {
        int max = Math.Max(0, total - Math.Max(0, height));
        return Math.Clamp(offset, 0, max);
    }

    /// <summary>
    /// Wraps each line to <paramref name="width"/> columns, keeping the styles: a span may continue on the next line. The plain
    /// text is wrapped exactly as <see cref="TextWidth.Wrap(string, int)"/> wraps it. Non-positive widths give no lines.
    /// </summary>
    public static IReadOnlyList<DetailsLine> Wrap(IReadOnlyList<DetailsLine> lines, int width) =>
        WrapRows(lines, width).Select(row => row.Line).ToList();

    /// <summary>
    /// The display rows of <see cref="Wrap"/>, each with the logical line it is from and the range of that line's text it shows,
    /// so that a selection made on the rows can be copied from the logical lines (see <see cref="DetailsSelection"/>).
    /// </summary>
    public static IReadOnlyList<DetailsRow> WrapRows(IReadOnlyList<DetailsLine> lines, int width)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var rows = new List<DetailsRow>();
        for (int i = 0; i < lines.Count; i++)
        {
            IReadOnlyList<(string Text, DetailsStyle Style)> spans = lines[i].Spans.Select(span => (span.Text, span.Style)).ToList();
            foreach (WrappedSpans<DetailsStyle> wrapped in TextWidth.WrapSpanRanges(spans, width))
            {
                var line = new DetailsLine(wrapped.Spans.Select(span => new DetailsSpan(span.Text, span.Style)).ToList());
                rows.Add(new DetailsRow(i, wrapped.Start, wrapped.End, line));
            }
        }

        return rows;
    }

    /// <summary>The plain text of each line, without styles.</summary>
    public static IReadOnlyList<string> Plain(IReadOnlyList<DetailsLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        return lines.Select(line => line.Text).ToList();
    }

    /// <summary>
    /// The lines of a command: the command line (prefixed with a prompt, which is not part of the copied text), the working
    /// directory, the environment cmdr sets for it, the pid and log line, and the whole description.
    /// </summary>
    /// <param name="environment">The process environment the values are applied to (see <see cref="EnvLines"/>).</param>
    /// <param name="windows">The platform: quoting of the command line, the prompt and the key comparison.</param>
    public static IReadOnlyList<DetailsLine> CommandLines(
        CommandEntry entry,
        string configDir,
        bool windows,
        IReadOnlyDictionary<string, string?> environment,
        char pathSeparator)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrEmpty(configDir);
        ArgumentNullException.ThrowIfNull(environment);

        CommandSpec spec = entry.Spec;
        IReadOnlyList<GroupSpec> ancestors = GroupTree.SpecChain(entry.Group);

        var lines = new List<DetailsLine>
        {
            Line(
                new DetailsSpan(windows ? "> " : "$ ", DetailsStyle.Prompt),
                new DetailsSpan(CommandLineFormatter.ForSpec(spec, powerShell: windows), DetailsStyle.Command)),
        };

        if (entry.IsReadOnly)
        {
            lines.Add(FromLine(entry.Source));
        }

        // A cwd that cannot be resolved (e.g. it contains a NUL) is shown, not thrown: the launch reports it on start.
        LaunchSpec? launch = null;
        try
        {
            launch = LaunchSpec.From(ancestors, spec, configDir);
        }
        catch (Exception ex) when (IsPathError(ex))
        {
            lines.Add(InvalidCwdLine(ex));
        }

        if (launch is not null)
        {
            lines.Add(Line(new DetailsSpan("cwd: ", DetailsStyle.Label), new DetailsSpan(launch.WorkingDirectory, DetailsStyle.Path)));
            lines.AddRange(EnvLines(launch.EnvLevels, launch.ConfigDir, environment, windows, pathSeparator));
        }

        // The default (pipe) is not shown: only the setting that changes how the command runs.
        if (spec.Stdin == StdinMode.Console)
        {
            lines.Add(Line(new DetailsSpan("stdin: ", DetailsStyle.Label), new DetailsSpan("console", DetailsStyle.Normal)));
        }

        // The readiness patterns, when the command declares them. A busy pattern is only listed with a ready pattern.
        if (spec.Ready is { } ready)
        {
            lines.Add(Line(new DetailsSpan("ready: ", DetailsStyle.Label), new DetailsSpan(ready.Pattern, DetailsStyle.Normal)));
            if (ready.Busy is not null)
            {
                lines.Add(Line(new DetailsSpan("busy: ", DetailsStyle.Label), new DetailsSpan(ready.Busy, DetailsStyle.Normal)));
            }
        }

        string pid = entry.Process.Snapshot.Pid?.ToString(CultureInfo.InvariantCulture) ?? "—";
        string log = entry.Process.Output.IsLogging ? entry.Process.Output.LogPath ?? "on" : "off";
        lines.Add(Line(new DetailsSpan("pid " + pid + " · log: " + log, DetailsStyle.Faint)));

        lines.AddRange(DescriptionLines(spec.Description));
        return lines;
    }

    /// <summary>
    /// The environment of a launch, as a preview: the keys that any level mentions, in first-mention order, each as
    /// <c>KEY=value</c> with the value expanded, or <c>KEY (unset)</c> when the variable is removed. The levels are applied to a
    /// copy of <paramref name="environment"/> exactly as the launch applies them. Empty when no level mentions a key.
    /// </summary>
    public static IReadOnlyList<DetailsLine> EnvLines(
        IReadOnlyList<EnvLevel> levels,
        string configDir,
        IReadOnlyDictionary<string, string?> environment,
        bool windows,
        char pathSeparator)
    {
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentException.ThrowIfNullOrEmpty(configDir);
        ArgumentNullException.ThrowIfNull(environment);

        StringComparer keys = windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        var mentioned = new List<string>();
        var seen = new HashSet<string>(keys);
        foreach (EnvLevel level in levels)
        {
            foreach (string key in level.Env.Keys)
            {
                if (seen.Add(key))
                {
                    mentioned.Add(key);
                }
            }
        }

        if (mentioned.Count == 0)
        {
            return Array.Empty<DetailsLine>();
        }

        var target = new Dictionary<string, string?>(keys);
        foreach (KeyValuePair<string, string?> entry in environment)
        {
            target[entry.Key] = entry.Value;
        }

        EnvExpansion.ApplyLevels(target, levels, configDir, pathSeparator);

        var lines = new List<DetailsLine>(mentioned.Count);
        foreach (string key in mentioned)
        {
            lines.Add(EnvLine(key, target.TryGetValue(key, out string? value) ? value : null));
        }

        return lines;
    }

    /// <summary>
    /// The lines of a group: its mode, its working directory (the setting and the resolved path, or only the resolved path
    /// when no setting is given), one line per environment entry, then the description.
    /// </summary>
    public static IReadOnlyList<DetailsLine> GroupLines(GroupEntry group, string configDir)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentException.ThrowIfNullOrEmpty(configDir);

        var lines = new List<DetailsLine>
        {
            Line(
                new DetailsSpan("mode: ", DetailsStyle.Label),
                new DetailsSpan(group.Mode == GroupMode.Sequential ? "sequential" : "parallel", DetailsStyle.Normal)),
        };

        if (group.IsReadOnly)
        {
            lines.Add(FromLine(group.Source));
        }

        string? resolved = null;
        try
        {
            resolved = ConfigPaths.ResolveGroupCwd(GroupTree.SpecChain(group), configDir);
        }
        catch (Exception ex) when (IsPathError(ex))
        {
            lines.Add(InvalidCwdLine(ex));
        }

        if (resolved is not null)
        {
            // The setting is shown only when it says something the resolved path does not (a relative or unnormalised path).
            lines.Add(string.IsNullOrWhiteSpace(group.Cwd) || group.Cwd == resolved
                ? Line(new DetailsSpan("cwd: ", DetailsStyle.Label), new DetailsSpan(resolved, DetailsStyle.Path))
                : Line(
                    new DetailsSpan("cwd: ", DetailsStyle.Label),
                    new DetailsSpan(group.Cwd, DetailsStyle.Path),
                    new DetailsSpan(" → ", DetailsStyle.Faint),
                    new DetailsSpan(resolved, DetailsStyle.Path)));
        }

        if (group.Env is not null)
        {
            foreach (KeyValuePair<string, string?> entry in group.Env)
            {
                lines.Add(EnvLine(entry.Key, entry.Value));
            }
        }

        lines.AddRange(DescriptionLines(group.Description));
        return lines;
    }

    /// <summary>
    /// The directory that the relative paths of a row resolve against: the directory of the file the item comes from, so that a
    /// base item uses its own directory. <paramref name="fallback"/> when there is no row.
    /// </summary>
    public static string ConfigDirFor(ListRow? row, string fallback)
    {
        ArgumentException.ThrowIfNullOrEmpty(fallback);

        return row switch
        {
            CommandRow command => command.Entry.Source.Dir,
            GroupRow group => group.Group.Source.Dir,
            _ => fallback,
        };
    }

    /// <summary>The source line of a read-only item: the file it comes from, marked read-only.</summary>
    private static DetailsLine FromLine(ConfigSource source) => Line(
        new DetailsSpan("from: ", DetailsStyle.Label),
        new DetailsSpan(source.DisplayPath, DetailsStyle.Path),
        new DetailsSpan(" (read-only)", DetailsStyle.Faint));

    private static bool IsPathError(Exception ex) => ex is ArgumentException or NotSupportedException or PathTooLongException;

    private static DetailsLine InvalidCwdLine(Exception ex) =>
        Line(new DetailsSpan("cwd: ", DetailsStyle.Label), new DetailsSpan("invalid (" + ex.Message + ")", DetailsStyle.Unset));

    /// <summary>The lines of a description, with line breaks normalised. Empty for no description.</summary>
    public static List<DetailsLine> DescriptionLines(string? description)
    {
        var lines = new List<DetailsLine>();
        if (string.IsNullOrEmpty(description))
        {
            return lines;
        }

        string text = description.Replace("\r\n", "\n").TrimEnd('\n');
        if (text.Length == 0)
        {
            return lines;
        }

        lines.AddRange(text.Split('\n').Select(part => Line(new DetailsSpan(part, DetailsStyle.Normal))));
        return lines;
    }

    /// <summary>One environment entry: <c>KEY=value</c>, or <c>KEY (unset)</c> when the value is null.</summary>
    private static DetailsLine EnvLine(string key, string? value) => value is null
        ? Line(new DetailsSpan(key, DetailsStyle.EnvName), new DetailsSpan(" (unset)", DetailsStyle.Unset))
        : Line(
            new DetailsSpan(key, DetailsStyle.EnvName),
            new DetailsSpan("=", DetailsStyle.Faint),
            new DetailsSpan(value, DetailsStyle.EnvValue));

    /// <summary>A line of the given spans. Empty spans are dropped, so a line never holds an empty run.</summary>
    private static DetailsLine Line(params DetailsSpan[] spans) =>
        new(spans.Where(span => span.Text.Length > 0).ToList());
}
