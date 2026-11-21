using System.Text;
using Cfgedit.Core.Model;
using Cfgedit.Core.Text;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// Shared helpers for the model tests. The dump writes a document tree as text so that a test can compare it with a literal
/// written by hand. The dump's own format is simple and is not the code under test.
/// </summary>
internal static class ModelTestSupport
{
    public static ConfigDocument Parse(string text, IConfigFormat format)
    {
        return ConfigDocument.Parse(SourceText.Create(text, Encoding.UTF8, false), format);
    }

    public static string FixturePath(string kind, string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", kind, name);
    }

    /// <summary>Every node below and including <paramref name="node"/>, in document order.</summary>
    public static IEnumerable<DocNode> AllNodes(DocNode node)
    {
        yield return node;
        foreach (DocNode child in node.Children)
        {
            foreach (DocNode descendant in AllNodes(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// The tree as text, one node per line, indented two spaces per level. A node line is the kind, the path, the source span,
    /// the JSON kind when there is one, then each part; end parts follow <c>end:</c>. A part is its kind, its attribute index
    /// when it has one, its value in quotes, its span, <c>+</c> when editable or <c>-</c> when not, and <c>raw</c> when raw.
    /// </summary>
    public static string Dump(DocNode root)
    {
        var builder = new StringBuilder();
        Write(builder, root, 0);
        return builder.ToString().TrimEnd('\n');
    }

    private static void Write(StringBuilder builder, DocNode node, int depth)
    {
        builder.Append(' ', depth * 2)
            .Append(node.Kind).Append(' ')
            .Append(node.Path.ToString())
            .Append(" [").Append(node.Start).Append(',').Append(node.End).Append(')');
        if (node.JsonKind is { } jsonKind)
        {
            builder.Append(" json=").Append(jsonKind);
        }

        foreach (DocPart part in node.Parts)
        {
            builder.Append(' ').Append(Part(part));
        }

        if (node.EndParts.Count > 0)
        {
            builder.Append(" end:").Append(string.Join(" ", node.EndParts.Select(Part)));
        }

        builder.Append('\n');
        foreach (DocNode child in node.Children)
        {
            Write(builder, child, depth + 1);
        }
    }

    private static string Part(DocPart part)
    {
        string index = part.AttributeIndex >= 0 ? "#" + part.AttributeIndex : string.Empty;
        string editable = part.Editable ? "+" : "-";
        string raw = part.IsRaw ? "raw" : string.Empty;
        return $"{part.Kind}{index}\"{part.Value.Replace("\n", "\\n")}\"[{part.Start},{part.End}){editable}{raw}";
    }
}
