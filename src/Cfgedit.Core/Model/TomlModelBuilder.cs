using System.Globalization;
using Cfgedit.Core.Toml;

namespace Cfgedit.Core.Model;

/// <summary>
/// Builds the document tree of a TOML document from its merged table model. Each table is a row with its key and member count, each
/// array is a row with its key and item count, each scalar is a row with its key and value, and each comment is a row. Members and
/// comments are in the order they appear in the text. A comment above a key/value line is the row before that line; a comment above
/// a header is the table's first child; a trailing comment is the row after its line, and the first child of a header's table.
/// </summary>
internal static class TomlModelBuilder
{
    /// <exception cref="Cfgedit.Core.CfgeditException">A TOML rule is broken. See <see cref="TomlMerger"/>.</exception>
    internal static TomlModel Build(TomlDocument document)
    {
        int length = document.Source.Text.Length;
        MergedTable data = TomlMerger.Merge(document);
        MeasureTable(data);
        data.Start = 0;
        data.End = length;

        var pieces = new Dictionary<DocNode, IReadOnlyList<TomlNode>>();
        var builder = new TreeBuilder(pieces);
        var tree = new DocNode(NodeKind.Document, NodePath.Empty, null, [], [], 0, length, null);
        var root = new DocPart(PartKind.Root, "$", false, false, 0, 0);
        tree.AddChild(builder.Table(tree, NodePath.Empty.Append(new ItemStep(0)), data, root));
        return new TomlModel(document, tree, data, pieces);
    }

    // ----- Spans -----

    /// <summary>
    /// Sets the span of a table: the union of its pieces, its comments and its members (with their comments). A table with no text
    /// of its own, which only the root can be, gets an empty span at zero.
    /// </summary>
    private static (int Start, int End) MeasureTable(MergedTable table)
    {
        int start = int.MaxValue;
        int end = int.MinValue;
        void Add(int from, int to)
        {
            start = Math.Min(start, from);
            end = Math.Max(end, to);
        }

        foreach (TomlNode piece in table.Pieces)
        {
            (int from, int to) = PieceSpan(piece);
            Add(from, to);
        }

        foreach (TomlComment comment in table.Leading.Concat(table.Loose))
        {
            Add(comment.Start, comment.End);
        }

        foreach (MergedMember member in table.Members)
        {
            (int from, int to) = MeasureMember(member);
            Add(from, to);
            foreach (TomlComment comment in member.Before)
            {
                Add(comment.Start, comment.End);
            }

            if (member.After is { } after)
            {
                Add(after.Start, after.End);
            }
        }

        if (start > end)
        {
            start = 0;
            end = 0;
        }

        table.Start = start;
        table.End = end;
        return (start, end);
    }

    private static (int Start, int End) MeasureMember(MergedMember member)
    {
        switch (member.Value)
        {
            case MergedTable table:
            {
                // An inline table's piece starts at its brace, but its row starts at its key.
                (int tableStart, int tableEnd) = MeasureTable(table);
                table.Start = Math.Min(tableStart, member.KeyPart.Start);
                return (table.Start, tableEnd);
            }

            case MergedArray { IsTableArray: true } tables:
            {
                int start = int.MaxValue;
                int end = int.MinValue;
                foreach (MergedValue item in tables.Items)
                {
                    (int from, int to) = MeasureTable((MergedTable)item);
                    start = Math.Min(start, from);
                    end = Math.Max(end, to);
                }

                tables.Start = start;
                tables.End = end;
                return (start, end);
            }

            case MergedArray array:
                array.Start = member.KeyPart.Start;
                array.End = array.Syntax!.End;
                MeasureElements(array);
                return (array.Start, array.End);

            case MergedScalar scalar:
                scalar.Start = member.KeyPart.Start;
                scalar.End = scalar.Value.End;
                return (scalar.Start, scalar.End);

            default:
                throw new InvalidOperationException("Unknown TOML value type: " + member.Value.GetType().Name);
        }
    }

    private static void MeasureElements(MergedArray array)
    {
        foreach (MergedValue item in array.Items)
        {
            switch (item)
            {
                case MergedTable table:
                    MeasureTable(table);
                    break;
                case MergedArray nested:
                    nested.Start = nested.Syntax!.Start;
                    nested.End = nested.Syntax.End;
                    MeasureElements(nested);
                    break;
                case MergedScalar scalar:
                    scalar.Start = scalar.Value.Start;
                    scalar.End = scalar.Value.End;
                    break;
            }
        }
    }

    private static (int Start, int End) PieceSpan(TomlNode piece)
    {
        return piece switch
        {
            TomlHeaderItem header => (header.OpenSpan.Start, header.CloseSpan.End),
            TomlKeyValueItem keyValue => (keyValue.Key.Start, keyValue.Value.End),
            TomlInlineTable inline => (inline.Start, inline.End),
            TomlInlineMember member => (member.Key.Start, member.Value.End),
            TomlArrayItem item => (item.Start, item.End),
            TomlComment comment => (comment.Start, comment.End),
            _ => throw new InvalidOperationException("Unknown TOML piece: " + piece.GetType().Name),
        };
    }

    // ----- Document tree -----

    /// <summary>Creates the rows of the document tree below a node, and records the pieces of each row.</summary>
    private sealed class TreeBuilder(Dictionary<DocNode, IReadOnlyList<TomlNode>> pieces)
    {
        /// <summary>A table row: its leading part (the root marker, a key or an array index), then its member count.</summary>
        internal DocNode Table(DocNode parent, NodePath path, MergedTable table, DocPart lead)
        {
            var parts = new[] { lead, SummaryPart(table.Members.Count, lead.Start) };
            var node = new DocNode(NodeKind.TomlTable, path, parent, parts, [], table.Start, table.End, null, null);
            pieces[node] = table.Pieces;
            FillTable(node, table);
            return node;
        }

