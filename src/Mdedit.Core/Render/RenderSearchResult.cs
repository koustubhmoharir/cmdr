using System.Collections.Immutable;

namespace Mdedit.Core.Render;

/// <summary>The result of searching a render document: the matches as source ranges, and whether the pattern timed out.</summary>
/// <param name="Matches">The matches in document order, as absolute source ranges. Empty when the pattern timed out.</param>
/// <param name="TimedOut">Whether the pattern timed out on the document (see <see cref="Shared.Core.SearchPattern.TimedOut"/>).</param>
public sealed record RenderSearchResult(ImmutableArray<SourceRange> Matches, bool TimedOut);
