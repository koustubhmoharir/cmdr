using System.Globalization;
using Cmdr.Core.Session;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tui;

/// <summary>
/// The details strip for the focused row. A command shows its description (up to three lines) and a pid/log line; a group
/// shows its mode, working directory, environment and description (see <see cref="DetailsText"/>). Reads the process state at draw time. UI thread only.
/// </summary>
internal sealed class DetailsView : View
{
    private ListRow? _row;
    private string _configDir = string.Empty;

    /// <summary>Shows the row. <paramref name="configDir"/> is where relative working directories resolve (for groups).</summary>
    public void Show(ListRow? row, string configDir)
    {
        _row = row;
        _configDir = configDir;
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        int width = Viewport.Width;
        int height = Viewport.Height;
        Attribute normal = GetAttributeForRole(VisualRole.Normal);

        IReadOnlyList<string> lines = LinesFor(_row, _configDir);
        for (int row = 0; row < height; row++)
        {
            string text = row < lines.Count ? lines[row] : string.Empty;
            Move(0, row);
            SetAttribute(normal);
            AddStr(TextWidth.Fit(text, width));
        }

        return true;
    }

    private static IReadOnlyList<string> LinesFor(ListRow? row, string configDir)
    {
        switch (row)
        {
            case CommandRow command:
            {
                CommandEntry entry = command.Entry;
                var lines = DetailsText.DescriptionLines(entry.Spec.Description);
                string pid = entry.Process.Snapshot.Pid?.ToString(CultureInfo.InvariantCulture) ?? "—";
                string log = entry.Process.Output.IsLogging ? entry.Process.Output.LogPath ?? "on" : "off";
                lines.Add("pid " + pid + " · log: " + log);
                return lines;
            }

            case GroupRow group:
                return DetailsText.GroupLines(group.Group, configDir);

            default:
                return Array.Empty<string>();
        }
    }
}
