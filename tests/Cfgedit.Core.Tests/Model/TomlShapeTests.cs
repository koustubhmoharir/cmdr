using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// ConfigDocument.ShapeLike on TOML: an array-of-tables item and an inline table of an inline array get the sibling's keys with empty
/// values (a date-time keeps its value, and subtables are not copied); a scalar or an array of an inline array gets one empty value.
/// </summary>
public sealed class TomlShapeTests
{
    private static readonly IConfigFormat Toml = EditTestSupport.Toml;

    private static NodePath Path(params PathStep[] steps) => At(new PathStep[] { Item(0) }.Concat(steps).ToArray());

    private static string Apply(ConfigDocument doc, ShapedInsert shaped) => doc.Apply(shaped.Edit).Document.Source.Text;

    [Fact]
    public void An_array_of_tables_item_is_copied_as_a_section_with_its_keys_and_empty_values()
    {
        const string Text = "[[x]]\na = 1\nb = \"s\"\nc = true\nd = 1.5\ne = 1979-05-27\n[x.sub]\nq = 1\n";
        ConfigDocument doc = ModelTestSupport.Parse(Text, Toml);

        ShapedInsert shaped = doc.ShapeLike(Path(Member("x"), Item(0)), Path(Member("x")), 1);

        Assert.Equal(
            Text + "[[x]]\na = 0\nb = \"\"\nc = false\nd = 0.0\ne = 1979-05-27\n",
            Apply(doc, shaped));
        Assert.Equal(Path(Member("x"), Item(1)), shaped.Node);
    }

    [Fact]
    public void An_inline_table_item_of_an_inline_array_is_copied_inline_with_empty_values()
    {
        ConfigDocument doc = ModelTestSupport.Parse("a = [{ x = 1, s = \"t\" }]\n", Toml);

        ShapedInsert shaped = doc.ShapeLike(Path(Member("a"), Item(0)), Path(Member("a")), 1);

        Assert.Equal("a = [{ x = 1, s = \"t\" }, {x = 0, s = \"\"}]\n", Apply(doc, shaped));
    }

    [Fact]
    public void A_scalar_of_an_inline_array_is_one_empty_value_of_its_kind()
    {
        ConfigDocument doc = ModelTestSupport.Parse("a = [1, 2]\n", Toml);

        Assert.Equal("a = [1, 2, 0]\n", Apply(doc, doc.ShapeLike(Path(Member("a"), Item(0)), Path(Member("a")), 2)));
    }

    [Fact]
    public void A_key_value_has_no_shape_to_copy_into_a_table()
    {
        ConfigDocument doc = ModelTestSupport.Parse("a = 1\nb = 2\n", Toml);

        ArgumentException error = Assert.Throws<ArgumentException>(() => doc.ShapeLike(Path(Member("a")), Path(), 2));

        Assert.Contains("cannot be copied into this place", error.Message);
    }
}
