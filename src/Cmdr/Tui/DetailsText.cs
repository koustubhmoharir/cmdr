using System.Globalization;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;

namespace Cmdr.Tui;

/// <summary>The scroll position of the details strip: the top line, the visible height and the number of wrapped lines.</summary>
internal readonly record struct DetailsScroll(int Offset, int Height, int Total);

/// <summary>
/// The text of the details strip: its title, its lines and its scroll position. Pure: the process state and the environment
/// are passed in.
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

    /// <summary>Wraps each line to <paramref name="width"/> columns. Non-positive widths give no lines.</summary>
    public static IReadOnlyList<string> Wrap(IReadOnlyList<string> lines, int width)
    {
        ArgumentNullException.ThrowIfNull(lines);

        return lines.SelectMany(line => TextWidth.Wrap(line, width)).ToList();
    }

    /// <summary>
    /// The lines of a command: the command line (prefixed with a prompt, which is not part of the copied text), the working
    /// directory, the environment cmdr sets for it, the pid and log line, and the whole description.
    /// </summary>
    /// <param name="environment">The process environment the values are applied to (see <see cref="EnvLines"/>).</param>
    /// <param name="windows">The platform: quoting of the command line, the prompt and the key comparison.</param>
    public static IReadOnlyList<string> CommandLines(
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
        LaunchSpec launch = LaunchSpec.From(ancestors, spec, configDir);

        var lines = new List<string>
        {
            (windows ? "> " : "$ ") + CommandLineFormatter.ForSpec(spec, windows),
            "cwd: " + ConfigPaths.ResolveCwd(ancestors, spec, configDir),
        };

        lines.AddRange(EnvLines(launch.EnvLevels, launch.ConfigDir, environment, windows, pathSeparator));

        string pid = entry.Process.Snapshot.Pid?.ToString(CultureInfo.InvariantCulture) ?? "—";
        string log = entry.Process.Output.IsLogging ? entry.Process.Output.LogPath ?? "on" : "off";
        lines.Add("pid " + pid + " · log: " + log);

        lines.AddRange(DescriptionLines(spec.Description));
        return lines;
    }

    /// <summary>
    /// The environment of a launch, as a preview: the keys that any level mentions, in first-mention order, each as
    /// <c>KEY=value</c> with the value expanded, or <c>KEY (unset)</c> when the variable is removed. The levels are applied to a
    /// copy of <paramref name="environment"/> exactly as the launch applies them. Empty when no level mentions a key.
    /// </summary>
    public static IReadOnlyList<string> EnvLines(
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
            return Array.Empty<string>();
        }

        var target = new Dictionary<string, string?>(keys);
        foreach (KeyValuePair<string, string?> entry in environment)
        {
            target[entry.Key] = entry.Value;
        }

        EnvExpansion.ApplyLevels(target, levels, configDir, pathSeparator);

        var lines = new List<string>(mentioned.Count);
        foreach (string key in mentioned)
        {
            lines.Add(target.TryGetValue(key, out string? value) && value is not null
                ? key + "=" + value
                : key + " (unset)");
        }

        return lines;
    }

    /// <summary>
    /// The lines of a group: its mode, its working directory (the setting and the resolved path, or only the resolved path
    /// when no setting is given), one line per environment entry, then the description.
    /// </summary>
    public static IReadOnlyList<string> GroupLines(GroupEntry group, string configDir)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentException.ThrowIfNullOrEmpty(configDir);

        var lines = new List<string>
        {
            "mode: " + (group.Mode == GroupMode.Sequential ? "sequential" : "parallel"),
        };

        string resolved = ConfigPaths.ResolveGroupCwd(GroupTree.SpecChain(group), configDir);
        // The setting is shown only when it says something the resolved path does not (a relative or unnormalised path).
        lines.Add(string.IsNullOrWhiteSpace(group.Cwd) || group.Cwd == resolved
            ? "cwd: " + resolved
            : "cwd: " + group.Cwd + " → " + resolved);

        if (group.Env is not null)
        {
            foreach (KeyValuePair<string, string?> entry in group.Env)
            {
                lines.Add(entry.Value is null
                    ? entry.Key + " (unset)"
                    : entry.Key + "=" + entry.Value);
            }
        }

        lines.AddRange(DescriptionLines(group.Description));
        return lines;
    }

    /// <summary>The lines of a description, with line breaks normalised. Empty for no description.</summary>
    public static List<string> DescriptionLines(string? description)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(description))
        {
            return lines;
        }

        string text = description.Replace("\r\n", "\n").TrimEnd('\n');
        if (text.Length == 0)
        {
            return lines;
        }

        lines.AddRange(text.Split('\n'));
        return lines;
    }
}
