using Cfgedit.Core;
using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>MoveNodeTo and NestBy on JSON: moves into another container, keys, commas, layout, focus and the nest rules.</summary>
public sealed class JsonEditMoveToTests
{
    private static readonly NodePath Root = At(Item(0));

    [Fact]
    public void A_member_nests_up_into_the_object_above_and_goes_last()
    {
        string text = Edit(
            "{\n  \"x\": {\n    \"a\": 1\n  },\n  \"y\": 2\n}",
            Strict,
            new MoveNodeTo(At(Item(0), Member("y")), At(Item(0), Member("x")), 1, null));

        Assert.Equal("{\n  \"x\": {\n    \"a\": 1,\n    \"y\": 2\n  }\n}", text);
    }

    [Fact]
    public void A_member_nests_down_into_the_object_below_and_goes_first()
    {
        string text = Edit(
            "{\n  \"x\": 1,\n  \"y\": {\n    \"a\": 1\n  }\n}",
            Strict,
            new MoveNodeTo(At(Item(0), Member("x")), At(Item(0), Member("y")), 0, null));

        Assert.Equal("{\n  \"y\": {\n    \"x\": 1,\n    \"a\": 1\n  }\n}", text);
    }

    [Fact]
    public void An_item_moves_into_the_array_above_and_goes_last()
    {
        string text = Edit("[[1], 2]", Strict, new MoveNodeTo(At(Item(0), Item(1)), At(Item(0), Item(0)), 1, null));

        Assert.Equal("[[1, 2]]", text);
    }

    [Fact]
    public void An_item_moves_into_the_array_below_and_goes_first()
    {
        string text = Edit("[1, [2]]", Strict, new MoveNodeTo(At(Item(0), Item(0)), At(Item(0), Item(1)), 0, null));

        Assert.Equal("[[1, 2]]", text);
    }

    [Fact]
    public void A_member_that_nests_into_an_array_drops_its_key()
    {
        string text = Edit(
            "{\"list\": [1, 2], \"a\": 3}",
            Strict,
            new MoveNodeTo(At(Item(0), Member("a")), At(Item(0), Member("list")), 1, null));

        Assert.Equal("{\"list\": [1, 3, 2]}", text);
    }

    [Fact]
    public void An_item_that_moves_into_an_object_takes_the_new_key()
    {
        string text = Edit(
            "[1, {\"k\": 1}]",
            Strict,
            new MoveNodeTo(At(Item(0), Item(0)), At(Item(0), Item(1)), 0, "n"));

        Assert.Equal("[{\"n\": 1, \"k\": 1}]", text);
    }

    [Fact]
    public void A_member_renames_into_a_json5_object_with_a_bare_key()
    {
        string text = Edit(
            "{\n  a: 1,\n  box: {\n    z: 0\n  }\n}",
            Json5,
            new MoveNodeTo(At(Item(0), Member("a")), At(Item(0), Member("box")), 0, "b"));

        Assert.Equal("{\n  box: {\n    b: 1,\n    z: 0\n  }\n}", text);
    }

    [Fact]
    public void A_member_moves_out_of_its_parent_to_just_before_the_parent_with_its_leading_comments()
    {
        string text = Edit(
            "{\n  // about outer\n  \"outer\": {\n    \"first\": 1\n  }\n}",
            Jsonc,
            new MoveNodeTo(At(Item(0), Member("outer"), Member("first")), Root, 0, null));

        Assert.Equal("{\n  \"first\": 1,\n  // about outer\n  \"outer\": {}\n}", text);
    }

    [Fact]
    public void A_last_item_moves_out_of_its_array_to_just_after_the_array()
    {
        string text = Edit(
            "{\n  \"list\": [\n    1,\n    2\n  ],\n  \"after\": 3\n}",
            Strict,
            new MoveNodeTo(At(Item(0), Member("list"), Item(1)), Root, 1, "two"));

        Assert.Equal("{\n  \"list\": [\n    1\n  ],\n  \"two\": 2,\n  \"after\": 3\n}", text);
    }

    [Fact]
    public void A_member_moved_into_an_object_with_its_key_taken_is_refused()
    {
        CfgeditException ex = Fails(
            "{\"x\": {\"a\": 1}, \"a\": 2}",
            Strict,
            new MoveNodeTo(At(Item(0), Member("a")), At(Item(0), Member("x")), 0, null));

        Assert.Equal("a member named 'a' already exists", ex.Message);
    }

    [Fact]
    public void An_item_moved_into_an_object_without_a_key_is_refused()
    {
        CfgeditException ex = Fails(
            "{\"list\": [1], \"o\": {\"a\": 1}}",
            Strict,
            new MoveNodeTo(At(Item(0), Member("list"), Item(0)), At(Item(0), Member("o")), 0, null));

        Assert.Equal("A member needs a key", ex.Message);
    }

