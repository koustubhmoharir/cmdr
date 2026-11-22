using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// Insert on TOML: key/values and comments into the root, header, implicit, dotted and inline tables, new tables as headers, inline arrays,
/// arrays of tables, the refusals, and the layout of new text (blank lines, end of file, CRLF). Every edit goes through the reparse and
/// logical checks of <see cref="ConfigDocument.Apply"/>.
/// </summary>
public sealed class TomlEditInsertTests
{
    private static readonly IConfigFormat Toml = EditTestSupport.Toml;

    private static NodePath Path(params PathStep[] steps) => At(new PathStep[] { Item(0) }.Concat(steps).ToArray());

    private static EditOp Value(NodePath parent, int index, string? key, TomlNewKind kind, string? value = null) =>
        new InsertNode(parent, index, new NewTomlValue(key, kind, value));

    private static EditOp Comment(NodePath parent, int index, string text) => new InsertNode(parent, index, new NewTomlComment(text));

    // ----- Root and header tables -----

    [Fact]
    public void A_key_appends_after_the_last_key_value_of_the_root_and_copies_its_formatting()
    {
        string text = Edit("a = 1\nb = 2\n", Toml, Value(Path(), 2, "c", TomlNewKind.Integer, "3"));

        Assert.Equal("a = 1\nb = 2\nc = 3\n", text);
    }

    [Fact]
    public void A_key_goes_before_a_key_value_sibling_and_copies_its_spacing()
    {
        string text = Edit("a=1\nb=2\n", Toml, Value(Path(), 0, "k", TomlNewKind.String, "x"));

        Assert.Equal("k=\"x\"\na=1\nb=2\n", text);
    }

    [Fact]
    public void A_key_goes_before_the_comment_group_of_a_key_value_it_is_placed_at()
    {
        string text = Edit("# about b\nb = 2\n", Toml, Value(Path(), 0, "k", TomlNewKind.Integer, "1"));

        Assert.Equal("k = 1\n# about b\nb = 2\n", text);
    }

    [Fact]
    public void In_a_header_table_a_key_goes_before_its_key_value_sibling_and_after_one()
    {
        const string Text = "[t]\na = 1\n[u]\nb = 2\n";

        Assert.Equal("[t]\nk = 1\na = 1\n[u]\nb = 2\n", Edit(Text, Toml, Value(Path(Member("t")), 0, "k", TomlNewKind.Integer, "1")));
        Assert.Equal("[t]\na = 1\nk = 1\n[u]\nb = 2\n", Edit(Text, Toml, Value(Path(Member("t")), 1, "k", TomlNewKind.Integer, "1")));
    }

    [Fact]
    public void A_header_table_without_key_values_takes_the_key_after_its_header()
    {
        string text = Edit("[t]\n[t.u]\nx = 1\n", Toml, Value(Path(Member("t")), 0, "k", TomlNewKind.Integer, "1"));

        Assert.Equal("[t]\nk = 1\n[t.u]\nx = 1\n", text);
    }

    [Fact]
    public void A_key_goes_after_the_key_values_of_its_section_not_after_a_subtable()
    {
        // Index 2 is after the subtable [a.b], but the key still goes after the section's last key/value.
        Assert.Equal("[a]\nx = 1\nk = 9\n[a.b]\ny = 2\n", Edit("[a]\nx = 1\n[a.b]\ny = 2\n", Toml, Value(Path(Member("a")), 2, "k", TomlNewKind.Integer, "9")));
        Assert.Equal("[a]\nx = 1\nk = 9\n[a.b]\ny = 2\n", Edit("[a]\nx = 1\n[a.b]\ny = 2\n", Toml, Value(Path(Member("a")), 1, "k", TomlNewKind.Integer, "9")));
    }

    [Fact]
    public void A_key_goes_into_the_section_it_belongs_to_when_the_table_is_spread_over_the_file()
    {
        // The new key is the second member of a, so its order in the logical tree is after x and before the subtable c.
        string text = Edit("[a]\nx = 1\n[b]\ny = 2\n[a.c]\nz = 3\n", Toml, Value(Path(Member("a")), 1, "k", TomlNewKind.Integer, "1"));

        Assert.Equal("[a]\nx = 1\nk = 1\n[b]\ny = 2\n[a.c]\nz = 3\n", text);
    }

