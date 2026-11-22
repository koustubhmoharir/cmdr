using System.Text;
using Cfgedit.Core;
using Cfgedit.Core.Model;
using Cfgedit.Core.Text;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// The TOML format as a format: its name, its refusal of every edit until the planner exists, its user-error rule, the logical tree
/// with the comments as an array, and the byte-order mark.
/// </summary>
public sealed class TomlFormatTests
{
    private static readonly TomlFormat Format = new();

    [Fact]
    public void Its_name_is_toml()
    {
        Assert.Equal("toml", Format.Name);
    }

    [Fact]
    public void Every_edit_is_refused_until_the_planner_exists()
    {
        ConfigDocument document = Format.Parse(SourceText.Create("a = 1\n", Encoding.UTF8, false));

        CfgeditException ex = Assert.Throws<CfgeditException>(() => Format.Plan(document, new DeleteNode(NodePath.Empty.Append(new ItemStep(0)))));

        Assert.Equal("editing TOML files is not supported yet", ex.Message);
    }

    [Theory]
    [InlineData("duplicate key 'a'")]
    [InlineData("expected '=' after the key but found end of line")]
    [InlineData("table 'a' is already defined")]
    public void Syntax_and_semantic_errors_of_an_edited_text_are_the_users(string message)
    {
        Assert.True(Format.IsUserError(new CfgeditException(message)));
    }

    [Fact]
    public void An_internal_parse_mismatch_is_cfgeditts_own_error_and_not_the_users()
    {
        Assert.False(Format.IsUserError(new CfgeditException("internal parse mismatch: at a: cfgedit has Integer \"1\" (please report this)")));
    }

    [Fact]
    public void The_logical_tree_is_the_data_then_an_array_of_the_comments_in_file_order()
    {
        ConfigDocument document = Format.Parse(SourceText.Create("# c\na = 1 # t\nb = [ # in\n  2,\n]\n", Encoding.UTF8, false));

        string expected = """
            Document
              Object
                Scalar name="a" toml=Integer value="1"
                Array name="b"
                  Scalar toml=Integer value="2"
              Array name="#comments"
                Comment value=" c"
                Comment value=" t"
                Comment value=" in"
            """;
        Assert.Equal(expected, Format.ToLogical(document).ToString());
    }

    [Fact]
    public void A_byte_order_mark_is_kept_and_the_text_parses_after_it()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("a = 1\n")];

        ConfigDocument document = ConfigDocument.Load(bytes, "settings.toml", Format);

        Assert.True(document.Source.HasBom);
        Assert.Equal(bytes, document.Source.Encode());
        Assert.Equal(NodeKind.Document, document.Root.Kind);
        Assert.Equal("a", Assert.Single(Format.ToLogical(document).Children[0].Children).Name);
    }

    [Fact]
    public void A_file_loaded_by_its_name_gets_the_toml_format()
    {
        IConfigFormat? format = Formats.ForPath("settings.toml");

        Assert.IsType<TomlFormat>(format);
    }
}