    [Fact]
    public void A_key_for_an_item_of_an_array_is_refused()
    {
        CfgeditException ex = Fails("[[1], 2]", Strict, new MoveNodeTo(At(Item(0), Item(1)), At(Item(0), Item(0)), 0, "k"));

        Assert.Equal("Items of an array have no key", ex.Message);
    }

    [Fact]
    public void A_node_cannot_move_into_a_container_inside_itself()
    {
        CfgeditException ex = Fails(
            "{\"x\": {\"y\": {}}}",
            Strict,
            new MoveNodeTo(At(Item(0), Member("x")), At(Item(0), Member("x"), Member("y")), 0, null));

        Assert.Equal("A node cannot be moved into itself", ex.Message);
    }

    [Fact]
    public void A_node_cannot_move_into_its_current_parent()
    {
        CfgeditException ex = Fails("{\"x\": {\"a\": 1}}", Strict, new MoveNodeTo(At(Item(0), Member("x"), Member("a")), At(Item(0), Member("x")), 0, null));

        Assert.Equal("The node is already in that container", ex.Message);
    }

    [Fact]
    public void The_root_value_cannot_move_into_another_container()
    {
        CfgeditException ex = Fails("[1, {}]", Strict, new MoveNodeTo(RootValue, At(Item(0), Item(1)), 0, null));

        Assert.Equal("The root value cannot be moved", ex.Message);
    }

    [Fact]
    public void A_scalar_is_not_a_container_to_move_into()
    {
        CfgeditException ex = Fails("[1, 2]", Strict, new MoveNodeTo(At(Item(0), Item(0)), At(Item(0), Item(1)), 0, null));

        Assert.Equal("The new parent must be a JSON object or array", ex.Message);
    }

    [Fact]
    public void A_member_that_moves_with_its_attached_comments_keeps_them_in_order()
    {
        string text = Edit(
            "{\n  // about a\n  \"a\": 1, // trailing a\n  \"o\": {\n    \"k\": 0\n  }\n}",
            Jsonc,
            new MoveNodeTo(At(Item(0), Member("a")), At(Item(0), Member("o")), 0, null));

        Assert.Equal("{\n  \"o\": {\n    // about a\n    \"a\": 1, // trailing a\n    \"k\": 0\n  }\n}", text);
    }

    [Fact]
    public void A_lone_comment_moves_alone_into_a_container()
    {
        string text = Edit(
            "{\n  \"o\": {\n    \"k\": 1\n  },\n  // lone\n  \"b\": 2\n}",
            Jsonc,
            new MoveNodeTo(At(Item(0), Comment(0)), At(Item(0), Member("o")), 1, null));

        Assert.Equal("{\n  \"o\": {\n    \"k\": 1\n    // lone\n  },\n  \"b\": 2\n}", text);
    }

    [Fact]
    public void A_comment_with_a_key_is_refused()
    {
        CfgeditException ex = Fails(
            "{\n  // c\n  \"o\": {}\n}",
            Jsonc,
            new MoveNodeTo(At(Item(0), Comment(0)), At(Item(0), Member("o")), 0, "k"));

        Assert.Equal("A comment has no key", ex.Message);
    }

    [Fact]
    public void Moving_the_last_entry_out_of_a_trailing_comma_object_keeps_the_trailing_comma()
    {
        string text = Edit(
            "{\n  \"x\": {\n    \"a\": 1\n  },\n  \"y\": 2,\n}",
            Jsonc,
            new MoveNodeTo(At(Item(0), Member("y")), At(Item(0), Member("x")), 1, null));

        Assert.Equal("{\n  \"x\": {\n    \"a\": 1,\n    \"y\": 2\n  },\n}", text);
    }

    [Fact]
    public void Moving_the_only_entry_leaves_its_container_empty_and_fills_the_new_empty_array()
    {
        string text = Edit(
            "{\n  \"box\": {\n    \"only\": 1\n  },\n  \"list\": []\n}",
            Strict,
            new MoveNodeTo(At(Item(0), Member("box"), Member("only")), At(Item(0), Member("list")), 0, null));

        Assert.Equal("{\n  \"box\": {},\n  \"list\": [\n    1\n  ]\n}", text);
    }

    [Fact]
    public void A_member_moves_into_an_empty_object_that_becomes_multi_line_in_a_multi_line_document()
    {
        string text = Edit(
            "{\n  \"e\": {},\n  \"s\": 1\n}",
            Strict,
            new MoveNodeTo(At(Item(0), Member("s")), At(Item(0), Member("e")), 0, null));

        Assert.Equal("{\n  \"e\": {\n    \"s\": 1\n  }\n}", text);
    }

