using Cfgedit.Core.Model;

namespace Cfgedit.Core.Tests.Model;

/// <summary>Paths: their text form, equality, resolution, the nearest node, and the round trip over every fixture.</summary>
public sealed class NodePathTests
{
    public static IEnumerable<object[]> Fixtures()
    {
        // Each JSON fixture with the dialect it is written in. markupentity.xml is rejected and is covered elsewhere.
        yield return ["json", "appsettings.jsonc", "jsonc"];
        yield return ["json", "tsconfig.jsonc", "jsonc"];
        yield return ["json", "example.json5", "json5"];
        yield return ["json", "strict.json", "json"];
        yield return ["json", "lines.json", "json"];
        yield return ["xml", "App.config", "xml"];
        yield return ["xml", "Sample.csproj", "xml"];
        yield return ["xml", "attlist.xml", "xml"];
        yield return ["xml", "empty-pair.xml", "xml"];
        yield return ["xml", "empty-self-closing.xml", "xml"];
        yield return ["xml", "empty-with-space.xml", "xml"];
        yield return ["xml", "log4net.xml", "xml"];
        yield return ["xml", "mixed.xml", "xml"];
        yield return ["xml", "rawentity.xml", "xml"];
        yield return ["xml", "webconfig.config", "xml"];
        yield return ["xml", "whitespace.xml", "xml"];
    }

    // ----- Text form and value equality -----

    [Fact]
    public void The_document_path_is_a_slash()
    {
        Assert.Equal("/", NodePath.Empty.ToString());
        Assert.Equal(0, NodePath.Empty.Count);
    }

    [Fact]
    public void Steps_print_in_their_literal_forms()
    {
        var path = new NodePath([new MemberStep("servers", 0), new ItemStep(1), new MemberStep("a\"b", 2)]);

        Assert.Equal("""/Member("servers", 0)/Item(1)/Member("a\"b", 2)""", path.ToString());
    }

    [Fact]
    public void Every_step_kind_has_its_literal_form()
    {
        var path = new NodePath(
        [
            new ElementStep("r:x", 1),
            new TextStep(2),
            new CommentStep(0),
            new PIStep(3),
            new DeclarationStep(),
            new DoctypeStep(),
        ]);

        Assert.Equal("""/Element("r:x", 1)/Text(2)/Comment(0)/PI(3)/Declaration/Doctype""", path.ToString());
    }

    [Fact]
    public void A_key_with_a_line_break_prints_on_one_line()
    {
        var path = new NodePath([new MemberStep("a\nb\\c", 0)]);

        Assert.Equal("""/Member("a\nb\\c", 0)""", path.ToString());
    }

