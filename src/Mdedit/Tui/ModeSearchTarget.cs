using System.Collections.Immutable;
using Mdedit.Core.Render;
using Shared.Core;

namespace Mdedit.Tui;

/// <summary>
/// The search target of the window: the rendered view in view mode, and the source buffer in edit mode, chosen when each call is made, so
/// that a change of mode never leaves the search on the other document.
/// </summary>
/// <remarks>
/// In view mode the matches are those of <see cref="RenderSearch"/> (the rendered text, mapped to source). In edit mode they are the matches
/// of <see cref="SearchPattern.Matches"/> over the buffer's text, so the markup is searched too: <c>**b</c> finds the bold text's start.
/// </remarks>
/// <param name="view">The rendered view.</param>
/// <param name="editor">The source editor.</param>
/// <param name="folds">The editor's folds, which open what hides a match in edit mode.</param>
/// <param name="editing">Whether edit mode is shown.</param>
internal sealed class ModeSearchTarget(RenderedView view, SourceEditor editor, EditorFolds folds, Func<bool> editing) : IMatchTarget
{
    /// <summary>The view's cursor in view mode, the editor's caret in edit mode.</summary>
    public int CursorOffset => editing() ? editor.CaretOffset : view.CursorOffset;

    /// <summary>The matches of the rendered text in view mode, or of the source text in edit mode.</summary>
    /// <inheritdoc/>
    public RenderSearchResult Find(SearchPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        if (!editing())
        {
            return RenderSearch.Find(view.Document, pattern);
        }

        ImmutableArray<SourceRange> matches = pattern.Matches(editor.Buffer.Text)
            .Select(match => new SourceRange(match.Start, match.End))
            .ToImmutableArray();
        return new RenderSearchResult(matches, pattern.TimedOut);
    }

    /// <summary>Gives the view its highlights in view mode; the editor has none.</summary>
    /// <inheritdoc/>
    public void ShowMatches(IReadOnlyList<SourceRange> matches, int active)
    {
        if (!editing())
        {
            view.SetMatches(matches, active);
        }
    }

    /// <summary>
    /// Selects the match: the view's cursor goes to its start, and the editor's selection runs from the match to its start, with the caret
    /// there. What hides the match in edit mode is unfolded first, as the view does.
    /// </summary>
    /// <inheritdoc/>
    public void Select(SourceRange range)
    {
        if (!editing())
        {
            view.SelectRange(range);
            return;
        }

        folds.UnfoldHiding(range.Start);
        editor.SelectMatch(range.Start, range.End);
    }
}
