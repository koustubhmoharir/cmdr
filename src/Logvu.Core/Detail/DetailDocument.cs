using Logvu.Core.Config;
using Logvu.Core.Reading;
using Logvu.Core.Store;

namespace Logvu.Core.Detail;

/// <summary>
/// The detail of one event as lines: the structured document (fields or summary, the tree, stack analysis) or the raw text, with the
/// expansion state of <see cref="Options"/> applied. Pure: it has no terminal and no I/O.
/// </summary>
/// <remarks>
/// <para>
/// The document is immutable. Navigation returns new <see cref="DetailOptions"/>, and the caller builds the next document from them with
/// <see cref="Build"/>. Ids are stable across rebuilds of the same event, so an options value from one build applies to the next.
/// </para>
/// <para>
/// Full-document indices (<see cref="FullLines"/>) are the lines with every node and fold expanded, in the current mode. A search hit maps
/// to one of them; <see cref="ExpandToReveal"/> then gives the options under which that line is shown, and <see cref="VisibleIndex"/>
/// gives its row in <see cref="Lines"/>.
/// </para>
/// </remarks>
public sealed class DetailDocument
{
    private readonly IReadOnlyList<ModelLine> _model;
    private readonly IReadOnlyDictionary<string, bool> _defaults;
    private readonly List<int> _fullOf;
    private readonly int[] _visibleOf;

    private DetailDocument(
        DetailOptions options,
        IReadOnlyList<ModelLine> model,
        IReadOnlyDictionary<string, bool> defaults,
        IReadOnlyList<string> nodeIds,
        IReadOnlyList<string> foldIds,
        bool truncated)
    {
        Options = options;
        _model = model;
        _defaults = defaults;
        NodeIds = nodeIds;
        FoldIds = foldIds;
        Truncated = truncated;
        FullLines = model.Select(m => ShowAs(m.Line, expanded: true, options)).ToList();

        var lines = new List<DetailLine>();
        _fullOf = [];
        _visibleOf = new int[model.Count];
        for (int i = 0; i < model.Count; i++)
        {
            if (IsVisible(model[i].Parent))
            {
                _visibleOf[i] = lines.Count;
                lines.Add(ShowAs(model[i].Line, expanded: null, options));
                _fullOf.Add(i);
            }
            else
            {
                _visibleOf[i] = -1;
            }
        }

        Lines = lines;
    }

    /// <summary>The options the document was built with.</summary>
    public DetailOptions Options { get; }

    /// <summary>The lines shown under <see cref="Options"/>: the structured lines, or the raw lines in raw mode.</summary>
    public IReadOnlyList<DetailLine> Lines { get; }

    /// <summary>Every line of the current mode, with every node and fold expanded. Indices are those of <see cref="ExpandToReveal"/>.</summary>
    public IReadOnlyList<DetailLine> FullLines { get; }

    /// <summary>Whether the event's text was cut at the store's cap. The document then ends with a notice saying so.</summary>
    public bool Truncated { get; }

    /// <summary>The ids of the JSON nodes of the event, in document order. Empty for an unavailable event.</summary>
    public IReadOnlyList<string> NodeIds { get; }

    /// <summary>The ids of the folds of the event, in document order. Empty for an unavailable event.</summary>
    public IReadOnlyList<string> FoldIds { get; }

    /// <summary>
    /// Builds the document of an event. The structured model is always built, so the ids exist in raw mode too; raw mode shows the raw lines.
    /// </summary>
    /// <param name="detail">The event's detail.</param>
    /// <param name="config">The configuration: the framework prefixes and highlight rules.</param>
    /// <param name="options">The view mode and the expansion state.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public static DetailDocument Build(EventDetail detail, LogvuConfig config, DetailOptions options)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(options);