    [Fact]
    public void A_key_in_the_root_with_no_key_values_goes_after_the_comments_at_the_top()
    {
        string text = Edit("# top\n\n[t]\nx = 1\n", Toml, Value(Path(), 1, "k", TomlNewKind.Integer, "1"));

        Assert.Equal("# top\nk = 1\n\n[t]\nx = 1\n", text);
    }

    [Fact]
    public void A_key_in_an_empty_root_goes_at_the_start()
    {
        string text = Edit("", Toml, Value(Path(), 0, "k", TomlNewKind.Boolean, null));

        Assert.Equal("k = false\n", text);
    }

    // ----- New tables and sections -----

    [Fact]
    public void A_new_table_in_the_root_is_a_header_section_after_the_last_section()
    {
        string text = Edit("a = 1\n[x]\ny = 2\n", Toml, Value(Path(), 2, "t", TomlNewKind.Table));

        Assert.Equal("a = 1\n[x]\ny = 2\n[t]\n", text);
    }

    [Fact]
    public void A_new_table_copies_the_blank_line_that_separates_the_file_sections()
    {
        string text = Edit("[x]\ny = 2\n\n[z]\nq = 1\n", Toml, Value(Path(), 2, "t", TomlNewKind.Table));

        Assert.Equal("[x]\ny = 2\n\n[z]\nq = 1\n\n[t]\n", text);
    }

    [Fact]
    public void A_new_table_in_a_header_table_goes_after_the_whole_subtree_with_the_full_header()
    {
        string text = Edit("[a]\nx = 1\n[a.b]\ny = 2\n[c]\n", Toml, Value(Path(Member("a")), 2, "d", TomlNewKind.Table));

        Assert.Equal("[a]\nx = 1\n[a.b]\ny = 2\n[a.d]\n[c]\n", text);
    }

    [Fact]
    public void A_new_table_after_a_section_with_a_blank_line_follows_the_file_style()
    {
        string text = Edit("[a]\nx = 1\n\n[c]\n", Toml, Value(Path(Member("a")), 1, "d", TomlNewKind.Table));

        Assert.Equal("[a]\nx = 1\n\n[a.d]\n\n[c]\n", text);
    }

    [Fact]
    public void A_new_table_has_its_key_encoded_when_the_key_is_not_bare()
    {
        string text = Edit("a = 1\n", Toml, Value(Path(), 1, "my key", TomlNewKind.Table));

        Assert.Equal("a = 1\n[\"my key\"]\n", text);
    }

    [Fact]
    public void A_new_table_in_a_dotted_table_is_a_dotted_empty_inline_table()
    {
        string text = Edit("a.x = 1\n", Toml, Value(Path(Member("a")), 1, "t", TomlNewKind.Table));

        Assert.Equal("a.x = 1\na.t = {}\n", text);
    }

    [Fact]
    public void A_new_array_is_an_empty_inline_array()
    {
        string text = Edit("a = 1\n", Toml, Value(Path(), 1, "list", TomlNewKind.Array));

        Assert.Equal("a = 1\nlist = []\n", text);
    }

    // ----- Dotted and implicit tables -----

    [Fact]
    public void A_key_in_a_dotted_table_is_a_dotted_key_after_its_last_dotted_line()
    {
        string text = Edit("a.x = 1\na.y = 2\n", Toml, Value(Path(Member("a")), 2, "k", TomlNewKind.Integer, "3"));

        Assert.Equal("a.x = 1\na.y = 2\na.k = 3\n", text);
    }

    [Fact]
    public void A_dotted_key_uses_the_spelling_of_its_siblings_prefix()
    {
        string text = Edit("a . x = 1\n", Toml, Value(Path(Member("a")), 1, "k", TomlNewKind.Integer, "3"));

        Assert.Equal("a . x = 1\na . k = 3\n", text);
    }

