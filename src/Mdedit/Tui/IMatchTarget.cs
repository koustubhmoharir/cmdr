using Mdedit.Core.Render;
using Shared.Core;

namespace Mdedit.Tui;

/// <summary>
/// The document that a search runs in: the text it matches against, the offset its moves start from, and how its matches are shown. The
/// window's <see cref="ModeSearchTarget"/> gives the rendered view in view mode and the source in edit mode, so that one search serves both.
/// </summary>
internal interface IMatchTarget
{
    /// <summary>The offset the moves start from: the view's cursor in view mode, the editor's caret in edit mode.</summary>
    int CursorOffset { get; }

    /// <summary>Searches the document with a pattern. The matches are source ranges, in document order.</summary>
    /// <param name="pattern">The compiled pattern.</param>
    RenderSearchResult Find(SearchPattern pattern);

    /// <summary>
    /// Shows the matches and the active one (its index, or -1 for none). The view draws every match; the editor shows only the selection of the
    /// active match, so it has nothing to draw.
    /// </summary>
    /// <param name="matches">The matches, in document order.</param>
    /// <param name="active">The index of the active match, or -1.</param>
    void ShowMatches(IReadOnlyList<SourceRange> matches, int active);

    /// <summary>Selects a match, with the cursor (the view's cursor or the editor's caret) at its start, and scrolls it into view.</summary>
    /// <param name="range">The match, in absolute source offsets.</param>
    void Select(SourceRange range);
}
