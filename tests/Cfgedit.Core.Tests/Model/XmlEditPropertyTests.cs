using System.Text;
using Cfgedit.Core;
using Cfgedit.Core.Model;
using Cfgedit.Core.Xml;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// Over every XML fixture except the one with an entity that holds markup: each editable part takes "x" appended (raw parts take
/// it as source), every node is deleted, and every element is duplicated. Each operation either succeeds or is refused with a
/// message that is not an internal error, and a successful part edit changes only its own span. The tally of outcomes per kind is
/// pinned, so that a change in what is refused shows up here.
/// </summary>
public sealed class XmlEditPropertyTests
{
    // The field shadows the Xml namespace of this test assembly, which the static import would otherwise lose to.
    private static readonly IConfigFormat Xml = EditTestSupport.Xml;

    private static readonly string[] FixtureNames =
    [
        "App.config",
        "Sample.csproj",
        "attlist.xml",
        "empty-pair.xml",
        "empty-self-closing.xml",
        "empty-with-space.xml",
        "log4net.xml",
        "mixed.xml",
        "rawentity.xml",
        "webconfig.config",
        "whitespace.xml",
    ];

    public static IEnumerable<object[]> Fixtures()
    {
        foreach (string name in FixtureNames)
        {
            yield return [name, Expected(name)];
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Every_operation_succeeds_or_is_refused_as_the_users_and_the_tally_is_as_recorded(string file, string expectedTally)
    {
        ConfigDocument document = Load(file);
        var tally = new Tally();

        SetEveryPart(document, tally);
        DeleteEveryNode(document, tally);
        DuplicateEveryElement(document, tally);

        Assert.Equal(expectedTally, tally.Summary());
    }

    private static void SetEveryPart(ConfigDocument document, Tally tally)
    {
        XmlDocumentSyntax syntax = XmlDocumentSyntax.Parse(document.Source);
        foreach (DocNode node in ModelTestSupport.AllNodes(document.Root))
        {
            foreach (DocPart part in node.Parts.Concat(node.EndParts))
            {
                if (!part.Editable)
                {
                    continue;
                }

                string value = part.Value + "x";
                var op = new SetPart(node.Path, part.Kind, part.AttributeIndex, value);
                string label = "SetPart " + part.Kind + (part.IsRaw ? " (raw)" : string.Empty);
                try
                {
                    EditResult result = document.Apply(op);
                    tally.Ok(label);
                    AssertOutsideSpanUnchanged(document.Source.Text, result.Document.Source.Text, ExpectationFor(syntax, node, part, value));
                }
                catch (CfgeditException ex)
                {
                    Assert.DoesNotContain("internal", ex.Message, StringComparison.Ordinal);
                    tally.Rejected(label, ex.Message);
                }
            }
        }
    }

    private static void DeleteEveryNode(ConfigDocument document, Tally tally)
    {
        foreach (DocNode node in ModelTestSupport.AllNodes(document.Root))
        {
            if (node.Parent is null)
            {
                continue;
            }

            string label = "DeleteNode " + node.Kind;
            try
            {
                EditResult result = document.Apply(new DeleteNode(node.Path));
                tally.Ok(label);
                Assert.NotSame(document, result.Document);
            }
            catch (CfgeditException ex)
            {
                Assert.DoesNotContain("internal", ex.Message, StringComparison.Ordinal);
                tally.Rejected(label, ex.Message);
            }
        }
    }

    private static void DuplicateEveryElement(ConfigDocument document, Tally tally)
    {
        foreach (DocNode node in ModelTestSupport.AllNodes(document.Root))
        {
            if (node.Kind != NodeKind.XmlElement)
            {
                continue;
            }

            const string label = "DuplicateNode XmlElement";
            try
            {
                EditResult result = document.Apply(new DuplicateNode(node.Path));
                tally.Ok(label);
                Assert.NotSame(document, result.Document);
            }
            catch (CfgeditException ex)
            {
                Assert.DoesNotContain("internal", ex.Message, StringComparison.Ordinal);
                tally.Rejected(label, ex.Message);
            }
        }
    }

    /// <summary>
    /// What the edit must leave unchanged. A name, a raw value, a comment or a PI target is written exactly as given, so the text
    /// is known: its span is replaced and nothing else moves. An element's start and end names change together. Other values
    /// are escaped, so only the part's span is known to change.
    /// </summary>
    private static Expectation ExpectationFor(XmlDocumentSyntax syntax, DocNode node, DocPart part, string value)
    {
        switch (part.Kind)
        {
            case PartKind.ElementName:
            case PartKind.EndName:
            {
                XmlElementSyntax element = FindElement(syntax.Root, node.Start) ?? throw new InvalidOperationException("No element.");
                var edits = new List<(int Start, int End, string NewText)>
                {
                    (element.NameStart, element.NameStart + element.Name.Length, value),
                };
                if (element.EndTag is { } endTag)
                {
                    edits.Add((endTag.NameStart, endTag.NameStart + endTag.Name.Length, value));
                }

                return Expectation.Exact(edits);
            }
            case PartKind.AttributeName:
                return Expectation.Exact([(part.Start, part.End, value)]);
            case PartKind.Comment:
            case PartKind.PITarget:
                return Expectation.Exact([(part.Start, part.End, value)]);
            case PartKind.Text when part.IsRaw:
            case PartKind.AttributeValue when part.IsRaw:
                return Expectation.Exact([(part.Start, part.End, value)]);
            default:
                return Expectation.WithinSpan(part.Start, part.End);
        }
    }

    private static void AssertOutsideSpanUnchanged(string before, string after, Expectation expectation)
    {
        if (expectation.Edits is { } edits)
        {
            Assert.Equal(Splice(before, edits), after);
            return;
        }

        int prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix
            && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
        {
            suffix++;
        }

        int changedEnd = before.Length - suffix;
        Assert.True(
            prefix >= expectation.Start && changedEnd <= expectation.End,
            $"The text changed in [{prefix}, {changedEnd}), outside the part span [{expectation.Start}, {expectation.End}).");
    }

    /// <summary>The text with each replacement made, the replacements given in document order.</summary>
    private static string Splice(string before, IReadOnlyList<(int Start, int End, string NewText)> edits)
    {
        var builder = new StringBuilder();
        int position = 0;
        foreach ((int start, int end, string newText) in edits.OrderBy(e => e.Start))
        {
            builder.Append(before, position, start - position).Append(newText);
            position = end;
        }

        return builder.Append(before, position, before.Length - position).ToString();
    }

    private static XmlElementSyntax? FindElement(XmlElementSyntax element, int start)
    {
        if (element.Start == start)
        {
            return element;
        }

        foreach (XmlNodeSyntax child in element.Children)
        {
            if (child is XmlElementSyntax nested && FindElement(nested, start) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static ConfigDocument Load(string file)
    {
        string path = ModelTestSupport.FixturePath("xml", file);
        return ConfigDocument.Load(File.ReadAllBytes(path), path, Xml);
    }

    /// <summary>
    /// The pinned tally for a fixture. It is a literal, copied from a run of this test; a change in an outcome shows up as a
    /// difference here, and the difference is to be read before the literal is updated.
    /// </summary>
    private static string Expected(string file)
    {
        return file switch
        {
            "App.config" => Lines(
                "DeleteNode XmlDeclaration: 0 ok, 1 refused",
                "    1 x This node cannot be deleted",
                "DeleteNode XmlElement: 9 ok, 1 refused",
                "    1 x The root element cannot be deleted",
                "DuplicateNode XmlElement: 9 ok, 1 refused",
                "    1 x The root element cannot be duplicated",
                "SetPart AttributeName: 11 ok, 0 refused",
                "SetPart AttributeValue: 11 ok, 0 refused",
                "SetPart ElementName: 10 ok, 0 refused",
                "SetPart EndName: 6 ok, 0 refused"),
            "Sample.csproj" => Lines(
                "DeleteNode XmlElement: 5 ok, 1 refused",
                "    1 x The root element cannot be deleted",
                "DuplicateNode XmlElement: 5 ok, 1 refused",
                "    1 x The root element cannot be duplicated",
                "SetPart AttributeName: 3 ok, 0 refused",
                "SetPart AttributeValue: 3 ok, 0 refused",
                "SetPart ElementName: 6 ok, 0 refused",
                "SetPart EndName: 3 ok, 0 refused",
                "SetPart Text: 2 ok, 0 refused"),
            "attlist.xml" => Lines(
                "DeleteNode XmlDeclaration: 0 ok, 1 refused",
                "    1 x This node cannot be deleted",
                "DeleteNode XmlDoctype: 0 ok, 1 refused",
                "    1 x This node cannot be deleted",
                "DeleteNode XmlElement: 0 ok, 1 refused",
                "    1 x The root element cannot be deleted",
                "DuplicateNode XmlElement: 0 ok, 1 refused",
                "    1 x The root element cannot be duplicated",
                "SetPart AttributeName: 1 ok, 0 refused",
                "SetPart AttributeValue: 1 ok, 0 refused",
                "SetPart ElementName: 1 ok, 0 refused"),
            "empty-pair.xml" => Lines(
                "DeleteNode XmlElement: 0 ok, 1 refused",
                "    1 x The root element cannot be deleted",
                "DuplicateNode XmlElement: 0 ok, 1 refused",
                "    1 x The root element cannot be duplicated",
                "SetPart ElementName: 1 ok, 0 refused",
                "SetPart Text: 1 ok, 0 refused"),
            "empty-self-closing.xml" => Lines(
                "DeleteNode XmlElement: 0 ok, 1 refused",
                "    1 x The root element cannot be deleted",
                "DuplicateNode XmlElement: 0 ok, 1 refused",
                "    1 x The root element cannot be duplicated",
                "SetPart ElementName: 1 ok, 0 refused"),
            "empty-with-space.xml" => Lines(
                "DeleteNode XmlElement: 0 ok, 1 refused",
                "    1 x The root element cannot be deleted",
                "DuplicateNode XmlElement: 0 ok, 1 refused",
                "    1 x The root element cannot be duplicated",
                "SetPart ElementName: 1 ok, 0 refused"),
            "log4net.xml" => Lines(
                "DeleteNode XmlDeclaration: 0 ok, 1 refused",
                "    1 x This node cannot be deleted",
                "DeleteNode XmlElement: 7 ok, 1 refused",
                "    1 x The root element cannot be deleted",
                "DuplicateNode XmlElement: 7 ok, 1 refused",
                "    1 x The root element cannot be duplicated",
                "SetPart AttributeName: 7 ok, 0 refused",
                "SetPart AttributeValue: 7 ok, 0 refused",
                "SetPart ElementName: 8 ok, 0 refused",
                "SetPart EndName: 4 ok, 0 refused"),
            "mixed.xml" => Lines(
                "DeleteNode XmlCData: 1 ok, 1 refused",
                "    1 x removing this would join two pieces of text",
                "DeleteNode XmlDeclaration: 0 ok, 1 refused",
                "    1 x This node cannot be deleted",
                "DeleteNode XmlElement: 4 ok, 2 refused",
                "    1 x The root element cannot be deleted",
                "    1 x removing this would join two pieces of text",
                "DeleteNode XmlProcessingInstruction: 1 ok, 1 refused",
                "    1 x removing this would join two pieces of text",
                "DeleteNode XmlText: 4 ok, 0 refused",
                "DuplicateNode XmlElement: 5 ok, 1 refused",
                "    1 x The root element cannot be duplicated",
                "SetPart AttributeName: 2 ok, 2 refused",
                "    1 x the prefix 'doc' is still used in its scope",
                "    1 x the prefix 'x' is still used in its scope",
                "SetPart AttributeValue: 3 ok, 1 refused",
                "    1 x this change would make the document invalid: 'preservex' is an invalid xml:space value.",
                "SetPart ElementName: 6 ok, 0 refused",
                "SetPart EndName: 3 ok, 0 refused",
                "SetPart PIData: 2 ok, 0 refused",
                "SetPart PITarget: 2 ok, 0 refused",
                "SetPart Text: 8 ok, 0 refused"),
            "rawentity.xml" => Lines(
                "DeleteNode XmlDeclaration: 0 ok, 1 refused",
                "    1 x This node cannot be deleted",
                "DeleteNode XmlDoctype: 0 ok, 1 refused",
                "    1 x This node cannot be deleted",
                "DeleteNode XmlElement: 4 ok, 1 refused",
                "    1 x The root element cannot be deleted",
                "DuplicateNode XmlElement: 4 ok, 1 refused",
                "    1 x The root element cannot be duplicated",
                "SetPart AttributeName: 1 ok, 0 refused",
                "SetPart AttributeValue (raw): 1 ok, 0 refused",
                "SetPart ElementName: 5 ok, 0 refused",
                "SetPart EndName: 1 ok, 0 refused",
                "SetPart Text: 1 ok, 0 refused",
                "SetPart Text (raw): 2 ok, 0 refused"),
            "webconfig.config" => Lines(
                "DeleteNode XmlComment: 2 ok, 0 refused",
                "DeleteNode XmlDeclaration: 0 ok, 1 refused",
                "    1 x This node cannot be deleted",
                "DeleteNode XmlElement: 10 ok, 1 refused",
                "    1 x The root element cannot be deleted",
                "DuplicateNode XmlElement: 10 ok, 1 refused",
                "    1 x The root element cannot be duplicated",
                "SetPart AttributeName: 11 ok, 0 refused",
                "SetPart AttributeValue: 11 ok, 0 refused",
                "SetPart Comment: 2 ok, 0 refused",
                "SetPart ElementName: 11 ok, 0 refused",
                "SetPart EndName: 6 ok, 0 refused"),
            "whitespace.xml" => Lines(
                "DeleteNode XmlDeclaration: 0 ok, 1 refused",
                "    1 x This node cannot be deleted",
                "DeleteNode XmlElement: 2 ok, 1 refused",
                "    1 x The root element cannot be deleted",
                "DuplicateNode XmlElement: 2 ok, 1 refused",
                "    1 x The root element cannot be duplicated",
                "SetPart AttributeName: 7 ok, 0 refused",
                "SetPart AttributeValue: 7 ok, 0 refused",
                "SetPart ElementName: 3 ok, 0 refused",
                "SetPart EndName: 1 ok, 0 refused",
                "SetPart Text: 1 ok, 0 refused"),
            _ => throw new ArgumentOutOfRangeException(nameof(file), file, "No pinned tally for this fixture."),
        };
    }

    private static string Lines(params string[] lines)
    {
        return string.Join("\n", lines);
    }

    /// <summary>The span an edit may change, or the exact replacements it makes.</summary>
    private sealed record Expectation(int Start, int End, IReadOnlyList<(int Start, int End, string NewText)>? Edits)
    {
        public static Expectation WithinSpan(int start, int end) => new(start, end, null);

        public static Expectation Exact(IReadOnlyList<(int Start, int End, string NewText)> edits) => new(0, 0, edits);
    }

    /// <summary>Counts the outcomes per kind of operation, and the messages of the refusals.</summary>
    private sealed class Tally
    {
        private readonly SortedDictionary<string, int> _ok = new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, SortedDictionary<string, int>> _refused = new(StringComparer.Ordinal);

        public void Ok(string kind)
        {
            _ok[kind] = _ok.GetValueOrDefault(kind) + 1;
        }

        public void Rejected(string kind, string message)
        {
            if (!_refused.TryGetValue(kind, out SortedDictionary<string, int>? messages))
            {
                messages = new SortedDictionary<string, int>(StringComparer.Ordinal);
                _refused[kind] = messages;
            }

            messages[message] = messages.GetValueOrDefault(message) + 1;
        }

        public string Summary()
        {
            var lines = new List<string>();
            foreach (string kind in _ok.Keys.Union(_refused.Keys).Order(StringComparer.Ordinal))
            {
                int ok = _ok.GetValueOrDefault(kind);
                int refused = _refused.TryGetValue(kind, out SortedDictionary<string, int>? messages) ? messages.Values.Sum() : 0;
                lines.Add($"{kind}: {ok} ok, {refused} refused");
                if (messages is not null)
                {
                    foreach ((string message, int count) in messages)
                    {
                        lines.Add($"    {count} x {message}");
                    }
                }
            }

            return string.Join("\n", lines);
        }
    }
}