    [Fact]
    public void A_member_moves_into_an_empty_object_and_stays_single_line_in_a_single_line_document()
    {
        string text = Edit(
            "{\"e\": {}, \"s\": 1}",
            Strict,
            new MoveNodeTo(At(Item(0), Member("s")), At(Item(0), Member("e")), 0, null));

        Assert.Equal("{\"e\": {\"s\": 1}}", text);
    }

    [Fact]
    public void A_member_moves_into_a_single_line_container_with_entries()
    {
        string text = Edit(
            "{\"x\": {\"a\": 1}, \"s\": 2}",
            Strict,
            new MoveNodeTo(At(Item(0), Member("s")), At(Item(0), Member("x")), 1, null));

        Assert.Equal("{\"x\": {\"a\": 1, \"s\": 2}}", text);
    }

    [Fact]
    public void A_multi_line_value_is_indented_deeper_when_it_moves_into_a_deeper_container()
    {
        string text = Edit(
            "{\n  \"a\": {\n    \"n\": 1\n  },\n  \"box\": {\n    \"z\": 0\n  }\n}",
            Strict,
            new MoveNodeTo(At(Item(0), Member("a")), At(Item(0), Member("box")), 0, null));

        Assert.Equal("{\n  \"box\": {\n    \"a\": {\n      \"n\": 1\n    },\n    \"z\": 0\n  }\n}", text);
    }

    [Fact]
    public void A_multi_line_value_is_indented_shallower_when_it_moves_out_of_a_deeper_container()
    {
        string text = Edit(
            "{\n  \"box\": {\n    \"a\": {\n      \"n\": 1\n    }\n  },\n  \"z\": 0\n}",
            Strict,
            new MoveNodeTo(At(Item(0), Member("box"), Member("a")), Root, 1, null));

        Assert.Equal("{\n  \"box\": {},\n  \"a\": {\n    \"n\": 1\n  },\n  \"z\": 0\n}", text);
    }

    [Fact]
    public void A_block_comment_inside_a_moved_value_keeps_its_lines_while_the_rest_is_indented()
    {
        string text = Edit(
            "{\n  \"a\": {\n    /* keep\n  this */\n    \"n\": 1\n  },\n  \"box\": {\n    \"z\": 0\n  }\n}",
            Jsonc,
            new MoveNodeTo(At(Item(0), Member("a")), At(Item(0), Member("box")), 0, null));

        Assert.Equal(
            "{\n  \"box\": {\n    \"a\": {\n      /* keep\n  this */\n      \"n\": 1\n    },\n    \"z\": 0\n  }\n}",
            text);
    }

    [Fact]
    public void A_member_moves_down_into_the_container_after_it_and_the_focus_follows_it()
    {
        (string text, NodePath? focus) = EditWithFocus(
            "{\n  \"a\": 1,\n  \"box\": {\n    \"z\": 0\n  }\n}",
            Strict,
            new MoveNodeTo(At(Item(0), Member("a")), At(Item(0), Member("box")), 0, null));

        Assert.Equal("{\n  \"box\": {\n    \"a\": 1,\n    \"z\": 0\n  }\n}", text);
        Assert.Equal(At(Item(0), Member("box"), Member("a")), focus);
    }

    [Fact]
    public void An_array_item_moved_down_into_the_next_sibling_array_gets_the_focus_of_its_new_index()
    {
        (string text, NodePath? focus) = EditWithFocus(
            "[[1, [2]]]",
            Strict,
            new MoveNodeTo(At(Item(0), Item(0), Item(0)), At(Item(0), Item(0), Item(1)), 0, null));

        Assert.Equal("[[[1, 2]]]", text);
        Assert.Equal(At(Item(0), Item(0), Item(0), Item(0)), focus);
    }

    [Fact]
    public void A_moved_duplicate_key_takes_the_ordinal_of_its_new_place_after_the_destination_shifts()
    {
        (string text, NodePath? focus) = EditWithFocus(
            "{\"box\": {\"j\": 1}, \"box\": {\"m\": 2}}",
            Strict,
            new MoveNodeTo(At(Item(0), Member("box", 0)), At(Item(0), Member("box", 1)), 0, null));

        Assert.Equal("{\"box\": {\"box\": {\"j\": 1}, \"m\": 2}}", text);
        Assert.Equal(At(Item(0), Member("box", 0), Member("box", 0)), focus);
    }