        /// <summary>
        /// The children of a table: its leading comments, then its members in order of first appearance, with the standalone
        /// comments of its sections placed by position, and the comments attached to each member.
        /// </summary>
        private void FillTable(DocNode node, MergedTable table)
        {
            int comments = 0;
            foreach (TomlComment comment in table.Leading)
            {
                node.AddChild(Comment(node, comment, comments++));
            }

            int loose = 0;
            foreach (MergedMember member in table.Members)
            {
                int first = member.Before.Count > 0 ? member.Before[0].Start : member.Position;
                while (loose < table.Loose.Count && table.Loose[loose].Start < first)
                {
                    node.AddChild(Comment(node, table.Loose[loose++], comments++));
                }

                foreach (TomlComment comment in member.Before)
                {
                    node.AddChild(Comment(node, comment, comments++));
                }

                node.AddChild(Member(node, member));
                if (member.After is { } after)
                {
                    node.AddChild(Comment(node, after, comments++));
                }
            }

            while (loose < table.Loose.Count)
            {
                node.AddChild(Comment(node, table.Loose[loose++], comments++));
            }
        }

        private DocNode Member(DocNode parent, MergedMember member)
        {
            var key = new DocPart(PartKind.Key, member.Name, true, false, member.KeyPart.Start, member.KeyPart.End);
            NodePath path = parent.Path.Append(new MemberStep(member.Name, 0));
            return member.Value switch
            {
                MergedTable table => Table(parent, path, table, key),
                MergedArray array => ArrayNode(parent, path, array, key),
                MergedScalar scalar => Scalar(parent, path, scalar, key),
                _ => throw new InvalidOperationException("Unknown TOML value type: " + member.Value.GetType().Name),
            };
        }

        /// <summary>An array row: its key or index, then its item count. Items are rows below it, and a static array's comments are rows too.</summary>
        private DocNode ArrayNode(DocNode parent, NodePath path, MergedArray array, DocPart lead)
        {
            var parts = new[] { lead, SummaryPart(array.Items.Count, lead.Start) };
            var node = new DocNode(NodeKind.TomlArray, path, parent, parts, [], array.Start, array.End, null, null);
            pieces[node] = array.Pieces;
            if (array.IsTableArray)
            {
                for (int i = 0; i < array.Items.Count; i++)
                {
                    var item = (MergedTable)array.Items[i];
                    node.AddChild(Table(node, node.Path.Append(new ItemStep(i)), item, Index(i, item.Start)));
                }
            }
            else
            {
                FillArray(node, array);
            }

            return node;
        }

        /// <summary>The rows of a static array in place: its items, and the comments among them, in source order.</summary>
        private void FillArray(DocNode node, MergedArray array)
        {
            int comments = 0;
            int index = 0;
            foreach (TomlNode entry in array.Syntax!.Entries)
            {
                switch (entry)
                {
                    case TomlComment comment:
                        node.AddChild(Comment(node, comment, comments++));
                        break;
                    case TomlArrayItem item:
                        node.AddChild(Element(node, array.Items[index], index));
                        index++;
                        foreach (TomlNode between in item.BeforeComma)
                        {
                            if (between is TomlComment betweenComment)
                            {
                                node.AddChild(Comment(node, betweenComment, comments++));
                            }
                        }

                        break;
                }
            }
        }

        private DocNode Element(DocNode parent, MergedValue element, int index)
        {
            NodePath path = parent.Path.Append(new ItemStep(index));
            DocPart lead = Index(index, element.Start);
            return element switch
            {
                MergedTable table => Table(parent, path, table, lead),
                MergedArray array => ArrayNode(parent, path, array, lead),
                MergedScalar scalar => Scalar(parent, path, scalar, lead),
                _ => throw new InvalidOperationException("Unknown TOML value type: " + element.GetType().Name),
            };
        }

        private DocNode Scalar(DocNode parent, NodePath path, MergedScalar scalar, DocPart lead)
        {
            (TomlValueKind kind, _) = TomlValues.Canonical(scalar.Value);
            var value = new DocPart(PartKind.Value, TomlValues.Display(scalar.Value), true, false, scalar.Value.Start, scalar.Value.End);
            var node = new DocNode(NodeKind.TomlScalar, path, parent, [lead, value], [], scalar.Start, scalar.End, null, kind);
            pieces[node] = scalar.Pieces;
            return node;
        }

        private DocNode Comment(DocNode parent, TomlComment comment, int index)
        {
            var part = new DocPart(PartKind.Comment, comment.Content, true, false, comment.Start + 1, comment.End);
            var node = new DocNode(
                NodeKind.TomlComment,
                parent.Path.Append(new CommentStep(index)),
                parent,
                [part],
                [],
                comment.Start,
                comment.End,
                null,
                null);
            pieces[node] = new TomlNode[] { comment };
            return node;
        }

        /// <summary>An array index: a zero-width part at the start of the item, shown as the position in the array.</summary>
        private static DocPart Index(int index, int offset)
        {
            return new DocPart(PartKind.Index, index.ToString(CultureInfo.InvariantCulture), false, false, offset, offset);
        }

        /// <summary>A member count: a zero-width part, shown as <c>{n}</c> or <c>[n]</c>.</summary>
        private static DocPart SummaryPart(int count, int position)
        {
            return new DocPart(PartKind.Summary, count.ToString(CultureInfo.InvariantCulture), false, false, position, position);
        }
    }
}