        switch (detail)
        {
            case EventDetail.Unavailable unavailable:
                return new DetailDocument(
                    options,
                    [new ModelLine(DetailBuilder.NoticeLine(unavailable.Reason).Line, null)],
                    new Dictionary<string, bool>(StringComparer.Ordinal),
                    [],
                    [],
                    truncated: false);

            case EventDetail.Loaded loaded:
                DetailModel structure = new DetailBuilder(loaded, config).Build();
                IReadOnlyList<ModelLine> structured = Decorate(structure.Lines, config, loaded.Truncated);
                IReadOnlyList<ModelLine> raw = Decorate(
                    DetailBuilder.SplitLines(loaded.Text)
                        .Select(text => new ModelLine(new DetailLine(text, [], LineKind.Raw, 0, null, "raw"), null))
                        .ToList(),
                    config,
                    loaded.Truncated);
                return new DetailDocument(
                    options,
                    options.Raw ? raw : structured,
                    structure.Defaults,
                    structure.NodeIds,
                    structure.FoldIds,
                    loaded.Truncated);

            default:
                throw new ArgumentException($"Unknown detail kind '{detail.GetType().Name}'.", nameof(detail));
        }
    }

    /// <summary>Whether a node or fold is expanded under <see cref="Options"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="id"/> is not a node or fold of this event.</exception>
    public bool IsExpanded(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return ExpandedIn(id, Options);
    }

    /// <summary>The options with the expansion of one node or fold flipped from its current state.</summary>
    /// <param name="nodeOrFoldId">A JSON node id or a fold id of this event.</param>
    /// <exception cref="ArgumentException"><paramref name="nodeOrFoldId"/> is not a node or fold of this event.</exception>
    public DetailOptions Toggle(string nodeOrFoldId)
    {
        ArgumentNullException.ThrowIfNull(nodeOrFoldId);
        _ = ExpandedIn(nodeOrFoldId, Options);

        var toggled = new HashSet<string>(Options.Toggled, StringComparer.Ordinal);
        FlipMembership(toggled, nodeOrFoldId);
        return Options with { Toggled = toggled };
    }

    /// <summary>
    /// The options that collapse every fold of the event if they are all expanded, and otherwise expand every fold. Nodes are not changed.
    /// </summary>
    public DetailOptions ToggleAllFolds()
    {
        if (FoldIds.Count == 0)
        {
            return Options;
        }

        bool allExpanded = FoldIds.All(id => ExpandedIn(id, Options));
        var toggled = new HashSet<string>(Options.Toggled, StringComparer.Ordinal);
        foreach (string id in FoldIds)
        {
            if (ExpandedIn(id, Options) == allExpanded)
            {
                FlipMembership(toggled, id);
            }
        }

        return Options with { Toggled = toggled };
    }

    /// <summary>
    /// The options under which the line at <paramref name="fullLineIndex"/> is shown: every node and fold around it is expanded. Returns the
    /// current options when nothing around the line is collapsed, and always in raw mode (which has no nodes or folds).
    /// </summary>
    /// <param name="fullLineIndex">An index into <see cref="FullLines"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fullLineIndex"/> is not an index into <see cref="FullLines"/>.</exception>
    public DetailOptions ExpandToReveal(int fullLineIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fullLineIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fullLineIndex, _model.Count);

        var toggled = new HashSet<string>(Options.Toggled, StringComparer.Ordinal);
        for (Anchor? anchor = _model[fullLineIndex].Parent; anchor is not null; anchor = anchor.Parent)
        {
            if (!ExpandedIn(anchor.Id, Options))
            {
                FlipMembership(toggled, anchor.Id);
            }
        }

        return Options with { Toggled = toggled };
    }

    /// <summary>The row of a full-document line in <see cref="Lines"/>, or -1 when the line is hidden under <see cref="Options"/>.</summary>
    /// <param name="fullLineIndex">An index into <see cref="FullLines"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fullLineIndex"/> is not an index into <see cref="FullLines"/>.</exception>
    public int VisibleIndex(int fullLineIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fullLineIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fullLineIndex, _visibleOf.Length);
        return _visibleOf[fullLineIndex];
    }

    /// <summary>The index in <see cref="FullLines"/> of a row of <see cref="Lines"/>.</summary>
    /// <param name="visibleIndex">An index into <see cref="Lines"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="visibleIndex"/> is not an index into <see cref="Lines"/>.</exception>
    public int FullIndex(int visibleIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(visibleIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(visibleIndex, _fullOf.Count);
        return _fullOf[visibleIndex];
    }

    private static IReadOnlyList<ModelLine> Decorate(IEnumerable<ModelLine> lines, LogvuConfig config, bool truncated)
    {
        var result = lines
            .Select(m => m with { Line = HighlightApplier.Apply(m.Line, config.Highlights) })
            .ToList();
        if (truncated)
        {
            result.Add(DetailBuilder.NoticeLine($"… truncated at {EventText.DefaultCap} bytes"));
        }

        return result;
    }

    private bool IsVisible(Anchor? anchor)
    {
        for (Anchor? current = anchor; current is not null; current = current.Parent)
        {
            if (!ExpandedIn(current.Id, Options))
            {
                return false;
            }
        }

        return true;
    }

    private bool ExpandedIn(string id, DetailOptions options)
    {
        if (!_defaults.TryGetValue(id, out bool defaultExpanded))
        {
            throw new ArgumentException($"'{id}' is not a node or fold of this event.", nameof(id));
        }

        return defaultExpanded ^ options.Toggled.Contains(id);
    }

    /// <summary>
    /// The line as shown with its node's marker in the given state: <paramref name="expanded"/> when it is set, otherwise the state the
    /// options give the line's node. A line that is not a JSON node is returned unchanged.
    /// </summary>
    private DetailLine ShowAs(DetailLine line, bool? expanded, DetailOptions options)
    {
        if (line.NodeId is null || line.Kind != LineKind.Json)
        {
            return line;
        }

        return WithMarker(line, expanded ?? ExpandedIn(line.NodeId, options));
    }

    /// <summary>
    /// The line with a node's expansion marker set to <paramref name="expanded"/>. The marker is the first non-space character of a node
    /// line, so the line's spans and indent do not change.
    /// </summary>
    private static DetailLine WithMarker(DetailLine line, bool expanded)
    {
        int index = line.Text.Length - line.Text.TrimStart(' ').Length;
        if (index >= line.Text.Length || line.Text[index] is not ('▾' or '▸'))
        {
            return line;
        }

        char wanted = expanded ? '▾' : '▸';
        if (line.Text[index] == wanted)
        {
            return line;
        }

        return line with { Text = string.Concat(line.Text.AsSpan(0, index), wanted.ToString(), line.Text.AsSpan(index + 1)) };
    }

    private static void FlipMembership(HashSet<string> set, string id)
    {
        if (!set.Remove(id))
        {
            set.Add(id);
        }
    }
}
