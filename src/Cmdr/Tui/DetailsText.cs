using System.Globalization;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Shared.Processes;
using Shared.Tui;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Tui;

/// <summary>The scroll position of the details strip: the top line, the visible height and the number of wrapped lines.</summary>
internal readonly record struct DetailsScroll(int Offset, int Height, int Total);

/// <summary>
/// The text of the details strip: its title, its styled lines and its scroll position. Pure: the process state and the
/// environment are passed in. The plain text of the lines is the copyable text; the styles only colour it.
/// </summary>
internal static class DetailsText
{
    /// <summary>
    /// The title: the full path of the focused item, e.g. "go-apps / nav / build", or the display path of a file row. Empty without a row.
    /// </summary>
    public static string Title(ListRow? row) => row switch
    {
        CommandRow command => GroupTree.PathText(command.Entry, " / "),
        GroupRow group => GroupTree.PathText(group.Group, " / "),
        FileRow file => file.Source.DisplayPath,
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
    /// so that a selection made on the rows can be copied from the logical lines (see <see cref="TextSelection"/>).
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

    /// <summary>The logical text of each line: its cleaned plain text, the same as the displayed text before wrapping.</summary>
    public static IReadOnlyList<string> LogicalTexts(IReadOnlyList<DetailsLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        return lines.Select(line => TextWidth.Clean(line.Text)).ToList();
    }

    /// <summary>The rows as the selection maps them (see <see cref="TextSelection"/>): the logical line and range of each row, drawn from column 0.</summary>
    public static IReadOnlyList<SelectionRow> SelectionRows(IReadOnlyList<DetailsRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return rows.Select(row => new SelectionRow(row.LogicalLine, row.Start, row.End)).ToList();
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
    /// <param name="known">
    /// The file levels and group values the launch is built from (see <see cref="CmdrSession.KnownLaunchEnvironment"/>). Null uses
    /// the file's levels without any environment script's values.
    /// </param>
    /// <param name="notRunLabels">The labels of the environment scripts of the command that have no successful run, one line each. Null for none.</param>
    /// <param name="secret">The secret names that apply to the command (see <see cref="CmdrSession.SecretNamesFor(ItemEntry)"/>): their values are masked. Null for none.</param>
    /// <remarks>A cwd with a variable that is not set is shown as written, with a note that it is resolved at start (see <see cref="UnresolvedCwdLine"/>).</remarks>
    public static IReadOnlyList<DetailsLine> CommandLines(
        CommandEntry entry,
        string configDir,
        bool windows,
        IReadOnlyDictionary<string, string?> environment,
        char pathSeparator,
        (IReadOnlyList<EnvLevel> FileLevels, IReadOnlyList<IReadOnlyDictionary<string, string?>?> GroupValues)? known = null,
        IReadOnlyList<string>? notRunLabels = null,
        IReadOnlySet<string>? secret = null)
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
                new DetailsSpan(CommandLineFormatter.ForEntry(entry, powerShell: windows), DetailsStyle.Command)),
        };

        if (entry.IsReadOnly)
        {
            lines.Add(FromLine(entry.Source));
        }

        // A cwd that cannot be resolved (e.g. it contains a NUL) is shown, not thrown: the launch reports it on start.
        LaunchSpec? launch = null;
        try
        {
            launch = known is { } values
                ? CommandLaunch.From(ancestors, spec, configDir, values.FileLevels, values.GroupValues)
                : CommandLaunch.From(ancestors, spec, configDir, entry.Source.EnvLevels);
        }
        catch (InvalidOperationException)
        {
            // A variable the cwd uses is not set yet, perhaps because the environment script that sets it has not run.
            lines.Add(UnresolvedCwdLine(spec.Cwd));
        }
        catch (Exception ex) when (IsPathError(ex))
        {
            lines.Add(InvalidCwdLine(ex));
        }

        if (launch is not null)
        {
            lines.Add(Line(new DetailsSpan("cwd: ", DetailsStyle.Label), new DetailsSpan(launch.WorkingDirectory, DetailsStyle.Path)));
            lines.AddRange(EnvLines(launch.EnvLevels, launch.ConfigDir, environment, windows, pathSeparator, secret));
        }

