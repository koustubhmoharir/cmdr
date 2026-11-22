using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>Change type on TOML: a scalar to each kind, containers to scalars and back, and the tables that cannot change type.</summary>
public sealed class TomlEditTypeTests
{
    private static readonly IConfigFormat Toml = EditTestSupport.Toml;

    private static NodePath Path(params PathStep[] steps) => At(new PathStep[] { Item(0) }.Concat(steps).ToArray());

    // ----- Scalars -----

    [Fact]
    public void A_scalar_becomes_a_string()
    {
        string text = Edit("a = 1\n", Toml, new ChangeTomlType(Path(Member("a")), TomlNewKind.String, "x"));

        Assert.Equal("a = \"x\"\n", text);
    }

    [Fact]
    public void A_string_without_a_value_becomes_the_empty_string()
    {
        string text = Edit("a = 1\n", Toml, new ChangeTomlType(Path(Member("a")), TomlNewKind.String, null));

        Assert.Equal("a = \"\"\n", text);
    }

    [Fact]
    public void A_string_becomes_an_integer_zero_without_a_value()
    {
        string text = Edit("a = \"x\"\n", Toml, new ChangeTomlType(Path(Member("a")), TomlNewKind.Integer, null));

        Assert.Equal("a = 0\n", text);
    }

    [Fact]
    public void A_string_becomes_a_float_zero_without_a_value()
    {
        string text = Edit("a = \"x\"\n", Toml, new ChangeTomlType(Path(Member("a")), TomlNewKind.Float, null));

        Assert.Equal("a = 0.0\n", text);
    }

    [Fact]
    public void A_string_becomes_false_without_a_value()
    {
        string text = Edit("a = \"x\"\n", Toml, new ChangeTomlType(Path(Member("a")), TomlNewKind.Boolean, null));

        Assert.Equal("a = false\n", text);
    }

    [Fact]
    public void A_string_becomes_a_local_date_with_the_given_text()
    {
        string text = Edit("a = \"x\"\n", Toml, new ChangeTomlType(Path(Member("a")), TomlNewKind.DateTime, "1979-05-27"));

        Assert.Equal("a = 1979-05-27\n", text);
    }

    [Fact]
    public void A_date_time_without_a_value_is_refused()
    {
        CfgeditException error = Fails("a = \"x\"\n", Toml, new ChangeTomlType(Path(Member("a")), TomlNewKind.DateTime, null));

        Assert.Equal("a date-time needs a value", error.Message);
    }

    [Fact]
    public void A_scalar_becomes_an_empty_inline_table()
    {
        string text = Edit("a = 1\n", Toml, new ChangeTomlType(Path(Member("a")), TomlNewKind.Table, null));

        Assert.Equal("a = {}\n", text);
    }

    [Fact]
    public void A_scalar_becomes_an_empty_inline_array()
    {
        string text = Edit("a = 1\n", Toml, new ChangeTomlType(Path(Member("a")), TomlNewKind.Array, null));

        Assert.Equal("a = []\n", text);
    }

    [Fact]
    public void A_scalar_of_the_same_kind_with_no_value_does_not_change()
    {
        string text = Edit("a = 1\n", Toml, new ChangeTomlType(Path(Member("a")), TomlNewKind.Integer, null));

        Assert.Equal("a = 1\n", text);
    }

    [Fact]
    public void An_invalid_integer_is_refused()
    {
        CfgeditException error = Fails("a = 1\n", Toml, new ChangeTomlType(Path(Member("a")), TomlNewKind.Integer, "1.5"));

        Assert.Equal("'1.5' is not a valid integer", error.Message);
    }

    [Fact]
    public void An_invalid_float_is_refused()
    {
        CfgeditException error = Fails("a = 1\n", Toml, new ChangeTomlType(Path(Member("a")), TomlNewKind.Float, "x"));

        Assert.Equal("'x' is not a valid float", error.Message);
    }

    [Fact]
    public void A_boolean_other_than_true_or_false_is_refused()
    {
        CfgeditException error = Fails("a = 1\n", Toml, new ChangeTomlType(Path(Member("a")), TomlNewKind.Boolean, "yes"));

        Assert.Equal("'yes' is not a valid boolean (true or false)", error.Message);
    }

    [Fact]
    public void An_invalid_date_time_is_refused()
    {
        CfgeditException error = Fails("a = 1\n", Toml, new ChangeTomlType(Path(Member("a")), TomlNewKind.DateTime, "nope"));

        Assert.Equal("'nope' is not a valid date-time", error.Message);
    }

    [Fact]
    public void The_type_change_keeps_the_path_of_the_node_as_focus()
    {
        (string text, NodePath? focus) = EditWithFocus("a = 1\n", Toml, new ChangeTomlType(Path(Member("a")), TomlNewKind.String, "x"));

        Assert.Equal("a = \"x\"\n", text);
        Assert.Equal(Path(Member("a")), focus);
    }

    // ----- Containers -----

    [Fact]
    public void An_inline_table_becomes_a_string_and_its_members_go()
    {
        string text = Edit("t = { x = 1 }\n", Toml, new ChangeTomlType(Path(Member("t")), TomlNewKind.String, "s"));

        Assert.Equal("t = \"s\"\n", text);
    }

    [Fact]
    public void An_inline_array_becomes_an_empty_inline_table()
    {
        string text = Edit("a = [1, 2]\n", Toml, new ChangeTomlType(Path(Member("a")), TomlNewKind.Table, null));

        Assert.Equal("a = {}\n", text);
    }

    [Fact]
    public void An_inline_array_with_comments_becomes_a_string_and_the_comments_go_with_it()
    {
        string text = Edit("a = [\n  1, # c\n  2\n]\nb = 1\n", Toml, new ChangeTomlType(Path(Member("a")), TomlNewKind.String, "s"));

        Assert.Equal("a = \"s\"\nb = 1\n", text);
    }

    [Fact]
    public void An_item_of_an_inline_array_changes_type_in_place()
    {
        string text = Edit("a = [1, 2]\n", Toml, new ChangeTomlType(Path(Member("a"), Item(0)), TomlNewKind.String, "s"));

        Assert.Equal("a = [\"s\", 2]\n", text);
    }

    [Fact]
    public void A_table_defined_by_a_header_cannot_change_type()
    {
        CfgeditException error = Fails("[a]\nx = 1\n", Toml, new ChangeTomlType(Path(Member("a")), TomlNewKind.Integer, null));

        Assert.Equal("a table defined with [headers] or dotted keys cannot change type; delete it instead", error.Message);
    }

    [Fact]
    public void A_table_defined_by_dotted_keys_cannot_change_type()
    {
        CfgeditException error = Fails("a.b = 1\n", Toml, new ChangeTomlType(Path(Member("a")), TomlNewKind.Integer, null));

        Assert.Equal("a table defined with [headers] or dotted keys cannot change type; delete it instead", error.Message);
    }

    [Fact]
    public void An_array_of_tables_cannot_change_type()
    {
        CfgeditException error = Fails("[[a]]\nx = 1\n", Toml, new ChangeTomlType(Path(Member("a")), TomlNewKind.Table, null));

        Assert.Equal("an array of tables cannot change type; delete it instead", error.Message);
    }

    [Fact]
    public void The_root_table_cannot_change_type()
    {
        CfgeditException error = Fails("a = 1\n", Toml, new ChangeTomlType(Path(), TomlNewKind.Array, null));

        Assert.Equal("the root table cannot change type", error.Message);
    }
}