    [Fact]
    public void A_moved_lone_comment_takes_the_focus_at_its_new_place()
    {
        (string text, NodePath? focus) = EditWithFocus(
            "{\n  \"o\": {\n    \"k\": 1\n  },\n  // lone\n  \"b\": 2\n}",
            Jsonc,
            new MoveNodeTo(At(Item(0), Comment(0)), At(Item(0), Member("o")), 1, null));

        Assert.Equal("{\n  \"o\": {\n    \"k\": 1\n    // lone\n  },\n  \"b\": 2\n}", text);
        Assert.Equal(At(Item(0), Member("o"), Comment(0)), focus);
    }

    [Fact]
    public void Nest_up_enters_the_container_above_at_its_end()
    {
        NestStep? step = ModelTestSupport.Parse("{\n  \"x\": {\n    \"a\": 1\n  },\n  \"y\": 2\n}", Strict)
            .NestBy(At(Item(0), Member("y")), -1);

        Assert.Equal(new MoveNodeTo(At(Item(0), Member("y")), At(Item(0), Member("x")), 1, null), step!.Op);
        Assert.False(step.NeedsKey);
    }

    [Fact]
    public void Nest_down_enters_the_container_below_at_its_start()
    {
        NestStep? step = ModelTestSupport.Parse("{\"x\": 1, \"y\": {\"a\": 1}}", Strict).NestBy(At(Item(0), Member("x")), 1);

        Assert.Equal(new MoveNodeTo(At(Item(0), Member("x")), At(Item(0), Member("y")), 0, null), step!.Op);
    }

    [Fact]
    public void Nest_past_a_scalar_neighbour_is_the_ordinary_move()
    {
        NestStep? step = ModelTestSupport.Parse("[1, 2]", Strict).NestBy(At(Item(0), Item(1)), -1);

        Assert.Equal(new MoveNode(At(Item(0), Item(1)), 0), step!.Op);
    }

    [Fact]
    public void Nest_past_a_comment_unit_is_the_ordinary_move()
    {
        NestStep? step = ModelTestSupport.Parse("{\n  \"a\": 1,\n\n  // c\n\n  \"b\": 2\n}", Jsonc).NestBy(At(Item(0), Member("b")), -1);

        Assert.Equal(new MoveNode(At(Item(0), Member("b")), 1), step!.Op);
    }

    [Fact]
    public void Nest_out_of_the_parent_up_goes_just_before_the_parent()
    {
        NestStep? step = ModelTestSupport.Parse("{\n  \"outer\": {\n    \"first\": 1,\n    \"second\": 2\n  }\n}", Strict)
            .NestBy(At(Item(0), Member("outer"), Member("first")), -1);

        Assert.Equal(new MoveNodeTo(At(Item(0), Member("outer"), Member("first")), Root, 0, null), step!.Op);
    }

    [Fact]
    public void Nest_out_of_the_parent_down_goes_just_after_the_parent()
    {
        NestStep? step = ModelTestSupport.Parse("{\n  \"outer\": {\n    \"first\": 1,\n    \"second\": 2\n  }\n}", Strict)
            .NestBy(At(Item(0), Member("outer"), Member("second")), 1);

        Assert.Equal(new MoveNodeTo(At(Item(0), Member("outer"), Member("second")), Root, 1, null), step!.Op);
    }

    [Fact]
    public void A_member_of_the_root_object_cannot_nest_out_and_the_root_value_cannot_nest()
    {
        ConfigDocument document = ModelTestSupport.Parse("{\"a\": 1}", Strict);

        Assert.Null(document.NestBy(At(Item(0), Member("a")), -1));
        Assert.Null(ModelTestSupport.Parse("[1]", Strict).NestBy(RootValue, 1));
    }

    [Fact]
    public void Nest_into_a_key_taken_in_the_container_renames_the_member()
    {
        NestStep? step = ModelTestSupport.Parse("{\n  \"x\": {\n    \"port\": 2\n  },\n  \"port\": 1\n}", Strict)
            .NestBy(At(Item(0), Member("port")), -1);

        Assert.Equal(new MoveNodeTo(At(Item(0), Member("port")), At(Item(0), Member("x")), 1, "port2"), step!.Op);
        Assert.False(step.NeedsKey);
    }

    [Fact]
    public void Nest_of_an_array_item_into_an_object_needs_a_key_and_the_key_is_applied_with_a_with_expression()
    {
        ConfigDocument document = ModelTestSupport.Parse("[1, {\"k\": 1}]", Strict);
        NestStep? step = document.NestBy(At(Item(0), Item(0)), 1);

        Assert.True(step!.NeedsKey);
        var op = (MoveNodeTo)step.Op;
        Assert.Null(op.NewKey);

        Assert.Equal("[{\"n\": 1, \"k\": 1}]", document.Apply(op with { NewKey = "n" }).Document.Source.Text);
    }
}
