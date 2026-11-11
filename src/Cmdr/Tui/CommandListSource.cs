using System.Collections;
using System.Collections.Specialized;
using Cmdr.Core.Session;
using Terminal.Gui.Drawing;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tui;

/// <summary>
/// Renders the rows of the command list. Marks come from <see cref="Selection"/>, not from ListView, which never draws marks.
/// Status comes from <see cref="StatusFormatter"/> at draw time. UI thread only.
/// </summary>
internal sealed class CommandListSource : IListDataSource
{
    private static readonly Color GlyphWarning = new(0xE5, 0xA5, 0x0A);

    private IReadOnlyList<ListRow> _rows = Array.Empty<ListRow>();

    public CommandListSource(Selection selection)
    {
        Selection = selection;
    }

    /// <summary>The marks shown on command rows.</summary>
    public Selection Selection { get; }

    /// <summary>The rows shown. Setting them redraws the whole list.</summary>
    public IReadOnlyList<ListRow> Rows
    {
        get => _rows;
        set
        {
            _rows = value;
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    public int Count => _rows.Count;

    public int MaxItemLength => 80;

    public bool SuspendCollectionChangedEvent { get; set; }

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    // Marks are drawn by Render; ListView's own marking stays unused.
    public bool IsMarked(int item) => false;

    public void SetMark(int item, bool value)
    {
    }

    public bool RenderMark(ListView listView, int item, int row, bool isMarked, bool markMultiple) => false;

    public IList ToList() => _rows.ToList();

    public void Render(ListView listView, bool selected, int item, int col, int row, int width, int viewportX)
    {
        Attribute baseAttr = listView.GetAttributeForRole(selected ? VisualRole.Focus : VisualRole.Normal);
        listView.Move(col, row);
        listView.SetAttribute(baseAttr);

        switch (_rows[item])
        {
            case GroupRow group:
                listView.SetAttribute(new Attribute(baseAttr.Foreground, baseAttr.Background, TextStyle.Bold));
                listView.AddStr(RowLayout.FormatGroupRow(width, group.Group.Name, group.Group.Mode, group.Group.IsSequenceRunning));
                break;

            case CommandRow command:
                CommandEntry entry = command.Entry;
                StatusText status = StatusFormatter.Format(entry.Process.Snapshot, entry.IsQueued, DateTimeOffset.Now);
                CommandRowText text = RowLayout.FormatCommandRow(width, Selection.IsMarked(entry), status, entry.Spec.Name);

                listView.AddStr(text.Text);
                if (text.GlyphColumn >= 0)
                {
                    Color glyphColor = GlyphColor(status.Kind, baseAttr.Foreground);
                    TextStyle style = status.Kind == StatusKind.Success ? TextStyle.Faint : TextStyle.None;
                    listView.Move(col + text.GlyphColumn, row);
                    listView.SetAttribute(new Attribute(glyphColor, baseAttr.Background, style));
                    listView.AddStr(status.Glyph);
                }

                break;
        }
    }

    public void Dispose()
    {
    }

    private static Color GlyphColor(StatusKind kind, Color normal) => kind switch
    {
        StatusKind.Running => ColorName16.Green,
        StatusKind.Stopping => ColorName16.Yellow,
        StatusKind.Success => ColorName16.Green,
        StatusKind.Failure => ColorName16.Red,
        StatusKind.Warning => GlyphWarning,
        StatusKind.Queued => ColorName16.Cyan,
        _ => normal,
    };
}
