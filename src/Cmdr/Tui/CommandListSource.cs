using System.Collections;
using System.Collections.Specialized;
using Cmdr.Core.Session;
using Terminal.Gui.Drawing;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tui;

/// <summary>
/// Renders the command list. Items of the ListView are display lines (see <see cref="DisplayLines"/>): a group header is one
/// line, a command is a main line and a detail line. Marks come from <see cref="Selection"/>, not from ListView, which never
/// draws marks. Status comes from <see cref="StatusFormatter"/> at draw time. UI thread only.
/// </summary>
internal sealed class CommandListSource : IListDataSource
{
    /// <summary>Columns from the indent of a group header to its name: the "▾ " or "▸ " before it.</summary>
    private const int HeaderNameOffset = 2;

    private readonly IReadOnlySet<GroupEntry> _collapsed;
    private IReadOnlyList<ListRow> _rows = Array.Empty<ListRow>();
    private DisplayLines _lines = DisplayLines.Build(Array.Empty<ListRow>());

    /// <param name="selection">The marks shown on command rows.</param>
    /// <param name="collapsed">The groups whose headers show a summary. Owned by the caller; read at draw time.</param>
    public CommandListSource(Selection selection, IReadOnlySet<GroupEntry> collapsed)
    {
        Selection = selection;
        _collapsed = collapsed;
    }

    /// <summary>The marks shown on command rows.</summary>
    public Selection Selection { get; }

    /// <summary>The search whose matches are highlighted in group and command names. Null when there is no search.</summary>
    public SearchPattern? Search { get; set; }

    /// <summary>
    /// The active match, drawn in its own color: its item (compared by reference), and its range in the item's name. Null when there
    /// is none.
    /// </summary>
    public (ItemEntry Item, int Start, int Length)? ActiveMatch { get; set; }

    /// <summary>The display lines of the rows shown. Changes with <see cref="Rows"/>.</summary>
    public DisplayLines Lines => _lines;

