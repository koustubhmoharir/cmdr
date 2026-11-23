using Cfgedit.Core.Model;
using YamlDotNet.Core;
using DotNet = YamlDotNet.RepresentationModel;

namespace Cfgedit.Core.Yaml;

/// <summary>
/// The YAML cross-check: the text is parsed again by YamlDotNet, an independent parser, and its nodes are converted to the logical shape of
/// cfgedit's data (see <see cref="YamlLogical.Data"/>). The two must be equal, in order: a mapping keeps its order in both. A disagreement
/// is cfgedit's bug or YamlDotNet's, never the file's, so it is reported as an internal parse mismatch.
/// </summary>
/// <remarks>
/// YamlDotNet's representation shares one node for an anchor and every alias to it. A node that is visited a second time is therefore an
/// alias, named by the anchor it carries, and the first visit is the anchor's own node.
/// </remarks>
internal static class YamlCrossCheck
{
    /// <summary>Parses the text with YamlDotNet and checks that its data is the given data.</summary>
    /// <exception cref="Cfgedit.Core.CfgeditException">YamlDotNet refuses the text, or its data differs from <paramref name="data"/>.</exception>
    internal static void Verify(string text, LogicalNode data)
    {
        var stream = new DotNet.YamlStream();
        try
        {
            stream.Load(new StringReader(text));
        }
        catch (YamlException ex)
        {
            throw Mismatch("YamlDotNet refuses text that cfgedit accepts: " + ex.Message);
        }

        Compare(data, FromYamlDotNet(stream));
    }

    /// <summary>Compares two data trees, and throws an internal parse mismatch that names the first difference.</summary>
    /// <exception cref="Cfgedit.Core.CfgeditException">The trees differ.</exception>
    internal static void Compare(LogicalNode data, LogicalNode other)
    {
        string? difference = FirstDifference(data, other);
        if (difference is not null)
        {
            throw Mismatch(difference);
        }
    }

    /// <summary>The first difference between cfgedit's data and another data tree, described for a message, or null when they are equal.</summary>
    internal static string? FirstDifference(LogicalNode expected, LogicalNode actual)
    {
        return Diff(expected, actual, string.Empty);
    }

    /// <summary>The error for a disagreement. It names the difference and asks for a report.</summary>
    internal static CfgeditException Mismatch(string detail)
    {
        return new CfgeditException("internal parse mismatch: " + detail + " (please report this)");
    }

    /// <summary>Converts the documents of a YamlDotNet stream to the logical shape: none is an empty object, one is its root, several an array of roots.</summary>
    internal static LogicalNode FromYamlDotNet(DotNet.YamlStream stream)
    {
        IList<DotNet.YamlDocument> documents = stream.Documents;
        if (documents.Count == 0)
        {
            return new LogicalNode(LogicalKind.Object, null, null, null, [], []);
        }

        if (documents.Count == 1)
        {
            return Root(documents[0]);
        }

        return new LogicalNode(LogicalKind.Array, null, null, null, [], documents.Select(Root).ToList());
    }

    private static LogicalNode Root(DotNet.YamlDocument document)
    {
        return Node(document.RootNode, null, new HashSet<DotNet.YamlNode>(ReferenceEqualityComparer.Instance));
    }

    private static LogicalNode Node(DotNet.YamlNode node, string? name, HashSet<DotNet.YamlNode> seen)
    {
        if (!seen.Add(node))
        {
            string anchor = AnchorOf(node) ?? throw Mismatch("YamlDotNet shares a node that has no anchor");
            return new LogicalNode(LogicalKind.Alias, name, null, anchor, [], []);
        }

        switch (node)
        {
            case DotNet.YamlScalarNode scalar:
            {
                string? tag = TagOf(scalar);
                string value = scalar.Value ?? string.Empty;
                bool plain = scalar.Style == ScalarStyle.Plain;
                return new LogicalNode(
                    LogicalKind.Scalar,
                    name,
                    null,
                    value,
                    [],
                    [],
                    yamlKind: YamlKinds.KindOf(plain, tag, value),
                    yamlPlain: plain,
                    yamlAnchor: AnchorOf(scalar),
                    yamlTag: tag);
            }

            case DotNet.YamlMappingNode mapping:
            {
                var children = new List<LogicalNode>();
                foreach (KeyValuePair<DotNet.YamlNode, DotNet.YamlNode> entry in mapping.Children)
                {
                    if (entry.Key is not DotNet.YamlScalarNode key)
                    {
                        throw Mismatch("YamlDotNet has a key that is not a scalar");
                    }

                    if (!seen.Add(key))
                    {
                        throw Mismatch("YamlDotNet uses a key as an alias");
                    }

                    children.Add(Node(entry.Value, key.Value, seen));
                }

                return new LogicalNode(LogicalKind.Object, name, null, null, [], children, yamlAnchor: AnchorOf(mapping), yamlTag: TagOf(mapping));
            }

            case DotNet.YamlSequenceNode sequence:
            {
                var children = new List<LogicalNode>();
                foreach (DotNet.YamlNode item in sequence.Children)
                {
                    children.Add(Node(item, null, seen));
                }

                return new LogicalNode(LogicalKind.Array, name, null, null, [], children, yamlAnchor: AnchorOf(sequence), yamlTag: TagOf(sequence));
            }

            default:
                throw Mismatch("YamlDotNet has a node of an unknown type: " + node.GetType().Name);
        }
    }

    private static string? AnchorOf(DotNet.YamlNode node)
    {
        return node.Anchor.IsEmpty ? null : node.Anchor.Value;
    }

    private static string? TagOf(DotNet.YamlNode node)
    {
        return node.Tag.IsEmpty ? null : node.Tag.Value;
    }

    private static string? Diff(LogicalNode expected, LogicalNode actual, string path)
    {
        if (expected.Kind != actual.Kind
            || expected.Name != actual.Name
            || expected.Value != actual.Value
            || expected.YamlKind != actual.YamlKind
            || expected.YamlPlain != actual.YamlPlain
            || expected.YamlAnchor != actual.YamlAnchor
            || expected.YamlTag != actual.YamlTag)
        {
            return Where(path) + "cfgedit has " + Describe(expected) + ", YamlDotNet has " + Describe(actual);
        }

        if (expected.Children.Count != actual.Children.Count)
        {
            return Where(path) + "cfgedit has " + expected.Children.Count + " entries, YamlDotNet has " + actual.Children.Count;
        }

        for (int i = 0; i < expected.Children.Count; i++)
        {
            LogicalNode child = expected.Children[i];
            string childPath = child.Name is null ? path + "[" + i + "]" : (path.Length == 0 ? child.Name : path + "." + child.Name);
            string? difference = Diff(child, actual.Children[i], childPath);
            if (difference is not null)
            {
                return difference;
            }
        }

        return null;
    }

    private static string Where(string path)
    {
        return path.Length == 0 ? string.Empty : "at " + path + ": ";
    }

    private static string Describe(LogicalNode node)
    {
        string text = node.Kind switch
        {
            LogicalKind.Object => "a mapping",
            LogicalKind.Array => "a sequence",
            LogicalKind.Alias => "an alias of " + node.Value,
            _ => node.YamlKind + " " + DisplayText.Quote(node.Value ?? string.Empty) + (node.YamlPlain ? " plain" : " quoted or block"),
        };
        return node.YamlTag is null ? text : text + " tagged " + node.YamlTag;
    }
}
