using System.Collections;
using System.Collections.Specialized;
using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Shared.Tui;

/// <summary>
/// Renders the rows of the recent-configs picker (see <see cref="RecentRow"/>). Each row starts with a two-column gutter: "&gt; " on the
/// selected row, two spaces on the others. The selected row is bold, every part keeping its own colour. No row takes the focus colours:
/// all rows use the list's normal background. The parts are drawn in <see cref="PickerColors"/>, and each is cut at the right edge as
/// the list cuts its text. The rows never change, so the source raises no change events. UI thread only.
/// </summary>
internal sealed class RecentRowsSource : IListDataSource
{
    private const string SelectedGutter = "> ";
    private const string OtherGutter = "  ";
    private const string Separator = "  ";
    private const string NotFound = "(not found)";

    private readonly IReadOnlyList<RecentRow> _rows;

    /// <param name="rows">The rows shown.</param>
    public RecentRowsSource(IReadOnlyList<RecentRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows = rows;
        MaxItemLength = rows.Count == 0 ? 0 : rows.Max(row => SelectedGutter.Length + TextWidth.Columns(row.Text));
    }

    /// <summary>The rows shown.</summary>
    internal IReadOnlyList<RecentRow> Rows => _rows;

    public int Count => _rows.Count;

    public int MaxItemLength { get; }

    public bool SuspendCollectionChangedEvent { get; set; }

    // The rows never change, so no change is ever raised. The event is kept for the interface.
    public event NotifyCollectionChangedEventHandler? CollectionChanged
    {
        add { }
        remove { }
    }

    // The picker does not mark rows; ListView's marking stays unused.
    public bool IsMarked(int item) => false;

    public void SetMark(int item, bool value)
    {
    }

    public bool RenderMark(ListView listView, int item, int row, bool isMarked, bool markMultiple) => false;

    public IList ToList() => _rows.ToList();

    public void Render(ListView listView, bool selected, int item, int col, int row, int width, int viewportX)
    {
        if (item < 0 || item >= _rows.Count)
        {
            return;
        }

        // ListView fills the rows below its last item with the attribute it set last, without setting it again, so the attribute it set
        // before this call is restored at the end. Otherwise this row's last part would leave its colors on the rows below.
        Attribute entryAttr = listView.GetCurrentAttribute();
        try
        {
            RenderRow(listView, selected, _rows[item], col, row, width);
        }
        finally
        {
            listView.SetAttribute(entryAttr);
        }
    }

    public void Dispose()
    {
    }

    private static void RenderRow(ListView listView, bool selected, RecentRow record, int col, int row, int width)
    {
        Attribute normal = listView.GetAttributeForRole(VisualRole.Normal);
        listView.Move(col, row);

        int left = width;
        foreach (Segment segment in SegmentsOf(selected, record))
        {
            if (left <= 0)
            {
                break;
            }

            string text = Clip(segment.Text, left);
            if (text.Length == 0)
            {
                break;
            }

            listView.SetAttribute(AttributeOf(segment.Kind, normal, selected));
            listView.AddStr(text);
            left -= TextWidth.Columns(text);
        }

        if (left > 0)
        {
            listView.SetAttribute(new Attribute(normal.Foreground, normal.Background, TextStyle.None));
            listView.AddStr(new string(' ', left));
        }
    }

    // The parts of a row, left to right: the gutter, the number, the name, the (not found) column when shown, and the directory with
    // the repository name apart so that it can be coloured.
    private static IEnumerable<Segment> SegmentsOf(bool selected, RecentRow record)
    {
        yield return new Segment(selected ? SelectedGutter : OtherGutter, Part.Plain);
        yield return new Segment(record.Number, Part.Number);
        yield return new Segment(Separator, Part.Plain);
        yield return new Segment(record.Name, Part.Name);

        if (record.MissingColumn)
        {
            yield return new Segment(Separator, Part.Plain);
            yield return record.Missing
                ? new Segment(NotFound, Part.Missing)
                : new Segment(new string(' ', NotFound.Length), Part.Plain);
        }

        if (record.Directory.Length == 0)
        {
            yield break;
        }

        yield return new Segment(Separator, Part.Plain);
        string directory = record.Directory;
        if (record.RepoLength <= 0)
        {
            yield return new Segment(directory, Part.Directory);
            yield break;
        }

        int repoEnd = record.RepoStart + record.RepoLength;
        if (record.RepoStart > 0)
        {
            yield return new Segment(directory[..record.RepoStart], Part.Directory);
        }

        yield return new Segment(directory[record.RepoStart..repoEnd], Part.Repo);
        if (repoEnd < directory.Length)
        {
            yield return new Segment(directory[repoEnd..], Part.Directory);
        }
    }

    private static Attribute AttributeOf(Part part, Attribute normal, bool selected)
    {
        Color foreground = part switch
        {
            Part.Directory => PickerColors.Directory,
            Part.Repo => PickerColors.Repo,
            Part.Missing => PickerColors.Missing,
            _ => normal.Foreground,
        };

        TextStyle style = TextStyle.None;
        if (part == Part.Number)
        {
            style |= TextStyle.Faint;
        }

        if (selected)
        {
            style |= TextStyle.Bold;
        }

        return new Attribute(foreground, normal.Background, style);
    }

    // The longest prefix of the text that fits in the columns, cut at a grapheme boundary (no ellipsis, as the list cuts its text).
    private static string Clip(string text, int width)
    {
        var clipped = new StringBuilder();
        int used = 0;
        foreach ((int offset, int length, int columns) in TextWidth.Graphemes(text))
        {
            if (used + columns > width)
            {
                break;
            }

            clipped.Append(text, offset, length);
            used += columns;
        }

        return clipped.ToString();
    }

    private enum Part
    {
        Plain,
        Number,
        Name,
        Directory,
        Repo,
        Missing,
    }

    private readonly record struct Segment(string Text, Part Kind);
}
