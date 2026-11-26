using System.Text;
using Cfgedit.Core;
using Cfgedit.Core.Model;
using Shared.Core;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// The YAML format as a format: its name, the planner's refusal until editing lands, its user-error rule, and the logical tree of several
/// documents and of an explicit empty document.
/// </summary>
public sealed class YamlFormatTests
{
    private static readonly YamlFormat Format = new();

    [Fact]
    public void Its_name_is_yaml()
    {
        Assert.Equal("yaml", Format.Name);
    }

    [Fact]
    public void A_block_entry_moves_through_the_format_plan()
    {
        ConfigDocument document = Format.Parse(SourceText.Create("a: 1\nb: 2\n", Encoding.UTF8, false));
        NodePath root = NodePath.Empty.Append(new ItemStep(0));

        IReadOnlyList<TextEdit> edits = Format.Plan(document, new MoveNode(root.Append(new MemberStep("a", 0)), 2));

        Assert.Equal("b: 2\na: 1\n", document.Apply(new MoveNode(root.Append(new MemberStep("a", 0)), 2)).Document.Source.Text);
        Assert.NotEmpty(edits);
    }

    [Theory]
    [InlineData("duplicate key 'a'")]
    [InlineData("expected ':' after the key but found end of line")]
    public void Syntax_and_semantic_errors_of_an_edited_text_are_the_users(string message)
    {
        Assert.True(Format.IsUserError(new CfgeditException(message)));
    }

    [Fact]
    public void A_YAMLDotNet_disagreement_is_cfgeditS_bug_and_not_the_users()
    {
        Assert.False(Format.IsUserError(new CfgeditException("internal parse mismatch: at a: differs (please report this)")));
    }

    [Fact]
    public void Several_documents_are_an_array_of_their_roots_in_the_logical_tree()
    {
        ConfigDocument document = Format.Parse(SourceText.Create("a: 1\n---\nb: 2\n", Encoding.UTF8, false));

        Assert.Equal(
            """
            Document
              Array
                Object
                  Scalar name="a" yaml=Integer plain value="1"
                Object
                  Scalar name="b" yaml=Integer plain value="2"
              Array name="#comments"
            """,
            Format.ToLogical(document).ToString());
    }

    [Fact]
    public void An_explicit_empty_document_is_a_null_scalar_root()
    {
        ConfigDocument document = Format.Parse(SourceText.Create("---\n", Encoding.UTF8, false));

        Assert.Equal(
            """
            Document
              Scalar yaml=Null plain value=""
              Array name="#comments"
            """,
            Format.ToLogical(document).ToString());
    }
}