    [Fact]
    public void A_key_in_an_implicit_table_is_a_new_section_before_its_first_deeper_header()
    {
        string text = Edit("[a.b]\nx = 1\n", Toml, Value(Path(Member("a")), 0, "k", TomlNewKind.Integer, "2"));

        Assert.Equal("[a]\nk = 2\n[a.b]\nx = 1\n", text);
    }

    [Fact]
    public void A_new_section_for_an_implicit_table_goes_before_the_comments_of_its_deeper_header()
    {
        // The implicit table a is made by [a.b]; its section goes before the comment that stands above [a.b], so the comment stays with it.
        string text = Edit("[x]\n\n# about a.b\n[a.b]\nx = 1\n", Toml, Value(Path(Member("a")), 0, "k", TomlNewKind.Integer, "2"));

        Assert.Equal("[x]\n\n[a]\nk = 2\n# about a.b\n[a.b]\nx = 1\n", text);
    }

    [Fact]
    public void A_comment_in_an_implicit_table_is_a_new_section_with_the_comment()
    {
        string text = Edit("[a.b]\nx = 1\n", Toml, Comment(Path(Member("a")), 0, " hi"));

        Assert.Equal("[a]\n# hi\n[a.b]\nx = 1\n", text);
    }

    // ----- Inline tables -----

    [Fact]
    public void A_member_of_an_inline_table_is_added_before_the_brace_with_the_spacing_of_its_members()
    {
        string text = Edit("t = { a = 1, b = 2 }\n", Toml, Value(Path(Member("t")), 2, "c", TomlNewKind.Integer, "3"));

        Assert.Equal("t = { a = 1, b = 2, c = 3 }\n", text);
    }

    [Fact]
    public void A_member_goes_into_an_empty_inline_table_without_extra_spaces()
    {
        string text = Edit("t = {}\n", Toml, Value(Path(Member("t")), 0, "k", TomlNewKind.String, "v"));

        Assert.Equal("t = {k = \"v\"}\n", text);
    }

    [Fact]
    public void A_member_goes_before_the_first_member_of_an_inline_table()
    {
        string text = Edit("t = { b = 2 }\n", Toml, Value(Path(Member("t")), 0, "a", TomlNewKind.Integer, "1"));

        Assert.Equal("t = { a = 1, b = 2 }\n", text);
    }

    [Fact]
    public void A_table_member_of_an_inline_table_is_an_empty_inline_table()
    {
        string text = Edit("t = { b = 2 }\n", Toml, Value(Path(Member("t")), 1, "c", TomlNewKind.Table));

        Assert.Equal("t = { b = 2, c = {} }\n", text);
    }

    // ----- Inline arrays -----

    [Fact]
    public void An_item_of_a_one_line_array_is_appended_with_the_comma_style_of_its_items()
    {
        string text = Edit("a = [1, 2]\n", Toml, Value(Path(Member("a")), 2, null, TomlNewKind.Integer, "3"));

        Assert.Equal("a = [1, 2, 3]\n", text);
    }

    [Fact]
    public void An_item_of_a_one_per_line_array_with_a_trailing_comma_is_a_line_that_keeps_the_trailing_comma()
    {
        string text = Edit("a = [\n  1,\n  2,\n]\n", Toml, Value(Path(Member("a")), 2, null, TomlNewKind.Integer, "3"));

        Assert.Equal("a = [\n  1,\n  2,\n  3,\n]\n", text);
    }

    [Fact]
    public void An_item_of_a_one_per_line_array_without_a_trailing_comma_adds_the_comma_to_the_last_item()
    {
        string text = Edit("a = [\n  1,\n  2\n]\n", Toml, Value(Path(Member("a")), 2, null, TomlNewKind.Integer, "3"));

        Assert.Equal("a = [\n  1,\n  2,\n  3\n]\n", text);
    }

    [Fact]
    public void An_item_before_the_first_item_of_a_one_per_line_array_is_a_line_of_its_own()
    {
        string text = Edit("a = [\n  1,\n  2,\n]\n", Toml, Value(Path(Member("a")), 0, null, TomlNewKind.Integer, "0"));

        Assert.Equal("a = [\n  0,\n  1,\n  2,\n]\n", text);
    }

