namespace Logvu.Core.Config;

/// <summary>
/// One extra column of the timeline table, as the configuration's <c>columns</c> names it: the JSON path of a JSON event, or the field
/// name of a text event, its heading, and the widest it may be shown.
/// </summary>
public sealed record ColumnSpec
{
    /// <summary>The smallest allowed <see cref="MaxWidth"/>.</summary>
    public const int MinWidth = 1;

    /// <summary>The largest allowed <see cref="MaxWidth"/>.</summary>
    public const int MaxAllowedWidth = 200;

    /// <summary>The widest a column is shown when the configuration does not set <c>width</c>.</summary>
    public const int DefaultMaxWidth = 30;

    /// <summary>
    /// Creates a column. The title defaults to the last segment of the path, as <see cref="DefaultTitleOf"/> gives it.
    /// </summary>
    /// <param name="path">The JSON path of a JSON event, or the field name of a text event. It must not be empty.</param>
    /// <param name="title">The heading, or <c>null</c> for the default.</param>
    /// <param name="maxWidth">The widest the column is shown, from <see cref="MinWidth"/> to <see cref="MaxAllowedWidth"/>.</param>
    /// <param name="keepEnd">Whether a value too long for the column keeps its end rather than its start, see <see cref="KeepEnd"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <c>null</c> or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxWidth"/> is outside the allowed range.</exception>
    public ColumnSpec(string path, string? title = null, int maxWidth = DefaultMaxWidth, bool keepEnd = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxWidth, MinWidth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxWidth, MaxAllowedWidth);
        Path = path;
        Title = title ?? DefaultTitleOf(path);
        MaxWidth = maxWidth;
        KeepEnd = keepEnd;
    }

    /// <summary>The JSON path of a JSON event, or the field name of a text event.</summary>
    public string Path { get; }

    /// <summary>The heading of the column.</summary>
    public string Title { get; }

    /// <summary>The widest the column is shown, in characters.</summary>
    public int MaxWidth { get; }

    /// <summary>
    /// A value too long for the column keeps its end behind a leading <c>…</c>, for names that are most specific at the end such as
    /// logger names. When this is <c>false</c>, a value too long for the column keeps its start.
    /// </summary>
    public bool KeepEnd { get; }

    /// <summary>
    /// The heading a path gives when none is set: its last segment with any trailing <c>[n]</c> indices removed, so <c>req.path</c> is
    /// <c>path</c> and <c>items[0]</c> is <c>items</c>. A path whose last segment is only indices, such as <c>[0]</c>, keeps that segment.
    /// </summary>
    /// <param name="path">The path. It must not be empty.</param>
    /// <returns>The default heading. It is never empty.</returns>
    public static string DefaultTitleOf(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string last = path[(path.LastIndexOf('.') + 1)..];
        string name = last;
        int open;
        while (name.EndsWith(']') && (open = name.LastIndexOf('[')) >= 0)
        {
            name = name[..open];
        }

        return name.Length > 0 ? name : last;
    }
}