        foreach (string label in notRunLabels ?? Array.Empty<string>())
        {
            lines.Add(Line(new DetailsSpan($"env script of {label} has no successful run yet; its variables are not shown", DetailsStyle.Faint)));
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
    /// copy of <paramref name="environment"/> exactly as the launch applies them. Empty when no level mentions a key. A secret key's
    /// value is masked (see <see cref="EnvLine"/>).
    /// </summary>
    /// <param name="secret">The secret names whose values are masked. Null for none.</param>
    public static IReadOnlyList<DetailsLine> EnvLines(
        IReadOnlyList<EnvLevel> levels,
        string configDir,
        IReadOnlyDictionary<string, string?> environment,
        bool windows,
        char pathSeparator,
        IReadOnlySet<string>? secret = null)
    {
        IReadOnlyList<KeyValuePair<string, string?>> effective = EffectiveEnv.Of(levels, configDir, environment, windows, pathSeparator);
        return effective.Select(entry => EnvLine(entry.Key, entry.Value, secret)).ToList();
    }

    /// <summary>
    /// The lines of a group: its mode, its working directory (the setting and the resolved path, or only the resolved path
    /// when no setting is given), one line per environment entry, its environment script when it has one (see
    /// <see cref="EnvScriptLines"/>), then the description.
    /// </summary>
    /// <param name="lastRun">The last run of the group's script (see <see cref="CmdrSession.LastEnvScriptRun"/>), or null when it has not run.</param>
    /// <param name="secret">The secret names of the group (see <see cref="CmdrSession.SecretNamesFor(ItemEntry)"/>): their values are masked. Null for none.</param>
    /// <param name="known">
    /// The file levels and group values the directory is resolved from (see <see cref="CmdrSession.KnownGroupEnvironment"/>). Null uses the
    /// file's levels without any environment script's values.
    /// </param>
    public static IReadOnlyList<DetailsLine> GroupLines(
        GroupEntry group,
        string configDir,
        (DateTimeOffset FinishedAt, EnvScriptResult Result)? lastRun = null,
        IReadOnlySet<string>? secret = null,
        (IReadOnlyList<EnvLevel> FileLevels, IReadOnlyList<IReadOnlyDictionary<string, string?>?> GroupValues)? known = null)
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
            resolved = known is { } values
                ? ConfigPaths.ResolveGroupCwd(GroupTree.SpecChain(group), configDir, values.FileLevels, values.GroupValues)
                : ConfigPaths.ResolveGroupCwd(GroupTree.SpecChain(group), configDir, group.Source.EnvLevels);
        }
        catch (InvalidOperationException)
        {
            // A variable the cwd uses is not known yet, perhaps because an environment script that sets it has not run.
            lines.Add(UnresolvedCwdLine(group.Cwd));
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
                lines.Add(EnvLine(entry.Key, entry.Value, secret));
            }
        }

        if (group.EnvScript is { } envScript)
        {
            lines.AddRange(EnvScriptLines(envScript, lastRun, secret));
        }

        lines.AddRange(DescriptionLines(group.Description));
        return lines;
    }

    /// <summary>
    /// The lines of a file row: its path, its <c>base</c> setting as written (when it imports one), its <c>bufferLines</c> and
    /// resolved <c>logsDir</c>, whether it is read-only, its <c>baseEnv</c>, <c>envScript</c> and <c>secret</c> when set, then the
    /// environment its own level adds (see <see cref="EnvLines"/>).
    /// </summary>
    /// <param name="environment">The process environment the values are applied to (see <see cref="EnvLines"/>).</param>
    /// <param name="windows">The platform, as for <see cref="EnvLines"/>.</param>
    /// <param name="pathSeparator">The path separator of the platform, as for <see cref="EnvLines"/>.</param>
    /// <param name="lastRun">The last run of the file's script (see <see cref="EnvScriptLines"/>), or null when it has not run.</param>
    /// <param name="secret">The secret names of the file (see <see cref="CmdrSession.SecretNamesFor(ConfigSource)"/>): their values are masked. Null for none.</param>
    public static IReadOnlyList<DetailsLine> FileLines(
        ConfigSource source,
        IReadOnlyDictionary<string, string?> environment,
        bool windows,
        char pathSeparator,
        (DateTimeOffset FinishedAt, EnvScriptResult Result)? lastRun = null,
        IReadOnlySet<string>? secret = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(environment);

        var lines = new List<DetailsLine>
        {
            Line(new DetailsSpan("file: ", DetailsStyle.Label), new DetailsSpan(source.Path, DetailsStyle.Path)),
        };

        if (source.BaseSetting is { } baseSetting && !string.IsNullOrWhiteSpace(baseSetting))
        {
            lines.Add(Line(new DetailsSpan("base: ", DetailsStyle.Label), new DetailsSpan(baseSetting, DetailsStyle.Path)));
        }

        lines.Add(Line(
            new DetailsSpan("bufferLines: ", DetailsStyle.Label),
            new DetailsSpan(source.BufferLines.ToString(CultureInfo.InvariantCulture), DetailsStyle.Normal)));
        lines.Add(Line(new DetailsSpan("logsDir: ", DetailsStyle.Label), new DetailsSpan(source.LogsDir, DetailsStyle.Path)));

        if (source.IsBase)
        {
            lines.Add(Line(new DetailsSpan("read-only (base file)", DetailsStyle.Faint)));
        }

        if (source.BaseEnv is { Count: > 0 } baseEnv)
        {
            lines.Add(Line(new DetailsSpan("baseEnv: ", DetailsStyle.Label), new DetailsSpan(string.Join(", ", baseEnv), DetailsStyle.Normal)));
        }

        if (source.EnvScript is { } envScript)
        {
            lines.AddRange(EnvScriptLines(envScript, lastRun, secret));
        }

        if (source.Secret is { Count: > 0 } secretNames)
        {
            lines.Add(Line(new DetailsSpan("secret: ", DetailsStyle.Label), new DetailsSpan(string.Join(", ", secretNames), DetailsStyle.Normal)));
        }

        lines.AddRange(EnvLines(source.EnvLevels, source.Dir, environment, windows, pathSeparator, secret));
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
            FileRow file => file.Source.Dir,
            _ => fallback,
        };
    }

    /// <summary>The source line of a read-only item: the file it comes from, marked read-only.</summary>
    private static DetailsLine FromLine(ConfigSource source) => Line(
        new DetailsSpan("from: ", DetailsStyle.Label),
        new DetailsSpan(source.DisplayPath, DetailsStyle.Path),
        new DetailsSpan(" (read-only)", DetailsStyle.Faint));

    /// <summary>
    /// The exceptions that resolving a configured working directory throws for a path it cannot use, or for a variable it uses that is not set.
    /// The copy falls back on the same ones. A cwd that resolves only once an environment script has run is not an error to show, see <see cref="UnresolvedCwdLine"/>.
    /// </summary>
    internal static bool IsPathError(Exception ex) => ex is ArgumentException or NotSupportedException or PathTooLongException or InvalidOperationException;

    private static DetailsLine InvalidCwdLine(Exception ex) =>
        Line(new DetailsSpan("cwd: ", DetailsStyle.Label), new DetailsSpan("invalid (" + ex.Message + ")", DetailsStyle.Unset));

    /// <summary>
    /// The cwd line of a directory that cannot be resolved yet, because a variable it uses is not set (its environment script may not have
    /// run): the setting as written, with a note that it is resolved at start. For an inherited directory, only the note.
    /// </summary>
    internal static DetailsLine UnresolvedCwdLine(string? written) =>
        string.IsNullOrWhiteSpace(written)
            ? Line(new DetailsSpan("cwd: ", DetailsStyle.Label), new DetailsSpan("(inherited, resolved at start)", DetailsStyle.Faint))
            : Line(
                new DetailsSpan("cwd: ", DetailsStyle.Label),
                new DetailsSpan(written, DetailsStyle.Path),
                new DetailsSpan(" (resolved at start)", DetailsStyle.Faint));

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

    /// <summary>
    /// The lines of an environment script: its command as written, then the status of its last run: when it ran, the time it finished
    /// and how long it took, and how many variables it produced, followed by one <c>KEY=value</c> line per variable in its output order
    /// (<c>KEY (unset)</c> for a removal); when it failed, the time and the error; or <c>not run yet</c> when it has not run. A secret
    /// variable's value is masked.
    /// </summary>
    private static List<DetailsLine> EnvScriptLines(
        EnvScriptSpec script,
        (DateTimeOffset FinishedAt, EnvScriptResult Result)? lastRun,
        IReadOnlySet<string>? secret)
    {
        string command = string.Join(" ", new[] { script.Program }.Concat(script.Args));
        var lines = new List<DetailsLine>
        {
            Line(new DetailsSpan("envScript: ", DetailsStyle.Label), new DetailsSpan(command, DetailsStyle.Normal)),
        };

        if (lastRun is not { } run)
        {
            lines.Add(Line(new DetailsSpan("not run yet", DetailsStyle.Faint)));
            return lines;
        }

        string time = run.FinishedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        if (!run.Result.Succeeded)
        {
            string error = run.Result.Error ?? "the script failed";
            lines.Add(Line(new DetailsSpan($"last run {time} failed: {error}", DetailsStyle.Unset)));
            return lines;
        }

        IReadOnlyDictionary<string, string?> values = run.Result.Values!;
        string seconds = run.Result.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        lines.Add(Line(new DetailsSpan($"last run {time} ({seconds} s): {values.Count} {(values.Count == 1 ? "variable" : "variables")}", DetailsStyle.Normal)));
        foreach (KeyValuePair<string, string?> entry in values)
        {
            lines.Add(EnvLine(entry.Key, entry.Value, secret));
        }

        return lines;
    }

    /// <summary>The text that stands for the value of a secret variable. The value itself is never shown.</summary>
    internal const string MaskedValue = "••••";

    /// <summary>
    /// One environment entry: <c>KEY=value</c>, or <c>KEY (unset)</c> when the value is null. When <paramref name="key"/> is secret, the
    /// value is <see cref="MaskedValue"/> instead; a removal is shown as before, since it has no value.
    /// </summary>
    private static DetailsLine EnvLine(string key, string? value, IReadOnlySet<string>? secret = null) => value is null
        ? Line(new DetailsSpan(key, DetailsStyle.EnvName), new DetailsSpan(" (unset)", DetailsStyle.Unset))
        : Line(
            new DetailsSpan(key, DetailsStyle.EnvName),
            new DetailsSpan("=", DetailsStyle.Faint),
            new DetailsSpan(secret is not null && secret.Contains(key) ? MaskedValue : value, DetailsStyle.EnvValue));

    /// <summary>A line of the given spans. Empty spans are dropped, so a line never holds an empty run.</summary>
    private static DetailsLine Line(params DetailsSpan[] spans) =>
        new(spans.Where(span => span.Text.Length > 0).ToList());
}