    [Fact]
    public void A_table_or_array_item_of_an_array_is_an_empty_value()
    {
        Assert.Equal("a = [1, {}]\n", Edit("a = [1]\n", Toml, Value(Path(Member("a")), 1, null, TomlNewKind.Table)));
        Assert.Equal("a = [[]]\n", Edit("a = []\n", Toml, Value(Path(Member("a")), 0, null, TomlNewKind.Array)));
        Assert.Equal("a = [5]\n", Edit("a = []\n", Toml, Value(Path(Member("a")), 0, null, TomlNewKind.Integer, "5")));
    }

    [Fact]
    public void A_comment_goes_on_its_own_line_in_a_one_per_line_array()
    {
        string text = Edit("a = [\n  1,\n]\n", Toml, Comment(Path(Member("a")), 0, " c"));

        Assert.Equal("a = [\n  # c\n  1,\n]\n", text);
    }

    [Fact]
    public void A_comment_in_a_one_line_array_is_refused_for_lack_of_a_line()
    {
        CfgeditException error = Fails("a = [1, 2]\n", Toml, Comment(Path(Member("a")), 1, " c"));

        Assert.Contains("needs a line of its own", error.Message);
    }

    // ----- Arrays of tables -----

    [Fact]
    public void A_table_item_of_an_array_of_tables_goes_before_the_item_at_its_index()
    {
        string text = Edit("[[x]]\na = 1\n[[x]]\na = 2\n", Toml, Value(Path(Member("x")), 1, null, TomlNewKind.Table));

        Assert.Equal("[[x]]\na = 1\n[[x]]\n[[x]]\na = 2\n", text);
    }

    [Fact]
    public void A_table_item_at_the_end_of_an_array_of_tables_goes_after_its_last_item()
    {
        string text = Edit("[[x]]\na = 1\n[[x]]\na = 2\n", Toml, Value(Path(Member("x")), 2, null, TomlNewKind.Table));

        Assert.Equal("[[x]]\na = 1\n[[x]]\na = 2\n[[x]]\n", text);
    }

    [Fact]
    public void A_table_item_at_the_start_of_an_array_of_tables_goes_first()
    {
        string text = Edit("[[x]]\na = 1\n", Toml, Value(Path(Member("x")), 0, null, TomlNewKind.Table));

        Assert.Equal("[[x]]\n[[x]]\na = 1\n", text);
    }

    [Fact]
    public void A_table_item_goes_after_the_subtables_of_the_item_before_it()
    {
        string text = Edit("[[x]]\na = 1\n[x.s]\nb = 2\n[[x]]\na = 2\n", Toml, Value(Path(Member("x")), 1, null, TomlNewKind.Table));

        Assert.Equal("[[x]]\na = 1\n[x.s]\nb = 2\n[[x]]\n[[x]]\na = 2\n", text);
    }

    [Fact]
    public void A_non_table_is_refused_in_an_array_of_tables()
    {
        CfgeditException error = Fails("[[x]]\na = 1\n", Toml, Value(Path(Member("x")), 1, null, TomlNewKind.Integer, "1"));

        Assert.Equal("an array of tables holds tables only", error.Message);
    }

    [Fact]
    public void An_item_with_a_key_is_refused_in_an_array()
    {
        CfgeditException error = Fails("a = [1]\n", Toml, Value(Path(Member("a")), 1, "k", TomlNewKind.Integer, "1"));

        Assert.Equal("an item of an array has no key", error.Message);
    }

    // ----- Comments -----

    [Fact]
    public void A_comment_line_goes_above_the_key_value_it_is_placed_before()
    {
        string text = Edit("a = 1\nb = 2\n", Toml, Comment(Path(), 1, " note"));

        Assert.Equal("a = 1\n# note\nb = 2\n", text);
    }

    [Fact]
    public void A_comment_line_appends_after_the_last_key_value()
    {
        string text = Edit("a = 1\n", Toml, Comment(Path(), 1, " end"));

        Assert.Equal("a = 1\n# end\n", text);
    }

