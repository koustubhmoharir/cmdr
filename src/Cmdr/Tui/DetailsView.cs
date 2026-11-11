using System.Globalization;
using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tui;

/// <summary>
/// The details strip for the focused row. A command shows its description (up to three lines) and a pid/log line; a group
/// shows its mode and description. Reads the process state at draw time. UI thread only.
/// </summary>
internal sealed class DetailsView : View
{
    private const int MaxDescriptionLines = 3;

    private ListRow? _row;

    public void Show(ListRow? row)
    {
        _row = row;
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        int width = Viewport.Width;
        int height = Viewport.Height;
        Attribute normal = GetAttributeForRole(VisualRole.Normal);

        IReadOnlyList<string> lines = LinesFor(_row);
        for (int row = 0; row < height; row++)
        {
            string text = row < lines.Count ? lines[row] : string.Empty;
            Move(0, row);
            SetAttribute(normal);
            AddStr(TextWidth.Fit(text, width));
        }

        return true;
    }

    private static IReadOnlyList<string> LinesFor(ListRow? row)
    {
        switch (row)
        {
            case CommandRow command:
            {
                CommandEntry entry = command.Entry;
                var lines = DescriptionLinesOf(entry.Spec.Description);
                string pid = entry.Process.Snapshot.Pid?.ToString(CultureInfo.InvariantCulture) ?? "—";
                string log = entry.Process.Output.IsLogging ? entry.Process.Output.LogPath ?? "on" : "off";
                lines.Add("pid " + pid + " · log: " + log);
                return lines;
            }

            case GroupRow group:
            {
                var lines = new List<string> { "mode: " + (group.Group.Mode == GroupMode.Sequential ? "sequential" : "parallel") };
                lines.AddRange(DescriptionLinesOf(group.Group.Description));
                return lines;
            }

            default:
                return Array.Empty<string>();
        }
    }

    private static List<string> DescriptionLinesOf(string? description)
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
