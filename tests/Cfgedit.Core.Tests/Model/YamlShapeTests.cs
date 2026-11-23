using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// ConfigDocument.ShapeLike on YAML: an item of a sequence of mappings gets the sibling's scalar and collection keys with empty values (aliases,
/// anchors and tags are not copied); a scalar item gets one empty value of its kind; a flow item stays flow. The shaped insert is a composite
/// (the item, then its members), so each step is written by the planner's own rules.
/// </summary>
public sealed class YamlShapeTests
{
    private static readonly IConfigFormat Yaml = EditTestSupport.Yaml;

    private static NodePath Path(params PathStep[] steps) => At(new PathStep[] { Item(0) }.Concat(steps).ToArray());

    private static string Apply(ConfigDocument doc, ShapedInsert shaped) => doc.Apply(shaped.Edit).Document.Source.Text;

    [Fact]
    public void A_mapping_item_of_a_services_list_is_copied_with_empty_values_and_empty_collections()
    {
        const string Text = "services:\n  - name: web\n    image: nginx\n    ports:\n      - 80\n  - name: db\n    image: pg\n";
        ConfigDocument doc = ModelTestSupport.Parse(Text, Yaml);

        ShapedInsert shaped = doc.ShapeLike(Path(Member("services"), Item(0)), Path(Member("services")), 2);

        // The empty mapping item becomes a compact block mapping: its first key follows the dash, and the other members align with it.
        Assert.Equal(
            Text + "  - name: ''\n    image: ''\n    ports: []\n",
            Apply(doc, shaped));
        Assert.Equal(Path(Member("services"), Item(2)), shaped.Node);
    }

    [Fact]
    public void Aliases_anchors_and_tags_of_the_sibling_are_not_copied()
    {
        const string Text = "list:\n  - &base {a: 1}\n  - b: !!str 2\n    c: *base\n    d: &v x\n";
        ConfigDocument doc = ModelTestSupport.Parse(Text, Yaml);

        ShapedInsert shaped = doc.ShapeLike(Path(Member("list"), Item(1)), Path(Member("list")), 2);

        Assert.Equal(Text + "  - b: ''\n    d: ''\n", Apply(doc, shaped));
    }

    [Fact]
    public void A_scalar_item_is_one_empty_value_of_its_kind()
    {
        const string Text = "xs:\n  - a\n  - 1\n";
        ConfigDocument doc = ModelTestSupport.Parse(Text, Yaml);

        Assert.Equal("xs:\n  - a\n  - 1\n  - ''\n", Apply(doc, doc.ShapeLike(Path(Member("xs"), Item(0)), Path(Member("xs")), 2)));
        Assert.Equal("xs:\n  - a\n  - 1\n  - 0\n", Apply(doc, doc.ShapeLike(Path(Member("xs"), Item(1)), Path(Member("xs")), 2)));
    }

    [Fact]
    public void A_float_and_a_boolean_item_keep_their_kinds_with_empty_values()
    {
        const string Text = "xs:\n  - 2.5\n  - true\n";
        ConfigDocument doc = ModelTestSupport.Parse(Text, Yaml);

        Assert.Equal("xs:\n  - 2.5\n  - true\n  - 0.0\n", Apply(doc, doc.ShapeLike(Path(Member("xs"), Item(0)), Path(Member("xs")), 2)));
        Assert.Equal("xs:\n  - 2.5\n  - true\n  - false\n", Apply(doc, doc.ShapeLike(Path(Member("xs"), Item(1)), Path(Member("xs")), 2)));
    }

    [Fact]
    public void A_mapping_item_of_a_flow_sequence_stays_flow()
    {
        ConfigDocument doc = ModelTestSupport.Parse("[{a: 1, b: x}]\n", Yaml);

        ShapedInsert shaped = doc.ShapeLike(Path(Item(0)), Path(), 1);

        Assert.Equal("[{a: 1, b: x}, {a: 0, b: ''}]\n", Apply(doc, shaped));
    }

    [Fact]
    public void A_member_has_a_key_so_its_shape_cannot_be_copied_into_a_sequence()
    {
        ConfigDocument doc = ModelTestSupport.Parse("a: 1\nb: 2\n", Yaml);

        ArgumentException error = Assert.Throws<ArgumentException>(() => doc.ShapeLike(Path(Member("a")), Path(), 2));

        Assert.Contains("A YAML mapping entry has a key", error.Message);
    }
}
