using Cfgedit.Core.Yaml;

namespace Cfgedit.Core.Model;

/// <summary>
/// The logical content of a YAML stream: its data, as a mapping is an object, a sequence an array, and a scalar a scalar with its kind,
/// its decoded text, whether it is plain, its anchor and its resolved tag. An alias is an alias node that names its anchor, not expanded.
/// A stream with one document has that document's root as its data; a stream with several has an array of their roots; a stream with none
/// is an empty object. A missing value is a plain null scalar with empty text.
/// </summary>
internal static class YamlLogical
{
    /// <summary>The data of a stream: see the class summary.</summary>
    /// <exception cref="Cfgedit.Core.CfgeditException">A tagged scalar's value does not read as its tag's type.</exception>
    internal static LogicalNode Data(YamlDocument syntax)
    {
        IReadOnlyList<YamlDocumentNode> documents = YamlDocuments.Counted(syntax);
        if (documents.Count == 0)
        {
            return new LogicalNode(LogicalKind.Object, null, null, null, [], []);
        }

        if (documents.Count == 1)
        {
            return Document(documents[0]);
        }

        return new LogicalNode(LogicalKind.Array, null, null, null, [], documents.Select(Document).ToList());
    }

    /// <summary>The array named <c>#comments</c>: every comment of the stream, in file order.</summary>
    internal static LogicalNode Comments(YamlDocument syntax)
    {
        var comments = new List<LogicalNode>();
        foreach (YamlNode item in syntax.Items)
        {
            foreach (YamlComment comment in YamlDocuments.CommentsIn(item))
            {
                comments.Add(new LogicalNode(LogicalKind.Comment, null, null, comment.Content, [], []));
            }
        }

        return new LogicalNode(LogicalKind.Array, "#comments", null, null, [], comments);
    }

    private static LogicalNode Document(YamlDocumentNode document)
    {
        return Value(document.Root, null, YamlTags.HandlesOf(document));
    }

    private static LogicalNode Value(YamlValueNode? value, string? name, IReadOnlyDictionary<string, string> handles)
    {
        switch (value)
        {
            case null:
                return new LogicalNode(LogicalKind.Scalar, name, null, string.Empty, [], [], yamlKind: YamlValueKind.Null, yamlPlain: true);

            case YamlAlias alias:
                return new LogicalNode(LogicalKind.Alias, name, null, alias.Name, [], []);

            case YamlScalar scalar:
            {
                bool plain = scalar.Style == YamlScalarStyle.Plain;
                string? tag = YamlTags.Resolve(scalar.Tag, handles);
                YamlValueKind kind = YamlKinds.KindOf(plain, tag, scalar.Value);
                return new LogicalNode(
                    LogicalKind.Scalar,
                    name,
                    null,
                    scalar.Value,
                    [],
                    [],
                    yamlKind: kind,
                    yamlPlain: plain,
                    yamlAnchor: AnchorName(scalar),
                    yamlTag: tag);
            }

            case YamlBlockMapping or YamlFlowMapping:
                return Mapping(value, name, handles);

            default:
                return Sequence(value, name, handles);
        }
    }

    private static LogicalNode Mapping(YamlValueNode mapping, string? name, IReadOnlyDictionary<string, string> handles)
    {
        var children = new List<LogicalNode>();
        switch (mapping)
        {
            case YamlBlockMapping block:
                foreach (YamlMappingEntry entry in block.Entries)
                {
                    children.Add(Value(entry.Value, entry.Key.Value, handles));
                }

                break;
            case YamlFlowMapping flow:
                foreach (YamlFlowMappingEntry entry in flow.Entries)
                {
                    children.Add(Value(entry.Value, entry.Key.Value, handles));
                }

                break;
        }

        return new LogicalNode(
            LogicalKind.Object,
            name,
            null,
            null,
            [],
            children,
            yamlAnchor: AnchorName(mapping),
            yamlTag: YamlTags.Resolve(mapping.Tag, handles));
    }

    private static LogicalNode Sequence(YamlValueNode sequence, string? name, IReadOnlyDictionary<string, string> handles)
    {
        var children = new List<LogicalNode>();
        switch (sequence)
        {
            case YamlBlockSequence block:
                foreach (YamlSequenceItem item in block.Items)
                {
                    children.Add(Value(item.Value, null, handles));
                }

                break;
            case YamlFlowSequence flow:
                foreach (YamlValueNode item in flow.Items)
                {
                    children.Add(Value(item, null, handles));
                }

                break;
        }

        return new LogicalNode(
            LogicalKind.Array,
            name,
            null,
            null,
            [],
            children,
            yamlAnchor: AnchorName(sequence),
            yamlTag: YamlTags.Resolve(sequence.Tag, handles));
    }

    /// <summary>The anchor name of a node, without its <c>&amp;</c>, or null.</summary>
    private static string? AnchorName(YamlValueNode node)
    {
        return node.Anchor is { } anchor ? anchor.Text[1..] : null;
    }

}