    /// <summary>The rows shown. Setting them rebuilds the display lines and redraws the whole list.</summary>
    public IReadOnlyList<ListRow> Rows
    {
        get => _rows;
        set
        {
            _rows = value;
            _lines = DisplayLines.Build(value);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    public int Count => _lines.Count;

    public int MaxItemLength => 80;

    public bool SuspendCollectionChangedEvent { get; set; }

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    // Marks are drawn by Render; ListView's own marking stays unused.
    public bool IsMarked(int item) => false;

    public void SetMark(int item, bool value)
    {
    }

    public bool RenderMark(ListView listView, int item, int row, bool isMarked, bool markMultiple) => false;

    public IList ToList() => _lines.Lines.Select(line => (object)_rows[line.RowIndex]).ToList();

    public void Render(ListView listView, bool selected, int item, int col, int row, int width, int viewportX)
    {
        if (item < 0 || item >= _lines.Count)
        {
            return;
        }

        // ListView remembers the attribute it last set and fills the rows below the last item with it without setting it
        // again, so the attribute it set before this call is restored at the end. Otherwise the focused command's detail
        // line, drawn last, would leave the focus colors for the empty rows below.
        Attribute entryAttr = listView.GetCurrentAttribute();
        try
        {
            RenderLine(listView, selected, item, col, row, width);
        }
        finally
        {
            listView.SetAttribute(entryAttr);
        }
    }

    private void RenderLine(ListView listView, bool selected, int item, int col, int row, int width)
    {
        DisplayLine line = _lines.Lines[item];

        // Both lines of the focused command are drawn as focused.
        bool focused = selected || line.RowIndex == _lines.LineToRow(listView.SelectedItem ?? -1);
        Attribute baseAttr = listView.GetAttributeForRole(focused ? VisualRole.Focus : VisualRole.Normal);
        listView.Move(col, row);
        listView.SetAttribute(baseAttr);

        switch (_rows[line.RowIndex])
        {
            case GroupRow group:
                RenderGroup(listView, baseAttr, group.Group, col, row, width);
                break;

            case CommandRow command when line.IsDetail:
                StatusText detailStatus = StatusOf(command.Entry);
                listView.SetAttribute(new Attribute(baseAttr.Foreground, baseAttr.Background, TextStyle.Faint));
                listView.AddStr(RowLayout.FormatCommandDetail(width, RowLayout.CommandDetailIndent(command.Entry.Group.Depth), detailStatus));
                break;

            case CommandRow command:
                CommandEntry entry = command.Entry;
                StatusText status = StatusOf(entry);
                CommandMainText text = RowLayout.FormatCommandMain(width, Selection.IsMarked(entry), status.Glyph, entry.Spec.Name, entry.Group.Depth);

                listView.AddStr(text.Text);
                if (text.GlyphColumn >= 0)
                {
                    Color glyphColor = StatusColors.For(status.Kind, baseAttr.Background, baseAttr.Foreground);
                    listView.Move(col + text.GlyphColumn, row);
                    listView.SetAttribute(new Attribute(glyphColor, baseAttr.Background, TextStyle.None));
                    listView.AddStr(status.Glyph);
                }

                if (text.NameColumn >= 0)
                {
                    // The name is cut to the width after its column, so that is the room the matches are drawn in.
                    DrawMatches(listView, baseAttr, entry, entry.Name, col + text.NameColumn, row, width - text.NameColumn);
                }

                break;
        }
    }

    public void Dispose()
    {
    }

    private void RenderGroup(ListView listView, Attribute baseAttr, GroupEntry group, int col, int row, int width)
    {
        bool collapsed = _collapsed.Contains(group);
        // A collapsed header summarises every command below it, not only the direct ones.
        string summary = collapsed
            ? GroupSummary.Compose(group.AllCommands.Select(command => StatusOf(command).Kind))
            : string.Empty;

        // A top-level group of a base file names that file, faint, after the header. Nested groups inherit it visibly.
        string? sourceLabel = group.Parent is null && group.IsReadOnly ? group.Source.DisplayPath : null;
        GroupHeaderText header = RowLayout.FormatGroupHeader(
            width, group.Name, group.Mode, group.IsSequenceRunning, collapsed, summary, group.Depth, sourceLabel);

        var headerAttr = new Attribute(baseAttr.Foreground, baseAttr.Background, TextStyle.Bold);
        listView.SetAttribute(headerAttr);
        listView.AddStr(header.Text);
        if (header.SourceColumn >= 0)
        {
            listView.Move(col + header.SourceColumn, row);
            listView.SetAttribute(new Attribute(baseAttr.Foreground, baseAttr.Background, TextStyle.Faint));
            listView.AddStr(header.SourceText);
        }

        // The name follows the "▾ " or "▸ " after the indent, and is cut to the width after that.
        int shift = RowLayout.IndentOf(group.Depth);
        DrawMatches(listView, headerAttr, group, group.Name, col + shift + HeaderNameOffset, row, width - shift - HeaderNameOffset);
    }

    /// <summary>
    /// Draws the matches of <see cref="Search"/> in a name over the text already drawn there, in the match color, or in the active
    /// color for <see cref="ActiveMatch"/>. Only the part of a match within the <paramref name="visible"/> columns from
    /// <paramref name="column"/> is drawn, the rest of the name being cut. The matched text keeps the style of <paramref name="attr"/>.
    /// </summary>
    private void DrawMatches(ListView listView, Attribute attr, ItemEntry item, string name, int column, int row, int visible)
    {
        if (Search is not { } pattern || visible <= 0)
        {
            return;
        }

        foreach (SearchMatch match in pattern.Matches(name))
        {
            int offset = TextWidth.Columns(name[..match.Start]);
            int room = visible - offset;
            if (room <= 0)
            {
                continue;
            }

            bool active = ActiveMatch is { } current
                && ReferenceEquals(current.Item, item)
                && current.Start == match.Start
                && current.Length == match.Length;
            listView.Move(column + offset, row);
            listView.SetAttribute(active ? SearchColors.Active(attr) : SearchColors.Match(attr));
            listView.AddStr(TextWidth.Truncate(name.Substring(match.Start, match.Length), room));
        }
    }

    private static StatusText StatusOf(CommandEntry entry) =>
        StatusFormatter.Format(entry.Process.Snapshot, entry.IsQueued, DateTimeOffset.Now);
}
