using Cfgedit.Core.Text;

namespace Cfgedit.Core.Model;

/// <summary>
/// A parsed config document: its format, its source text, its document tree and the format's concrete syntax tree. A
/// document is immutable. An edit produces a new one.
/// </summary>
public sealed class ConfigDocument
{
    internal ConfigDocument(IConfigFormat format, SourceText source, DocNode root, object syntax)
    {
        Format = format;
        Source = source;
        Root = root;
        Syntax = syntax;
    }

    /// <summary>The format the document was parsed as.</summary>
    public IConfigFormat Format { get; }

    /// <summary>The decoded source text.</summary>
    public SourceText Source { get; }

    /// <summary>The document node. Its children are the top-level nodes of the document.</summary>
    public DocNode Root { get; }

    /// <summary>The format's concrete syntax tree: a <c>JsonDocument</c> or an <c>XmlDocumentSyntax</c>.</summary>
    public object Syntax { get; }

    /// <summary>
    /// Parses a file's bytes. The JSON family decodes the bytes as UTF-8 or UTF-16 by BOM. XML decodes them itself, as
    /// XML 1.0 Appendix F describes.
    /// </summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <param name="path">The file's path, carried by any error. Null when unknown.</param>
    /// <param name="format">The format to parse as.</param>
    /// <exception cref="Cfgedit.Core.CfgeditException">The bytes cannot be decoded, or the text is not valid in the format.</exception>
    public static ConfigDocument Load(ReadOnlySpan<byte> bytes, string? path, IConfigFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (format is XmlFormat xml)
        {
            return xml.ParseBytes(bytes, path);
        }

        return format.Parse(SourceText.Decode(bytes, path));
    }

    /// <summary>Parses text that is already decoded.</summary>
    /// <exception cref="Cfgedit.Core.CfgeditException">The text is not valid in the format.</exception>
    public static ConfigDocument Parse(SourceText source, IConfigFormat format)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(format);
        return format.Parse(source);
    }

    /// <summary>Applies an edit operation. Not implemented before the edit milestone.</summary>
    /// <exception cref="NotImplementedException">Always, for now.</exception>
    public EditResult Apply(EditOp op)
    {
        throw new NotImplementedException("Edit operations are not implemented yet.");
    }

    /// <summary>Returns the node at a path, or null when no node is there.</summary>
    public DocNode? Resolve(NodePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        DocNode node = Root;
        foreach (PathStep step in path.Steps)
        {
            DocNode? next = FindChild(node, step);
            if (next is null)
            {
                return null;
            }

            node = next;
        }

        return node;
    }

    /// <summary>
    /// Returns the node at a path if it exists. Otherwise it returns the deepest existing ancestor, or the nearest sibling of
    /// the first missing step: the sibling with the same name (or the same kind of step) whose ordinal or index is closest to
    /// the missing one, the lower one on a tie. The document node is returned when nothing closer exists.
    /// </summary>
    public DocNode ResolveNearest(NodePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        DocNode node = Root;
        int depth = 0;
        while (depth < path.Count)
        {
            DocNode? next = FindChild(node, path.Steps[depth]);
            if (next is null)
            {
                break;
            }

            node = next;
            depth++;
        }

        if (depth == path.Count)
        {
            return node;
        }

        return NearestSibling(node, path.Steps[depth]) ?? node;
    }

    private static DocNode? FindChild(DocNode parent, PathStep step)
    {
        foreach (DocNode child in parent.Children)
        {
            if (child.Path.Steps[child.Path.Count - 1].Equals(step))
            {
                return child;
            }
        }

        return null;
    }

    private static DocNode? NearestSibling(DocNode parent, PathStep missing)
    {
        return missing switch
        {
            MemberStep member => Nearest<MemberStep>(parent, member.Ordinal, step => step.Key == member.Key, step => step.Ordinal),
            ElementStep element => Nearest<ElementStep>(parent, element.Ordinal, step => step.Name == element.Name, step => step.Ordinal),
            ItemStep item => Nearest<ItemStep>(parent, item.Index, _ => true, step => step.Index),
            CommentStep comment => Nearest<CommentStep>(parent, comment.Index, _ => true, step => step.Index),
            TextStep text => Nearest<TextStep>(parent, text.Index, _ => true, step => step.Index),
            PIStep pi => Nearest<PIStep>(parent, pi.Index, _ => true, step => step.Index),
            _ => null,
        };
    }

    private static DocNode? Nearest<TStep>(DocNode parent, int wanted, Func<TStep, bool> sameFamily, Func<TStep, int> position)
        where TStep : PathStep
    {
        DocNode? best = null;
        int bestDistance = int.MaxValue;
        int bestPosition = int.MaxValue;
        foreach (DocNode child in parent.Children)
        {
            if (child.Path.Steps[child.Path.Count - 1] is not TStep step || !sameFamily(step))
            {
                continue;
            }

            int candidate = position(step);
            int distance = Math.Abs(candidate - wanted);
            if (distance < bestDistance || (distance == bestDistance && candidate < bestPosition))
            {
                best = child;
                bestDistance = distance;
                bestPosition = candidate;
            }
        }

        return best;
    }
}
