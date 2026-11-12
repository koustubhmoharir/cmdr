using Cmdr.Core.Config;
using Cmdr.Core.Session;

namespace Cmdr.Tui;

/// <summary>The text of the details strip: its title and the group's lines. Pure; reads the group's settings only.</summary>
internal static class DetailsText
{
    /// <summary>The most description lines shown.</summary>
    public const int MaxDescriptionLines = 3;

    /// <summary>The title: the full path of the focused item, e.g. "go-apps / nav / build". Empty without a row.</summary>
    public static string Title(ListRow? row) => row switch
    {
        CommandRow command => GroupTree.PathText(command.Entry, " / "),
        GroupRow group => GroupTree.PathText(group.Group, " / "),
        _ => string.Empty,
    };

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
        lines.Add(string.IsNullOrWhiteSpace(group.Cwd)
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

    /// <summary>The first lines of a description, with line breaks normalised. Empty for no description.</summary>
    public static List<string> DescriptionLines(string? description)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(description))
        {
            return lines;
        }

        foreach (string line in description.Replace("\r\n", "\n").Split('\n').Take(MaxDescriptionLines))
        {
            lines.Add(line);
        }

        return lines;
    }
}