    [Fact]
    public void A_comment_text_may_have_no_leading_space_and_may_not_break_a_line()
    {
        Assert.Equal("a = 1\n#x\n", Edit("a = 1\n", Toml, Comment(Path(), 1, "x")));
        CfgeditException error = Fails("a = 1\n", Toml, Comment(Path(), 1, "x\ny"));
        Assert.Equal("a comment cannot contain a line break", error.Message);
    }

    [Fact]
    public void A_comment_cannot_go_into_an_inline_table()
    {
        CfgeditException error = Fails("t = {a = 1}\n", Toml, Comment(Path(Member("t")), 1, " c"));

        Assert.Equal("a comment cannot go into an inline table", error.Message);
    }

    // ----- Ends, line breaks and refusals -----

    [Fact]
    public void A_key_at_the_end_of_a_file_without_a_final_line_break_gets_the_break_first()
    {
        string text = Edit("a = 1", Toml, Value(Path(), 1, "b", TomlNewKind.Integer, "2"));

        Assert.Equal("a = 1\nb = 2", text);
    }

    [Fact]
    public void A_key_in_a_CRLF_document_uses_CRLF()
    {
        string text = Edit("a = 1\r\nb = 2\r\n", Toml, Value(Path(), 2, "c", TomlNewKind.Integer, "3"));

        Assert.Equal("a = 1\r\nb = 2\r\nc = 3\r\n", text);
    }

    [Fact]
    public void A_key_that_is_already_a_member_is_refused()
    {
        CfgeditException error = Fails("a = 1\n", Toml, Value(Path(), 1, "a", TomlNewKind.Integer, "2"));

        Assert.Equal("a key named 'a' already exists here", error.Message);
    }

    [Fact]
    public void A_table_that_is_already_a_member_is_refused_as_a_duplicate_key()
    {
        CfgeditException error = Fails("[t]\nx = 1\n", Toml, Value(Path(), 1, "t", TomlNewKind.Table));

        Assert.Equal("a key named 't' already exists here", error.Message);
    }

    [Fact]
    public void A_key_is_needed_in_a_table_and_an_inline_table()
    {
        Assert.Equal("a key is needed for an entry of a table", Fails("a = 1\n", Toml, Value(Path(), 1, null, TomlNewKind.Integer, "1")).Message);
        Assert.Equal("a key is needed for a member of an inline table", Fails("t = {}\n", Toml, Value(Path(Member("t")), 0, null, TomlNewKind.Integer, "1")).Message);
    }

    [Fact]
    public void A_value_of_the_wrong_kind_is_refused_before_anything_is_written()
    {
        CfgeditException error = Fails("a = 1\n", Toml, Value(Path(), 1, "b", TomlNewKind.Integer, "1.5"));

        Assert.Equal("'1.5' is not a valid integer", error.Message);
        Assert.Equal("a date-time needs a value", Fails("a = 1\n", Toml, Value(Path(), 1, "b", TomlNewKind.DateTime, null)).Message);
    }

    // ----- Focus and the index helpers -----

    [Fact]
    public void The_focus_after_a_key_is_the_new_key_and_after_an_item_it_is_the_item()
    {
        (string _, NodePath? focus) = EditWithFocus("a = 1\n", Toml, Value(Path(), 1, "b", TomlNewKind.Integer, "2"));
        Assert.Equal(Path(Member("b")), focus);

        (string _, NodePath? itemFocus) = EditWithFocus("a = [1]\n", Toml, Value(Path(Member("a")), 1, null, TomlNewKind.Integer, "2"));
        Assert.Equal(Path(Member("a"), Item(1)), itemFocus);
    }

    [Fact]
    public void Insertion_indices_on_a_TOML_key_include_its_attached_and_trailing_comments()
    {
        ConfigDocument doc = ModelTestSupport.Parse("# about a\na = 1 # t\n# about b\nb = 2\n", Toml);

        Assert.Equal(0, doc.InsertionIndexBefore(Path(Member("a"))));
        Assert.Equal(3, doc.InsertionIndexAfter(Path(Member("a"))));
        Assert.Equal(3, doc.InsertionIndexBefore(Path(Member("b"))));
        Assert.Equal(5, doc.InsertionIndexAfter(Path(Member("b"))));
    }

