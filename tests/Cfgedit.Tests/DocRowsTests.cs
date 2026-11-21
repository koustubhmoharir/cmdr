using System.Text;
using Cfgedit.Core.Model;
using Cfgedit.Tui;

namespace Cfgedit.Tests;

public sealed class DocRowsTests
{
    [Fact]
    public void Visible_lists_children_in_order_with_end_rows_after_their_children()
    {
        ConfigDocument doc = TreeFixtures.Parse("<a><b><c/></b><d/></a>", "xml");

        IReadOnlyList<Row> rows = DocRows.Visible(doc, new HashSet<NodePath>());

        Assert.Equal(
            new[]
            {
                ("/Element(\"a\", 0)", false, 0),
                ("/Element(\"a\", 0)/Element(\"b\", 0)", false, 1),
                ("/Element(\"a\", 0)/Element(\"b\", 0)/Element(\"c\", 0)", false, 2),
                ("/Element(\"a\", 0)/Element(\"b\", 0)", true, 1),
                ("/Element(\"a\", 0)/Element(\"d\", 0)", false, 1),
                ("/Element(\"a\", 0)", true, 0),
            },
            rows.Select(row => (row.Node.Path.ToString(), row.IsEnd, row.Depth)));
    }

    [Fact]
    public void Collapsed_element_hides_its_children_and_its_end_row()
    {
        ConfigDocument doc = TreeFixtures.Parse("<a><b><c/></b><d/></a>", "xml");
        var collapsed = new HashSet<NodePath> { new NodePath([new ElementStep("a", 0), new ElementStep("b", 0)]) };

        IReadOnlyList<Row> rows = DocRows.Visible(doc, collapsed);

        Assert.Equal(
            new[]
            {
                ("/Element(\"a\", 0)", false, 0),
                ("/Element(\"a\", 0)/Element(\"b\", 0)", false, 1),
                ("/Element(\"a\", 0)/Element(\"d\", 0)", false, 1),
                ("/Element(\"a\", 0)", true, 0),
            },
            rows.Select(row => (row.Node.Path.ToString(), row.IsEnd, row.Depth)));
    }

    [Fact]
    public void Initial_collapses_collapsible_nodes_below_depth_three_only()
    {
        ConfigDocument doc = TreeFixtures.Parse("<a><b><c><d><e/></d></c></b></a>", "xml");

        IReadOnlySet<NodePath> initial = DocRows.Initial(doc);

        Assert.Equal(
            new[] { "/Element(\"a\", 0)/Element(\"b\", 0)/Element(\"c\", 0)/Element(\"d\", 0)" },
            initial.Select(path => path.ToString()));
    }

    [Fact]
    public void Initial_collapses_a_node_with_more_than_200_children()
    {
        string over = Wide(201);
        string at = Wide(200);

        IReadOnlySet<NodePath> collapsedOver = DocRows.Initial(TreeFixtures.Parse(over, "xml"));
        IReadOnlySet<NodePath> collapsedAt = DocRows.Initial(TreeFixtures.Parse(at, "xml"));

        Assert.Equal(new[] { "/Element(\"r\", 0)" }, collapsedOver.Select(path => path.ToString()));
        Assert.Empty(collapsedAt);
    }

    [Fact]
    public void Initial_collapses_nested_json_arrays_at_depth_four()
    {
        ConfigDocument doc = TreeFixtures.Parse("[[[[1]]]]", "json");

        IReadOnlySet<NodePath> initial = DocRows.Initial(doc);

        Assert.Equal(new[] { "/Item(0)/Item(0)/Item(0)/Item(0)" }, initial.Select(path => path.ToString()));
    }

    [Fact]
    public void Retain_drops_the_paths_that_an_edit_removed()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"a\": {\"x\": 1}, \"b\": {\"y\": 2}}", "json");
        var collapsed = new HashSet<NodePath>
        {
            new NodePath([new ItemStep(0), new MemberStep("a", 0)]),
            new NodePath([new ItemStep(0), new MemberStep("b", 0)]),
        };
        ConfigDocument after = doc.Apply(new DeleteNode(new NodePath([new ItemStep(0), new MemberStep("a", 0)]))).Document;

        IReadOnlySet<NodePath> retained = DocRows.Retain(after, collapsed);

        Assert.Equal(new[] { "/Item(0)/Member(\"b\", 0)" }, retained.Select(path => path.ToString()));
    }

    [Fact]
    public void Retain_keeps_the_document_path()
    {
        ConfigDocument doc = TreeFixtures.Parse("{}", "json");
        var collapsed = new HashSet<NodePath> { NodePath.Empty };

        IReadOnlySet<NodePath> retained = DocRows.Retain(doc, collapsed);

        Assert.Equal(new[] { "/" }, retained.Select(path => path.ToString()));
    }

    [Fact]
    public void Empty_json_object_has_no_collapsed_nodes_and_one_row()
    {
        ConfigDocument doc = TreeFixtures.Parse("{}", "json");

        Assert.Empty(DocRows.Initial(doc));
        IReadOnlyList<Row> rows = DocRows.Visible(doc, DocRows.Initial(doc));
        Assert.Equal(new[] { ("/Item(0)", false, 0) }, rows.Select(row => (row.Node.Path.ToString(), row.IsEnd, row.Depth)));
    }

    [Fact]
    public void Json_root_comments_are_rows_at_the_top_level()
    {
        ConfigDocument doc = TreeFixtures.Parse("// c\n{\"a\": 1}", "jsonc");

        IReadOnlyList<Row> rows = DocRows.Visible(doc, new HashSet<NodePath>());

        Assert.Equal(
            new[] { ("/Comment(0)", false, 0), ("/Item(0)", false, 0), ("/Item(0)/Member(\"a\", 0)", false, 1) },
            rows.Select(row => (row.Node.Path.ToString(), row.IsEnd, row.Depth)));
    }

    [Fact]
    public void Xml_declaration_is_a_top_level_row()
    {
        ConfigDocument doc = TreeFixtures.Parse("<?xml version=\"1.0\"?><r/>", "xml");

        IReadOnlyList<Row> rows = DocRows.Visible(doc, new HashSet<NodePath>());

        Assert.Equal(
            new[] { ("/Declaration", false, 0), ("/Element(\"r\", 0)", false, 0) },
            rows.Select(row => (row.Node.Path.ToString(), row.IsEnd, row.Depth)));
    }

    [Fact]
    public void Collapsibility_follows_children_and_excludes_the_document_node()
    {
        Assert.False(DocRows.IsCollapsible(TreeFixtures.Parse("<a/>", "xml").Root));
        Assert.False(DocRows.IsCollapsible(TreeFixtures.Find(TreeFixtures.Parse("<a/>", "xml"), new ElementStep("a", 0))));
        Assert.False(DocRows.IsCollapsible(TreeFixtures.Find(TreeFixtures.Parse("<a>x</a>", "xml"), new ElementStep("a", 0))));
        Assert.True(DocRows.IsCollapsible(TreeFixtures.Find(TreeFixtures.Parse("<a><b/></a>", "xml"), new ElementStep("a", 0))));
        Assert.False(DocRows.IsCollapsible(TreeFixtures.Find(TreeFixtures.Parse("[]", "json"), new ItemStep(0))));
        Assert.True(DocRows.IsCollapsible(TreeFixtures.Find(TreeFixtures.Parse("[1]", "json"), new ItemStep(0))));
    }

    private static string Wide(int children)
    {
        var builder = new StringBuilder("<r>");
        for (int i = 0; i < children; i++)
        {
            builder.Append("<i/>");
        }

        return builder.Append("</r>").ToString();
    }
}
