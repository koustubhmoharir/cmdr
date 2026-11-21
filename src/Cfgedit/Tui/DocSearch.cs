using Cfgedit.Core.Model;
using Shared.Core;

namespace Cfgedit.Tui;

/// <summary>
/// A match in a document: the node, the index of its part in <see cref="DocNode.Parts"/>, the UTF-16 range of the match in the part's
/// logical value, and the order of the part in the document (see <see cref="DocSearch"/>).
/// </summary>
internal readonly record struct DocMatch(DocNode Node, int Part, int Start, int Length, int Order);

/// <summary>
/// The matches of a pattern in a document, in document order. A part's logical value is searched: the parts searched are every part
/// of every node except the summaries (<c>{3}</c>) and the indexes of JSON items, and the end-tag names of XML elements, which repeat
/// the element name. The declaration and the doctype are searched. Pure: no views.
/// </summary>
/// <remarks>
/// Document order is the order the parts are visited in: a node's parts from left to right, then its children, then its end-tag parts.
/// Every part has an order, searched or not, so that a cursor on an end row falls after the matches of its element's content.
/// </remarks>
internal sealed class DocSearch
{
    private readonly IReadOnlyList<DocMatch> _matches;
    private readonly Dictionary<PartKey, int> _orders;
    private readonly Dictionary<PartKey, IReadOnlyList<SearchMatch>> _marks;

    private DocSearch(
        IReadOnlyList<DocMatch> matches,
        Dictionary<PartKey, int> orders,
        Dictionary<PartKey, IReadOnlyList<SearchMatch>> marks,
        bool timedOut)
    {
        _matches = matches;
        _orders = orders;
        _marks = marks;
        TimedOut = timedOut;
    }

    /// <summary>The matches, in document order. Empty when the pattern timed out.</summary>
    public IReadOnlyList<DocMatch> Matches => _matches;

    /// <summary>True when the pattern timed out on the document. The search then has no matches.</summary>
    public bool TimedOut { get; }

    /// <summary>Searches a document with a pattern.</summary>
    /// <param name="document">The document.</param>
    /// <param name="pattern">The pattern.</param>
    public static DocSearch Build(ConfigDocument document, SearchPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pattern);

        var matches = new List<DocMatch>();
        var orders = new Dictionary<PartKey, int>();
        var marks = new Dictionary<PartKey, IReadOnlyList<SearchMatch>>();
        int next = 0;

        void Visit(DocNode node)
        {
            for (int p = 0; p < node.Parts.Count; p++)
            {
                DocPart part = node.Parts[p];
                int order = next++;
                var key = new PartKey(node.Path, false, p);
                orders[key] = order;
                if (!IsSearched(part.Kind))
                {
                    continue;
                }

                IReadOnlyList<SearchMatch> found = pattern.Matches(part.Value);
                if (found.Count == 0)
                {
                    continue;
                }

                marks[key] = found;
                foreach (SearchMatch match in found)
                {
                    matches.Add(new DocMatch(node, p, match.Start, match.Length, order));
                }
            }

            foreach (DocNode child in node.Children)
            {
                Visit(child);
            }

            for (int p = 0; p < node.EndParts.Count; p++)
            {
                orders[new PartKey(node.Path, true, p)] = next++;
            }
        }

        Visit(document.Root);

        // A pattern that timed out returns nothing from then on, so the matches of the parts before the timeout are dropped too.
        if (pattern.TimedOut)
        {
            matches.Clear();
            marks.Clear();
        }

        return new DocSearch(matches, orders, marks, pattern.TimedOut);
    }

    /// <summary>
    /// The order of a part, as the matches carry it. An end row is <paramref name="isEnd"/>, with its part index among the end parts. Returns
    /// -1 for a part the document does not have.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="isEnd">True for an end row's part.</param>
    /// <param name="part">The part's index among the node's parts, or its end parts.</param>
    public int OrderOf(NodePath node, bool isEnd, int part) =>
        _orders.TryGetValue(new PartKey(node, isEnd, part), out int order) ? order : -1;

    /// <summary>
    /// The marks of a part for the highlights: its matches, and the index of <paramref name="active"/> among them when the active match is
    /// in this part. Null for a part with no matches.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="part">The part's index among the node's parts.</param>
    /// <param name="active">The current match, or null for none.</param>
    public PartMarks? MarksOf(NodePath node, int part, DocMatch? active)
    {
        if (!_marks.TryGetValue(new PartKey(node, false, part), out IReadOnlyList<SearchMatch>? matches))
        {
            return null;
        }

        int activeIndex = -1;
        if (active is { } current && current.Node.Path == node && current.Part == part)
        {
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Start == current.Start)
                {
                    activeIndex = i;
                    break;
                }
            }
        }

        return new PartMarks(matches, activeIndex);
    }

    /// <summary>The parts that search finds: every part but the summaries, the indexes and the end-tag names.</summary>
    private static bool IsSearched(PartKind kind) => kind is not (PartKind.Summary or PartKind.Index or PartKind.EndName);

    private readonly record struct PartKey(NodePath Node, bool IsEnd, int Part);
}