    [Fact]
    public void A_key_placed_at_an_insertion_index_follows_the_comments_of_its_neighbour()
    {
        ConfigDocument doc = ModelTestSupport.Parse("# about a\na = 1 # t\nb = 2\n", Toml);
        int index = doc.InsertionIndexAfter(Path(Member("a")));

        string text = Apply(doc, Value(Path(), index, "k", TomlNewKind.Integer, "9"));

        Assert.Equal("# about a\na = 1 # t\nk = 9\nb = 2\n", text);
    }

    /// <summary>
    /// Every container of a mixed document gets every index with each kind of keyed and unkeyed value, and a comment; every node gets a
    /// duplicate. A refusal is a user's answer and is fine. An internal edit error, or any other exception, means the planner and the
    /// logical check disagree, or the planner broke.
    /// </summary>
    [Fact]
    public void Every_insert_and_duplicate_in_mixed_documents_is_planned_and_checked_without_an_internal_error()
    {
        string[] documents =
        [
            "a = 1\n# c\nb = 2 # t\n[x]\ny = [1, 2]\n[x.z]\nq = {a = 1}\n[[w]]\nk = 1\n[[w]]\n",
            "[a.b]\nx = 1\n[a.b.c]\ny = [\n  1, # one\n  2,\n]\n[[a.d]]\n# lead\ne = 1\n",
            "t = {a.b = 1, c = [ {x = 1}, {x = 2} ]}\nd.e = 2\nd.f.g = 3 # tr\n",
            "[x]\r\ny = 2\r\n\r\n[z]\r\nq = [\r\n  1,\r\n]\r\n",
            "a = 1",
            "",
            "\n\n",
            "[[a]]\n# c\nx = 1 # t\n[a.b]\n[[a]]\n[a.b.c]\nz = [\n  # in\n  1,\n]\n",
            "# top\n\n# loose\n\n[t]\n\n[t.u]\n# about w\n[t.w]\nk = 1\n",
            "x = [1,2 # c\n, 3]\ny = {}\nw = [\n  {a = 1},\n  [ 2 ],\n]\n",
            "t = {a.b = 1, a.c = {d = 2}}\nu = { }\n",
        ];
        var failures = new List<string>();
        foreach (string text in documents)
        {
            ConfigDocument doc = ModelTestSupport.Parse(text, Toml);
            List<DocNode> nodes = ModelTestSupport.AllNodes(doc.Root).Where(node => node.Parent is not null).ToList();
            foreach (DocNode container in nodes.Where(node => node.Kind is NodeKind.TomlTable or NodeKind.TomlArray))
            {
                for (int index = 0; index <= container.Children.Count; index++)
                {
                    NodePath path = container.Path;
                    var ops = new List<EditOp>
                    {
                        Value(path, index, "zz", TomlNewKind.Integer, "1"),
                        Value(path, index, "zt", TomlNewKind.Table),
                        Value(path, index, "za", TomlNewKind.Array),
                        Value(path, index, "zs", TomlNewKind.String, "x y"),
                        Value(path, index, null, TomlNewKind.Table),
                        Value(path, index, null, TomlNewKind.Array),
                        Value(path, index, null, TomlNewKind.Boolean),
                        Comment(path, index, " cmt"),
                    };
                    foreach (EditOp op in ops)
                    {
                        Check(doc, op, text, failures);
                    }
                }
            }

            foreach (DocNode node in nodes)
            {
                Check(doc, new DuplicateNode(node.Path), text, failures);
            }
        }

        Assert.True(failures.Count == 0, failures.Count + " failures:\n" + string.Join("\n", failures.Take(40)));
    }

    private static void Check(ConfigDocument doc, EditOp op, string text, List<string> failures)
    {
        try
        {
            doc.Apply(op);
        }
        catch (CfgeditException ex) when (!ex.Message.StartsWith("internal edit error", StringComparison.Ordinal))
        {
            // A refusal: the planner said no, with a message.
        }
        catch (Exception ex)
        {
            failures.Add($"{op} on {text.Replace("\r", "\\r").Replace("\n", "\\n")}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string Apply(ConfigDocument doc, EditOp op) => doc.Apply(op).Document.Source.Text;
}