    [Fact]
    public void Paths_with_equal_steps_are_equal_and_hash_alike()
    {
        var first = NodePath.Empty.Append(new MemberStep("a", 0)).Append(new ItemStep(2));
        var second = new NodePath([new MemberStep("a", 0), new ItemStep(2)]);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Paths_differing_in_one_step_are_not_equal()
    {
        var path = new NodePath([new MemberStep("a", 0), new ItemStep(2)]);

        Assert.NotEqual(path, new NodePath([new MemberStep("a", 1), new ItemStep(2)]));
        Assert.NotEqual(path, new NodePath([new MemberStep("b", 0), new ItemStep(2)]));
        Assert.NotEqual(path, new NodePath([new MemberStep("a", 0), new ItemStep(3)]));
        Assert.NotEqual(path, new NodePath([new MemberStep("a", 0)]));
        Assert.NotEqual(new NodePath([new ItemStep(0)]), new NodePath([new CommentStep(0)]));
        Assert.False(path.Equals(null));
    }

    [Fact]
    public void Prefix_and_append_build_new_paths_and_leave_the_original_unchanged()
    {
        var path = new NodePath([new MemberStep("a", 0), new ItemStep(2), new CommentStep(1)]);

        Assert.Equal("/Member(\"a\", 0)/Item(2)", path.Prefix(2).ToString());
        Assert.Equal(NodePath.Empty, path.Prefix(0));
        Assert.Equal(3, path.Count);
        Assert.Equal("/Member(\"a\", 0)/Item(2)/Comment(1)/Item(9)", path.Append(new ItemStep(9)).ToString());
        Assert.Equal(3, path.Count);
    }

    [Fact]
    public void Steps_are_read_in_order_from_the_document_node()
    {
        var path = new NodePath([new ElementStep("r", 0), new ElementStep("a", 1)]);

        Assert.Equal(new ElementStep("r", 0), path.Steps[0]);
        Assert.Equal(new ElementStep("a", 1), path.Steps[1]);
    }

    // ----- Round trip over every fixture -----

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Every_node_resolves_from_its_own_path(string kind, string name, string formatName)
    {
        ConfigDocument document = LoadFixture(kind, name, formatName);

        int count = 0;
        foreach (DocNode node in ModelTestSupport.AllNodes(document.Root))
        {
            count++;
            Assert.Same(node, document.Resolve(node.Path));
            Assert.Same(node, document.ResolveNearest(node.Path));
            Assert.Equal(DepthOf(node), node.Path.Count);
        }

        Assert.True(count > 1, "the fixture has nodes below the document node");
    }

    [Fact]
    public void The_document_node_resolves_from_the_empty_path()
    {
        ConfigDocument document = ModelTestSupport.Parse("[1]", Formats.FromName("json")!);

        Assert.Same(document.Root, document.Resolve(NodePath.Empty));
        // In [1] the root value is Item(0) and its item is Item(0), so the third step is the first missing one.
        Assert.Null(document.Resolve(new NodePath([new ItemStep(0), new ItemStep(0), new ItemStep(0)])));
    }

    // ----- Nearest node -----

    [Fact]
    public void A_removed_leaf_item_resolves_to_the_nearest_remaining_item()
    {
        // Two items remain. Item(2) is one past the end, so the nearest index is 1.
        ConfigDocument document = ModelTestSupport.Parse("{\"a\": [1, 2]}", Formats.FromName("json")!);

        DocNode nearest = document.ResolveNearest(new NodePath([new ItemStep(0), new MemberStep("a", 0), new ItemStep(2)]));

        Assert.Equal("/Item(0)/Member(\"a\", 0)/Item(1)", nearest.Path.ToString());
        Assert.Null(document.Resolve(new NodePath([new ItemStep(0), new MemberStep("a", 0), new ItemStep(2)])));
    }

    [Fact]
    public void A_removed_middle_resolves_to_the_nearest_sibling_and_drops_the_steps_below_it()
    {
        // Item(5) is missing in the middle of the path. Item(1) is nearer than Item(0), so it is the result.
        ConfigDocument document = ModelTestSupport.Parse("{\"a\": [1, 2]}", Formats.FromName("json")!);

        DocNode nearest = document.ResolveNearest(
            new NodePath([new ItemStep(0), new MemberStep("a", 0), new ItemStep(5), new MemberStep("x", 0)]));

        Assert.Equal("/Item(0)/Member(\"a\", 0)/Item(1)", nearest.Path.ToString());
    }

    [Fact]
    public void A_missing_ordinal_resolves_to_the_same_name_with_the_nearest_ordinal()
    {
        ConfigDocument document = ModelTestSupport.Parse("{\"a\": 1, \"a\": 2}", Formats.FromName("json")!);

        DocNode nearest = document.ResolveNearest(new NodePath([new ItemStep(0), new MemberStep("a", 7)]));

        Assert.Equal("/Item(0)/Member(\"a\", 1)", nearest.Path.ToString());
    }

    [Fact]
    public void A_missing_name_with_no_same_name_sibling_resolves_to_the_container()
    {
        ConfigDocument document = ModelTestSupport.Parse("{\"a\": {\"x\": 1}}", Formats.FromName("json")!);

        DocNode nearest = document.ResolveNearest(
            new NodePath([new ItemStep(0), new MemberStep("q", 0), new MemberStep("x", 0)]));

        Assert.Equal("/Item(0)", nearest.Path.ToString());
    }

    [Fact]
    public void A_missing_element_resolves_to_its_parent_when_no_element_of_that_name_exists()
    {
        ConfigDocument document = ModelTestSupport.Parse("<r><a/></r>", Formats.FromName("xml")!);

        DocNode nearest = document.ResolveNearest(new NodePath([new ElementStep("r", 0), new ElementStep("b", 0)]));

        Assert.Equal("/Element(\"r\", 0)", nearest.Path.ToString());
    }

    [Fact]
    public void A_missing_declaration_resolves_to_the_document_node_when_there_is_none()
    {
        ConfigDocument document = ModelTestSupport.Parse("[1]", Formats.FromName("json")!);

        Assert.Null(document.Resolve(new NodePath([new DeclarationStep()])));
        Assert.Same(document.Root, document.ResolveNearest(new NodePath([new DeclarationStep()])));
    }

    [Fact]
    public void A_path_past_a_leaf_resolves_to_the_leaf()
    {
        ConfigDocument document = ModelTestSupport.Parse("[1]", Formats.FromName("json")!);

        // The leaf is the number 1 at /Item(0)/Item(0). Its own child does not exist.
        DocNode leaf = document.Resolve(new NodePath([new ItemStep(0), new ItemStep(0)]))!;
        DocNode nearest = document.ResolveNearest(new NodePath([new ItemStep(0), new ItemStep(0), new ItemStep(0)]));

        Assert.Same(leaf, nearest);
    }

    private static int DepthOf(DocNode node)
    {
        int depth = 0;
        for (DocNode? current = node; current?.Parent is not null; current = current.Parent)
        {
            depth++;
        }

        return depth;
    }

    private static ConfigDocument LoadFixture(string kind, string name, string formatName)
    {
        byte[] bytes = File.ReadAllBytes(ModelTestSupport.FixturePath(kind, name));
        return ConfigDocument.Load(bytes, name, Formats.FromName(formatName)!);
    }
}
