using Cfgedit.Core.Yaml;

namespace Cfgedit.Core.Model;

/// <summary>
/// Decides which node owns each comment of a YAML stream, so that the document tree can show it in place. A comment in the parts of a
/// block or flow collection belongs to that collection, and is shown among its entries in source order (so a comment line above an entry
/// is the row before it, and a trailing comment is the row after its entry). A comment between a key and a collection value that follows
/// it (<c>key:</c> then a comment, then the value) belongs to that value, as its first rows. A comment before the root value of a document
/// belongs to the root collection, or to the document when the root is a scalar; so does a comment after it. A comment between two
/// documents belongs to the next document. A comment after the last document belongs to it. A stream with no document holds its
/// comments in the empty root.
/// </summary>
internal sealed class YamlComments
{
    private readonly Dictionary<YamlNode, List<YamlComment>> _own = new();
    private readonly Dictionary<YamlDocumentNode, List<YamlComment>> _before = new();
    private readonly Dictionary<YamlDocumentNode, List<YamlComment>> _after = new();
    private readonly List<YamlComment> _stream = [];

    private YamlComments()
    {
    }

    /// <summary>Assigns every comment of the stream to its owner.</summary>
    internal static YamlComments Assign(YamlDocument syntax)
    {
        var comments = new YamlComments();
        var pending = new List<YamlComment>();
        YamlDocumentNode? last = null;
        foreach (YamlNode item in syntax.Items)
        {
            if (item is YamlComment comment)
            {
                pending.Add(comment);
            }
            else if (item is YamlDocumentNode document && !YamlDocuments.IsCounted(document))
            {
                // Not a document (see YamlDocuments.Counted): its comments belong to the stream.
                pending.AddRange(YamlDocuments.CommentsIn(document));
            }
            else if (item is YamlDocumentNode counted)
            {
                AddTo(comments._before, counted, pending);
                pending.Clear();
                comments.AssignDocument(counted);
                last = counted;
            }
        }

        if (last is null)
        {
            comments._stream.AddRange(pending);
        }
        else
        {
            AddTo(comments._after, last, pending);
        }

        return comments;
    }

    /// <summary>The comments a collection, or a document, owns directly, in source order.</summary>
    internal IReadOnlyList<YamlComment> Of(YamlNode owner)
    {
        return Sorted(_own.TryGetValue(owner, out List<YamlComment>? list) ? list : []);
    }

    /// <summary>The comments of a document that come before its root value, in source order.</summary>
    internal IReadOnlyList<YamlComment> Before(YamlDocumentNode document)
    {
        return Sorted(_before.TryGetValue(document, out List<YamlComment>? list) ? list : []);
    }

    /// <summary>The comments of a document that come after its root value, in source order.</summary>
    internal IReadOnlyList<YamlComment> After(YamlDocumentNode document)
    {
        return Sorted(_after.TryGetValue(document, out List<YamlComment>? list) ? list : []);
    }

    /// <summary>The comments of a stream that has no document, in source order.</summary>
    internal IReadOnlyList<YamlComment> Stream => Sorted(_stream);

    private void AssignDocument(YamlDocumentNode document)
    {
        foreach (YamlNode part in document.Parts)
        {
            if (part is YamlComment comment)
            {
                if (document.Root is null || comment.Start < document.Root.Start)
                {
                    AddTo(_before, document, [comment]);
                }
                else
                {
                    AddTo(_after, document, [comment]);
                }
            }
            else
            {
                Walk(part, document);
            }
        }
    }

    /// <summary>Assigns the comments inside a node to their owners. <paramref name="owner"/> owns the comments that no collection inside the node owns.</summary>
    private void Walk(YamlNode node, YamlNode owner)
    {
        switch (node)
        {
            case YamlComment comment:
                AddTo(_own, owner, [comment]);
                break;
            case YamlBlockMapping or YamlFlowMapping or YamlBlockSequence or YamlFlowSequence:
                foreach (YamlNode part in node.Parts)
                {
                    Walk(part, node);
                }

                break;
            case YamlCompositeNode composite:
                // An entry or an item: a comment before its collection value belongs to that value, and any other comment to the owner.
                YamlValueNode? value = ValueOf(composite);
                foreach (YamlNode part in composite.Parts)
                {
                    YamlNode target = owner;
                    if (part is YamlComment && value is { } inner && IsCollection(inner) && part.Start < inner.Start)
                    {
                        target = inner;
                    }

                    Walk(part, target);
                }

                break;
        }
    }

    private static YamlValueNode? ValueOf(YamlCompositeNode node)
    {
        return node switch
        {
            YamlMappingEntry entry => entry.Value,
            YamlFlowMappingEntry entry => entry.Value,
            YamlSequenceItem item => item.Value,
            _ => null,
        };
    }

    /// <summary>True for a block or flow mapping or sequence: a node that holds entries.</summary>
    internal static bool IsCollection(YamlNode node)
    {
        return node is YamlBlockMapping or YamlFlowMapping or YamlBlockSequence or YamlFlowSequence;
    }

    private static void AddTo<TKey>(Dictionary<TKey, List<YamlComment>> map, TKey key, IEnumerable<YamlComment> comments)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out List<YamlComment>? list))
        {
            list = [];
            map[key] = list;
        }

        list.AddRange(comments);
    }

    private static List<YamlComment> Sorted(IEnumerable<YamlComment> comments)
    {
        return comments.OrderBy(comment => comment.Start).ToList();
    }
}
