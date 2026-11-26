using Mdedit.Core.Render;
using Terminal.Gui.Editor.Document;
using Terminal.Gui.Editor.Document.Folding;

namespace Mdedit.Tui;

/// <summary>
/// The editor's folds and their link to the view's fold state. The editor's <see cref="FoldingManager"/> holds one folding per fold unit (see
/// <see cref="MarkdownFoldingStrategy"/>); the view keeps its folded units as tracked positions. The two agree at the moments that matter:
/// entering edit mode takes the view's state into the foldings, and every change of a folding's state (a gutter click, or a folding moved
/// by an update) gives the view the editor's state.
/// </summary>
/// <remarks>
/// <para>
/// <b>Automatic folding is off.</b> The editor does not run the strategy by itself. A change that may affect the foldings (see
/// <see cref="MarkdownFoldingStrategy.ChangeMayAffectFoldings"/>) raises <see cref="UpdateRequested"/>, and the window runs
/// <see cref="Update"/> later, once per batch of changes. Until then the foldings move with the edits, as the editor keeps them.
/// </para>
/// <para>
/// <b>Entering.</b> <see cref="Update"/> and then <see cref="TakeViewState"/> make the foldings of the current text, with the folded ones the
/// view has. The view must show the current text (the window renders first), so that its starts are the buffer's.
/// </para>
/// </remarks>
/// <param name="editor">The source editor, whose buffer is the document.</param>
/// <param name="view">The rendered view, which keeps the fold state the editor shares.</param>
internal sealed class EditorFolds
{
    private readonly SourceEditor _editor;
    private readonly RenderedView _view;
    private readonly MarkdownFoldingStrategy _strategy = new();
    private readonly FoldingManager _manager;

    // True while the foldings are being changed by the window or the strategy, so that the changes are not given to the view one by one.
    private bool _updating;

    /// <summary>Creates the folds of an editor and links them to a view.</summary>
    /// <param name="editor">The source editor.</param>
    /// <param name="view">The rendered view.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public EditorFolds(SourceEditor editor, RenderedView view)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(view);

        _editor = editor;
        _view = view;
        _manager = new FoldingManager(editor.Buffer);
        editor.AutomaticFolding = false;
        editor.FoldingManager = _manager;
        _manager.FoldingChanged += OnFoldingChanged;
        ((IDocument)editor.Buffer).TextChanging += OnTextChanging;
    }

    /// <summary>Raised when a change of the text may add or remove a folding. The window calls <see cref="Update"/> later.</summary>
    public event Action? UpdateRequested;

    /// <summary>Recomputes the foldings from the buffer's Markdown. The folded state of the units whose starts survive is kept.</summary>
    public void Update()
    {
        _updating = true;
        try
        {
            _strategy.UpdateFoldings(_manager, _editor.Buffer);
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>
    /// Sets each folding's state from the view's fold state: the folding of a unit is folded when the view has it folded. Called on entering
    /// edit mode, after <see cref="Update"/>.
    /// </summary>
    public void TakeViewState()
    {
        HashSet<int> folded = _view.FoldedStarts.ToHashSet();
        _updating = true;
        try
        {
            foreach (FoldingSection folding in _manager.AllFoldings)
            {
                if (Anchor(folding) is { } anchor)
                {
                    folding.IsFolded = folded.Contains(anchor.Offset);
                }
            }
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>
    /// Gives the view the editor's fold state: the units whose foldings are folded. Called after an update and on leaving edit mode, and by
    /// every change of a folding's state, so that the view shows the same folds.
    /// </summary>
    public void GiveViewState()
    {
        var starts = new List<int>();
        foreach (FoldingSection folding in _manager.AllFoldings)
        {
            if (folding.IsFolded && Anchor(folding) is { } anchor)
            {
                starts.Add(anchor.Offset);
            }
        }

        _view.SetFoldedStarts(starts);
    }

    /// <summary>
    /// Unfolds the foldings that hide an offset, so that a search can select the match there. A folding hides the text after its start, up to
    /// its end.
    /// </summary>
    /// <param name="offset">The offset of the match's start.</param>
    public void UnfoldHiding(int offset)
    {
        foreach (FoldingSection folding in _manager.AllFoldings)
        {
            if (folding.IsFolded && folding.StartOffset < offset && offset <= folding.EndOffset)
            {
                folding.IsFolded = false;
            }
        }
    }

    /// <summary>The anchor of a folding's unit (its start, which moves with the edits), or null for a folding without one.</summary>
    private static AnchorOffset? Anchor(FoldingSection folding) =>
        folding.Tag is AnchorOffset { IsDeleted: false } anchor ? anchor : null;

    /// <summary>A folding changed: unless the change is the window's own update, the view is given the new state.</summary>
    private void OnFoldingChanged(object? sender, EventArgs e)
    {
        if (!_updating)
        {
            GiveViewState();
        }
    }

    /// <summary>A change of the buffer: the window is asked to update the foldings when the change may affect them.</summary>
    private void OnTextChanging(object? sender, TextChangeEventArgs args)
    {
        if (MarkdownFoldingStrategy.MayChangeStructure(args.InsertedText.Text, args.RemovedText.Text))
        {
            UpdateRequested?.Invoke();
        }
    }
}
