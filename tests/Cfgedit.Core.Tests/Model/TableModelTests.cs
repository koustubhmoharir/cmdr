using Cfgedit.Core;
using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>Table detection, columns, cells, and the edit operations the table returns.</summary>
public sealed class TableModelTests
{
    /// <summary>The ellipsis of a nested XML cell, written without the code under test.</summary>
    private static readonly string NestedXml = "<" + (char)0x2026 + ">";

    private static readonly NodePath XmlRoot = At(Element("root"));

    private static ConfigDocument ParseJson(string text) => ModelTestSupport.Parse(text, Strict);

    private static readonly IConfigFormat XmlFormat = EditTestSupport.Xml;

    private static ConfigDocument ParseXml(string text) => ModelTestSupport.Parse(text, XmlFormat);

    private static NodePath Row(int index) => At(Item(0), Item(index));

    private static NodePath XmlRow(string name, int ordinal) => At(Element("root"), Element(name, ordinal));

    private static TableModel Table(ConfigDocument document, NodePath node)
    {
        TableModel? table = TableModel.TryCreate(document, node);
        Assert.NotNull(table);
        return table;
    }

    // ----- TryCreate: JSON -----

    [Fact]
    public void An_array_of_objects_is_a_table_whose_rows_are_its_items()
    {
        TableModel table = Table(ParseJson("""[{"a": 1}, {"b": 2}]"""), RootValue);

        Assert.Equal(RootValue, table.Container);
        Assert.Equal(new[] { Row(0), Row(1) }, table.Rows);
    }

    [Fact]
    public void A_row_path_gives_the_array_as_the_container()
    {
        TableModel table = Table(ParseJson("""[{"a": 1}, {"b": 2}]"""), Row(1));

        Assert.Equal(RootValue, table.Container);
        Assert.Equal(new[] { Row(0), Row(1) }, table.Rows);
    }

    [Fact]
    public void An_array_of_scalars_is_a_table_with_one_value_column()
    {
        TableModel table = Table(ParseJson("""[1, "x", true, null]"""), RootValue);

        Assert.Equal(new[] { new TableColumn(TableColumnKind.Value, "value") }, table.Columns);
        Assert.Equal(4, table.Rows.Count);
    }

    [Fact]
    public void A_row_of_a_scalar_array_gives_the_array_as_the_container()
    {
        TableModel table = Table(ParseJson("""[1, "x", true]"""), Row(2));

        Assert.Equal(RootValue, table.Container);
    }

    [Fact]
    public void An_array_mixing_objects_and_scalars_is_not_a_table()
    {
        Assert.Null(TableModel.TryCreate(ParseJson("""[{"a": 1}, 2]"""), RootValue));
    }

    [Fact]
    public void An_array_mixing_objects_and_arrays_is_not_a_table()
    {
        Assert.Null(TableModel.TryCreate(ParseJson("""[{"a": 1}, [1]]"""), RootValue));
    }

    [Fact]
    public void An_array_of_arrays_is_not_a_table()
    {
        Assert.Null(TableModel.TryCreate(ParseJson("""[[1], [2]]"""), RootValue));
    }

    [Fact]
    public void An_empty_array_is_not_a_table()
    {
        Assert.Null(TableModel.TryCreate(ParseJson("[]"), RootValue));
    }

    [Fact]
    public void An_array_holding_only_comments_is_not_a_table()
    {
        Assert.Null(TableModel.TryCreate(ParseJsonc("[ // nothing\n]"), RootValue));
    }

    [Fact]
    public void Comments_in_an_array_are_not_rows()
    {
        TableModel table = Table(ParseJsonc("[ // head\n {\"a\": 1}, /* between */ {\"a\": 2} ]"), RootValue);

        Assert.Equal(new[] { Row(0), Row(1) }, table.Rows);
    }

    [Fact]
    public void Comments_in_a_scalar_array_are_not_rows()
    {
        TableModel table = Table(ParseJsonc("[1, /* c */ 2]"), RootValue);

        Assert.Equal(2, table.Rows.Count);
    }

    [Fact]
    public void An_array_inside_an_object_is_a_table_at_its_own_path()
    {
        NodePath inner = At(Item(0), Member("a"));
        TableModel table = Table(ParseJson("""{"a": [{"x": 1}]}"""), inner);

        Assert.Equal(inner, table.Container);
        Assert.Single(table.Rows);
    }

    [Fact]
    public void An_object_is_not_a_table()
    {
        Assert.Null(TableModel.TryCreate(ParseJson("""{"a": 1}"""), RootValue));
    }

    [Fact]
    public void A_scalar_root_is_not_a_table()
    {
        Assert.Null(TableModel.TryCreate(ParseJson("5"), RootValue));
    }

    [Fact]
    public void A_member_of_a_row_is_not_a_table_row()
    {
        Assert.Null(TableModel.TryCreate(ParseJson("""[{"a": 1}]"""), At(Item(0), Item(0), Member("a"))));
    }

    [Fact]
    public void A_missing_path_is_not_a_table()
    {
        Assert.Null(TableModel.TryCreate(ParseJson("""[{"a": 1}]"""), At(Item(0), Item(9))));
    }

    // ----- TryCreate: XML -----

    [Fact]
    public void An_element_with_two_same_named_children_is_a_table()
    {
        TableModel table = Table(ParseXml("<root><r/><r/></root>"), XmlRoot);

        Assert.Equal(XmlRoot, table.Container);
        Assert.Equal(new[] { XmlRow("r", 0), XmlRow("r", 1) }, table.Rows);
    }

    [Fact]
    public void A_row_path_gives_the_element_as_the_container()
    {
        TableModel table = Table(ParseXml("<root><r/><r/></root>"), XmlRow("r", 1));

        Assert.Equal(XmlRoot, table.Container);
        Assert.Equal(2, table.Rows.Count);
    }

    [Fact]
    public void An_element_with_one_child_is_not_a_table()
    {
        Assert.Null(TableModel.TryCreate(ParseXml("<root><r/></root>"), XmlRoot));
    }

    [Fact]
    public void An_element_with_no_children_is_not_a_table()
    {
        Assert.Null(TableModel.TryCreate(ParseXml("<root/>"), XmlRoot));
    }

    [Fact]
    public void An_element_whose_children_all_have_different_names_is_not_a_table()
    {
        Assert.Null(TableModel.TryCreate(ParseXml("<root><a/><b/></root>"), XmlRoot));
    }

    [Fact]
    public void The_name_with_the_most_elements_is_chosen_for_a_container()
    {
        TableModel table = Table(ParseXml("<root><a/><a/><a/><b/><b/></root>"), XmlRoot);

        Assert.Equal(new[] { XmlRow("a", 0), XmlRow("a", 1), XmlRow("a", 2) }, table.Rows);
    }

    [Fact]
    public void A_tie_in_element_counts_goes_to_the_name_seen_first()
    {
        TableModel table = Table(ParseXml("<root><b/><a/><a/><b/></root>"), XmlRoot);

        Assert.Equal(new[] { XmlRow("b", 0), XmlRow("b", 1) }, table.Rows);
    }

    [Fact]
    public void A_row_uses_its_own_name_even_when_another_name_has_more_elements()
    {
        TableModel table = Table(ParseXml("<root><a/><a/><a/><b/><b/></root>"), XmlRow("b", 0));

        Assert.Equal(new[] { XmlRow("b", 0), XmlRow("b", 1) }, table.Rows);
    }

    [Fact]
    public void A_row_whose_name_occurs_once_is_not_a_table_row()
    {
        Assert.Null(TableModel.TryCreate(ParseXml("<root><a/><b/><b/></root>"), XmlRow("a", 0)));
    }

    [Fact]
    public void A_row_is_a_row_even_when_it_has_qualifying_children_of_its_own()
    {
        TableModel table = Table(ParseXml("<root><item><x/><x/></item><item/></root>"), XmlRow("item", 0));

        Assert.Equal(XmlRoot, table.Container);
        Assert.Equal(2, table.Rows.Count);
    }

    [Fact]
    public void Comments_between_elements_do_not_count_as_rows()
    {
        TableModel table = Table(ParseXml("<root><!--c--><a/><!--d--><a/></root>"), XmlRoot);

        Assert.Equal(2, table.Rows.Count);
    }

    // ----- Columns: JSON -----

    [Fact]
    public void Object_columns_are_the_union_of_keys_in_first_seen_order()
    {
        TableModel table = Table(ParseJson("""[{"b": 1, "a": 2}, {"c": 3, "a": 4}]"""), RootValue);

        Assert.Equal(
            new[] { new TableColumn(TableColumnKind.Key, "b"), new TableColumn(TableColumnKind.Key, "a"), new TableColumn(TableColumnKind.Key, "c") },
            table.Columns);
    }

    [Fact]
    public void An_empty_object_row_adds_no_columns()
    {
        TableModel table = Table(ParseJson("""[{}, {"x": 1}]"""), RootValue);

        Assert.Equal(new[] { new TableColumn(TableColumnKind.Key, "x") }, table.Columns);
    }

    // ----- Columns: XML -----

    [Fact]
    public void Attribute_columns_are_the_union_of_attribute_names_in_first_seen_order()
    {
        TableModel table = Table(ParseXml("""<root><r x="1" y="2"/><r y="3" z="4"/></root>"""), XmlRoot);

        Assert.Equal(
            new[] { new TableColumn(TableColumnKind.Attribute, "x"), new TableColumn(TableColumnKind.Attribute, "y"), new TableColumn(TableColumnKind.Attribute, "z") },
            table.Columns);
    }

    [Fact]
    public void Child_element_columns_follow_the_attributes_in_first_seen_order()
    {
        TableModel table = Table(ParseXml("<root><r><n>1</n><m/></r><r><n>2</n></r></root>"), XmlRoot);

        Assert.Equal(
            new[] { new TableColumn(TableColumnKind.ChildElement, "n"), new TableColumn(TableColumnKind.ChildElement, "m") },
            table.Columns);
    }

    [Fact]
    public void A_child_name_that_occurs_twice_in_one_row_is_not_a_column()
    {
        TableModel table = Table(ParseXml("<root><r><n>1</n><n>2</n><m>x</m></r><r><m>y</m></r></root>"), XmlRoot);

        Assert.Equal(new[] { new TableColumn(TableColumnKind.ChildElement, "m") }, table.Columns);
    }

    [Fact]
    public void A_child_name_that_is_not_text_only_in_some_row_is_not_a_column()
    {
        TableModel table = Table(ParseXml("<root><r><n>1</n><k>2</k></r><r><n><j/></n></r></root>"), XmlRoot);

        Assert.Equal(new[] { new TableColumn(TableColumnKind.ChildElement, "k") }, table.Columns);
    }

    [Fact]
    public void An_empty_container_written_across_lines_is_not_text_only()
    {
        TableModel table = Table(ParseXml("<root><r><n>\n</n></r><r><n>x</n></r></root>"), XmlRoot);

        Assert.Empty(table.Columns);
    }

    [Fact]
    public void A_self_closing_child_counts_as_text_only()
    {
        TableModel table = Table(ParseXml("<root><r><n/></r><r><n>x</n></r></root>"), XmlRoot);

        Assert.Equal(new[] { new TableColumn(TableColumnKind.ChildElement, "n") }, table.Columns);
    }

    [Fact]
    public void The_text_column_follows_the_children_when_some_row_has_text()
    {
        TableModel table = Table(ParseXml("""<root><r id="1">hi</r><r id="2"/></root>"""), XmlRoot);

        Assert.Equal(
            new[] { new TableColumn(TableColumnKind.Attribute, "id"), new TableColumn(TableColumnKind.Text, "text") },
            table.Columns);
    }

    [Fact]
    public void The_text_column_is_absent_when_every_text_is_empty()
    {
        TableModel table = Table(ParseXml("<root><r></r><r/></root>"), XmlRoot);

        Assert.Empty(table.Columns);
    }

    [Fact]
    public void Attribute_then_child_then_text_columns_come_in_that_order()
    {
        TableModel table = Table(ParseXml("""<root><r t="1" k="0"><n>2</n></r><r k="3"><n>4</n></r><r>y</r></root>"""), XmlRoot);

        Assert.Equal(
            new[]
            {
                new TableColumn(TableColumnKind.Attribute, "t"),
                new TableColumn(TableColumnKind.Attribute, "k"),
                new TableColumn(TableColumnKind.ChildElement, "n"),
                new TableColumn(TableColumnKind.Text, "text"),
            },
            table.Columns);
    }

    // ----- Cells: JSON -----

    private static readonly string JsonCells = """[{"a": 1, "b": "x", "c": [1, 2]}, {"a": {"k": 1}, "d": null}]""";

    [Fact]
    public void A_scalar_member_cell_has_its_value_part_and_display()
    {
        TableModel table = Table(ParseJson(JsonCells), RootValue);

        TableCell cell = table.Cell(0, 0);

        Assert.Equal(At(Item(0), Item(0), Member("a")), cell.Node!.Path);
        Assert.Equal(PartKind.Value, cell.Part!.Kind);
        Assert.Equal("1", cell.Part.Value);
        Assert.False(cell.IsMissing);
        Assert.False(cell.IsNested);
        Assert.Equal("1", cell.Display);
    }

    [Fact]
    public void A_string_member_cell_displays_the_decoded_text_without_quotes()
    {
        TableCell cell = Table(ParseJson(JsonCells), RootValue).Cell(0, 1);

        Assert.Equal("x", cell.Display);
        Assert.Equal("x", cell.Part!.Value);
    }

    [Fact]
    public void A_nested_array_member_cell_displays_its_count_and_has_no_part()
    {
        TableCell cell = Table(ParseJson(JsonCells), RootValue).Cell(0, 2);

        Assert.True(cell.IsNested);
        Assert.Null(cell.Part);
        Assert.Equal("[2]", cell.Display);
        Assert.Equal(At(Item(0), Item(0), Member("c")), cell.Node!.Path);
    }

    [Fact]
    public void A_nested_object_member_cell_displays_its_count()
    {
        TableCell cell = Table(ParseJson(JsonCells), RootValue).Cell(1, 0);

        Assert.True(cell.IsNested);
        Assert.Null(cell.Part);
        Assert.Equal("{1}", cell.Display);
    }

    [Fact]
    public void A_missing_member_cell_has_no_node_and_an_empty_display()
    {
        TableModel table = Table(ParseJson(JsonCells), RootValue);

        TableCell cell = table.Cell(0, 3);

        Assert.True(cell.IsMissing);
        Assert.Null(cell.Node);
        Assert.Null(cell.Part);
        Assert.False(cell.IsNested);
        Assert.Equal(string.Empty, cell.Display);
    }

    [Fact]
    public void A_null_member_displays_null_and_booleans_display_as_written()
    {
        TableModel table = Table(ParseJson("""[{"a": null, "b": false}]"""), RootValue);

        Assert.Equal("null", table.Cell(0, 0).Display);
        Assert.Equal("false", table.Cell(0, 1).Display);
    }

    [Fact]
    public void A_scalar_array_cell_is_the_item_itself()
    {
        TableModel table = Table(ParseJson("""[10, "s", false]"""), RootValue);

        TableCell first = table.Cell(0, 0);
        Assert.Equal(Row(0), first.Node!.Path);
        Assert.Equal(PartKind.Value, first.Part!.Kind);
        Assert.Equal("10", first.Display);
        Assert.Equal("s", table.Cell(1, 0).Display);
        Assert.Equal("false", table.Cell(2, 0).Display);
    }

    // ----- Cells: XML -----

    private const string XmlCells = """<root><r id="1" note="a"><n>x</n><m/></r><r id="2"><n>y</n></r></root>""";

    [Fact]
    public void An_attribute_cell_is_the_value_part_of_its_row_element()
    {
        TableModel table = Table(ParseXml(XmlCells), XmlRoot);

        TableCell cell = table.Cell(0, 0);

        Assert.Equal(XmlRow("r", 0), cell.Node!.Path);
        Assert.Equal(PartKind.AttributeValue, cell.Part!.Kind);
        Assert.Equal(0, cell.Part.AttributeIndex);
        Assert.Equal("1", cell.Display);
        Assert.False(cell.IsMissing);
        Assert.Equal("a", table.Cell(0, 1).Display);
    }

    [Fact]
    public void A_child_element_cell_is_the_text_part_of_the_child()
    {
        TableCell cell = Table(ParseXml(XmlCells), XmlRoot).Cell(0, 2);

        Assert.Equal(At(Element("root"), Element("r", 0), Element("n", 0)), cell.Node!.Path);
        Assert.Equal(PartKind.Text, cell.Part!.Kind);
        Assert.Equal("x", cell.Display);
        Assert.False(cell.IsNested);
    }

    [Fact]
    public void An_empty_child_element_cell_is_present_with_an_empty_display()
    {
        TableCell cell = Table(ParseXml(XmlCells), XmlRoot).Cell(0, 3);

        Assert.False(cell.IsMissing);
        Assert.Null(cell.Part);
        Assert.Equal(At(Element("root"), Element("r", 0), Element("m", 0)), cell.Node!.Path);
        Assert.Equal(string.Empty, cell.Display);
    }

    [Fact]
    public void A_missing_attribute_cell_is_missing_with_its_row_as_the_node()
    {
        TableCell cell = Table(ParseXml(XmlCells), XmlRoot).Cell(1, 1);

        Assert.True(cell.IsMissing);
        Assert.Null(cell.Part);
        Assert.Equal(XmlRow("r", 1), cell.Node!.Path);
        Assert.Equal(string.Empty, cell.Display);
    }

    [Fact]
    public void A_missing_child_element_cell_has_no_node()
    {
        TableCell cell = Table(ParseXml(XmlCells), XmlRoot).Cell(1, 3);

        Assert.True(cell.IsMissing);
        Assert.Null(cell.Node);
        Assert.Equal(string.Empty, cell.Display);
    }

    [Fact]
    public void The_text_cell_of_a_text_only_row_is_its_text()
    {
        TableModel table = Table(ParseXml("<root><r>hello</r><r/><r></r></root>"), XmlRoot);

        TableCell text = table.Cell(0, 0);
        Assert.Equal(PartKind.Text, text.Part!.Kind);
        Assert.Equal("hello", text.Display);
        Assert.Equal(XmlRow("r", 0), text.Node!.Path);
    }

    [Fact]
    public void The_text_cell_of_a_self_closing_row_is_missing()
    {
        TableCell cell = Table(ParseXml("<root><r>hello</r><r/><r></r></root>"), XmlRoot).Cell(1, 0);

        Assert.True(cell.IsMissing);
        Assert.Null(cell.Part);
        Assert.Equal(XmlRow("r", 1), cell.Node!.Path);
    }

    [Fact]
    public void An_empty_text_element_has_an_empty_text_part_and_is_not_missing()
    {
        TableCell cell = Table(ParseXml("<root><r>hello</r><r/><r></r></root>"), XmlRoot).Cell(2, 0);

        Assert.False(cell.IsMissing);
        Assert.Equal(PartKind.Text, cell.Part!.Kind);
        Assert.Equal(string.Empty, cell.Display);
    }

    [Fact]
    public void The_text_cell_of_a_row_with_element_children_is_nested()
    {
        TableCell cell = Table(ParseXml("<root><r>x</r><r><k/><k/></r></root>"), XmlRoot).Cell(1, 0);

        Assert.True(cell.IsNested);
        Assert.Null(cell.Part);
        Assert.Equal(NestedXml, cell.Display);
    }

    [Fact]
    public void Cell_rejects_a_row_or_column_outside_the_table()
    {
        TableModel table = Table(ParseJson("""[{"a": 1}]"""), RootValue);

        Assert.Throws<ArgumentOutOfRangeException>(() => table.Cell(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.Cell(0, 1));
    }

    // ----- Edits: JSON -----

    [Fact]
    public void Editing_an_existing_JSON_value_sets_its_part()
    {
        const string text = """[{"a": 1}, {"a": 2}]""";
        TableModel table = Table(ParseJson(text), RootValue);

        EditOp op = table.EditCell(0, 0, "5");

        Assert.IsType<SetPart>(op);
        Assert.Equal("""[{"a": 5}, {"a": 2}]""", Edit(text, Strict, op));
    }

    [Fact]
    public void A_missing_JSON_member_is_inserted_before_the_first_member_of_a_later_column()
    {
        const string text = """[{"a": "1", "b": "2", "c": "3"}, {"a": "x", "c": "y"}]""";
        TableModel table = Table(ParseJson(text), RootValue);

        string after = Edit(text, Strict, table.EditCell(1, 1, "k"));

        Assert.Equal("""[{"a": "1", "b": "2", "c": "3"}, {"a": "x", "b": "k", "c": "y"}]""", after);
        Assert.Equal("k", Table(ParseJson(after), RootValue).Cell(1, 1).Display);
    }

    [Fact]
    public void A_missing_JSON_member_goes_at_the_end_when_no_later_column_exists()
    {
        const string text = """[{"a": "1", "c": "3"}, {"a": "4"}]""";
        TableModel table = Table(ParseJson(text), RootValue);

        string after = Edit(text, Strict, table.EditCell(1, 1, "9"));

        Assert.Equal("""[{"a": "1", "c": "3"}, {"a": "4", "c": "9"}]""", after);
    }

    [Fact]
    public void A_missing_JSON_number_takes_the_column_kind_and_its_position()
    {
        const string text = """[{"a": 1}, {"b": "x"}]""";
        TableModel table = Table(ParseJson(text), RootValue);

        string after = Edit(text, Strict, table.EditCell(1, 0, "7"));

        Assert.Equal("""[{"a": 1}, {"a": 7, "b": "x"}]""", after);
    }

    [Fact]
    public void A_tie_between_kinds_gives_a_string()
    {
        const string text = """[{"a": 1}, {"a": "s"}, {}]""";
        TableModel table = Table(ParseJson(text), RootValue);

        string after = Edit(text, Strict, table.EditCell(2, 0, "5"));

        Assert.Equal("""[{"a": 1}, {"a": "s"}, {"a": "5"}]""", after);
    }

    [Fact]
    public void Booleans_count_by_their_own_kind_and_the_value_text_is_ignored()
    {
        const string text = """[{"a": true}, {"a": false}, {"a": false}, {"b": 1}]""";
        TableModel table = Table(ParseJson(text), RootValue);

        string after = Edit(text, Strict, table.EditCell(3, 0, "ignored"));

        Assert.Equal("""[{"a": true}, {"a": false}, {"a": false}, {"a": false, "b": 1}]""", after);
    }

    [Fact]
    public void A_missing_null_member_is_inserted_as_null()
    {
        const string text = """[{"a": null}, {"b": 1}]""";
        TableModel table = Table(ParseJson(text), RootValue);

        string after = Edit(text, Strict, table.EditCell(1, 0, "x"));

        Assert.Equal("""[{"a": null}, {"a": null, "b": 1}]""", after);
    }

    [Fact]
    public void MissingKind_is_the_kind_a_missing_member_is_inserted_with_and_null_for_other_tables()
    {
        TableModel flags = Table(ParseJson("""[{"a": true}, {"b": 1}]"""), RootValue);
        Assert.Equal(JsonValueKind.True, flags.MissingKind(0));
        Assert.Equal(JsonValueKind.Number, flags.MissingKind(1));

        Assert.Null(Table(ParseJson("[1, 2]"), RootValue).MissingKind(0));
        Assert.Null(Table(ParseXml("""<root><r a="1"/><r a="2"/></root>"""), XmlRoot).MissingKind(0));
    }

    [Fact]
    public void A_missing_member_is_inserted_with_the_kind_given_and_a_boolean_ignores_the_value_text()
    {
        const string text = """[{"a": true}, {"b": 1}]""";
        TableModel table = Table(ParseJson(text), RootValue);

        string after = Edit(text, Strict, table.EditCell(1, 0, JsonValueKind.False, "true"));

        Assert.Equal("""[{"a": true}, {"a": false, "b": 1}]""", after);
    }

    [Fact]
    public void A_missing_member_takes_a_string_or_a_number_given_with_its_text_whatever_the_column_kind()
    {
        const string numbers = """[{"a": 1}, {"b": "x"}]""";
        Assert.Equal("""[{"a": 1}, {"a": "x", "b": "x"}]""", Edit(numbers, Strict, Table(ParseJson(numbers), RootValue).EditCell(1, 0, JsonValueKind.String, "x")));

        const string strings = """[{"a": "s"}, {}]""";
        Assert.Equal("""[{"a": "s"}, {"a": 7}]""", Edit(strings, Strict, Table(ParseJson(strings), RootValue).EditCell(1, 0, JsonValueKind.Number, "7")));
    }

    [Fact]
    public void An_existing_value_changes_its_type_with_the_kind_given()
    {
        const string text = """[{"a": "x"}, {"a": 2}]""";
        EditOp toNull = Table(ParseJson(text), RootValue).EditCell(0, 0, JsonValueKind.Null, null);
        Assert.IsType<ChangeJsonType>(toNull);
        Assert.Equal("""[{"a": null}, {"a": 2}]""", Edit(text, Strict, toNull));

        const string numbers = """[{"a": 2}]""";
        Assert.Equal("""[{"a": "two"}]""", Edit(numbers, Strict, Table(ParseJson(numbers), RootValue).EditCell(0, 0, JsonValueKind.String, "two")));
    }

    [Fact]
    public void An_existing_string_or_number_of_the_same_kind_is_set_with_its_text()
    {
        const string text = """[{"a": "x"}]""";
        EditOp op = Table(ParseJson(text), RootValue).EditCell(0, 0, JsonValueKind.String, "y");

        Assert.IsType<SetPart>(op);
        Assert.Equal("""[{"a": "y"}]""", Edit(text, Strict, op));
    }

    [Fact]
    public void A_boolean_or_null_that_already_has_the_kind_given_is_refused()
    {
        TableModel table = Table(ParseJson("""[{"a": true}]"""), RootValue);

        CfgeditException ex = Assert.Throws<CfgeditException>(() => table.EditCell(0, 0, JsonValueKind.True, null));
        Assert.Equal("this value is already of that type", ex.Message);
    }

    [Fact]
    public void The_kind_overload_refuses_XML_nested_cells_and_container_kinds()
    {
        Assert.Throws<CfgeditException>(() => Table(ParseXml("""<root><r a="1"/><r a="2"/></root>"""), XmlRoot).EditCell(0, 0, JsonValueKind.String, "x"));
        Assert.Throws<CfgeditException>(() => Table(ParseJson("""[{"a": {"k": 1}}]"""), RootValue).EditCell(0, 0, JsonValueKind.String, "x"));
        Assert.Throws<ArgumentException>(() => Table(ParseJson("""[{"a": 1}]"""), RootValue).EditCell(0, 0, JsonValueKind.Object, "x"));
    }

    [Fact]
    public void An_array_item_changes_its_type_with_the_kind_given()
    {
        const string text = "[1, 2]";
        EditOp op = Table(ParseJson(text), RootValue).EditCell(0, 0, JsonValueKind.Null, null);

        Assert.IsType<ChangeJsonType>(op);
        Assert.Equal("[null, 2]", Edit(text, Strict, op));
    }

    [Fact]
    public void Editing_a_scalar_array_item_sets_its_value()
    {
        const string text = """[1, 2]""";
        TableModel table = Table(ParseJson(text), RootValue);

        Assert.Equal("[1, 3]", Edit(text, Strict, table.EditCell(1, 0, "3")));

        const string strings = """["a", "b"]""";
        Assert.Equal("""["z", "b"]""", Edit(strings, Strict, Table(ParseJson(strings), RootValue).EditCell(0, 0, "z")));
    }

    [Fact]
    public void Editing_a_nested_JSON_cell_is_refused()
    {
        TableModel table = Table(ParseJson("""[{"a": {"k": 1}}]"""), RootValue);

        Assert.Throws<CfgeditException>(() => table.EditCell(0, 0, "x"));
    }

    [Fact]
    public void Editing_a_row_outside_the_table_is_refused()
    {
        TableModel table = Table(ParseJson("""[{"a": 1}]"""), RootValue);

        Assert.Throws<CfgeditException>(() => table.EditCell(5, 0, "x"));
    }

    [Fact]
    public void Clearing_a_JSON_member_deletes_it()
    {
        const string text = """[{"a": 1, "b": 2}, {"a": 3}]""";
        TableModel table = Table(ParseJson(text), RootValue);

        EditOp? op = table.ClearCell(0, 1);

        Assert.IsType<DeleteNode>(op);
        Assert.Equal("""[{"a": 1}, {"a": 3}]""", Edit(text, Strict, op));
    }

    [Fact]
    public void Clearing_a_missing_JSON_cell_gives_null()
    {
        TableModel table = Table(ParseJson("""[{"a": 1, "b": 2}, {"a": 3}]"""), RootValue);

        Assert.Null(table.ClearCell(1, 1));
    }

    [Fact]
    public void Clearing_a_nested_JSON_cell_is_refused()
    {
        TableModel table = Table(ParseJson("""[{"a": {"k": 1}}]"""), RootValue);

        Assert.Throws<CfgeditException>(() => table.ClearCell(0, 0));
    }

    [Fact]
    public void Clearing_a_scalar_array_value_is_refused()
    {
        TableModel table = Table(ParseJson("[1]"), RootValue);

        Assert.Throws<CfgeditException>(() => table.ClearCell(0, 0));
    }

    [Fact]
    public void Deleting_a_row_deletes_the_item()
    {
        const string text = """[{"a": 1}, {"a": 2}]""";

        string after = Edit(text, Strict, Table(ParseJson(text), RootValue).DeleteRow(0));

        Assert.Equal("""[{"a": 2}]""", after);
    }

    [Fact]
    public void Adding_a_row_before_the_first_copies_the_shape_with_empty_values()
    {
        const string text = """[{"a": 1, "b": "x"}, {"a": 2, "b": "y"}]""";

        string after = Edit(text, Strict, Table(ParseJson(text), RootValue).AddRow(-1));

        Assert.Equal("""[{"a": 0, "b": ""}, {"a": 1, "b": "x"}, {"a": 2, "b": "y"}]""", after);
    }

    [Fact]
    public void Adding_a_row_in_the_middle_copies_the_row_before_it()
    {
        const string text = """[{"a": 1, "b": "x"}, {"a": 2, "b": "y"}]""";

        string after = Edit(text, Strict, Table(ParseJson(text), RootValue).AddRow(0));

        Assert.Equal("""[{"a": 1, "b": "x"}, {"a": 0, "b": ""}, {"a": 2, "b": "y"}]""", after);
    }

    [Fact]
    public void Adding_a_row_at_the_end_copies_the_last_row()
    {
        const string text = """[{"a": 1, "b": "x"}, {"a": 2, "b": "y"}]""";

        string after = Edit(text, Strict, Table(ParseJson(text), RootValue).AddRow(1));

        Assert.Equal("""[{"a": 1, "b": "x"}, {"a": 2, "b": "y"}, {"a": 0, "b": ""}]""", after);
    }

    [Fact]
    public void Adding_a_row_gives_empty_values_of_each_kind_and_empty_containers()
    {
        const string text = """[{"a": 1, "flag": true, "obj": {"k": 1}, "arr": [1]}]""";

        string after = Edit(text, Strict, Table(ParseJson(text), RootValue).AddRow(0));

        Assert.Equal(
            """[{"a": 1, "flag": true, "obj": {"k": 1}, "arr": [1]}, {"a": 0, "flag": false, "obj": {}, "arr": []}]""",
            after);
    }

    [Fact]
    public void Adding_a_scalar_row_gives_an_empty_value_of_the_same_kind()
    {
        Assert.Equal("[1, 0, 2]", Edit("[1, 2]", Strict, Table(ParseJson("[1, 2]"), RootValue).AddRow(0)));
        Assert.Equal("""["", "a", 1]""", Edit("""["a", 1]""", Strict, Table(ParseJson("""["a", 1]"""), RootValue).AddRow(-1)));
        Assert.Equal("[true, false]", Edit("[true]", Strict, Table(ParseJson("[true]"), RootValue).AddRow(0)));
    }

    [Fact]
    public void An_added_row_is_a_row_of_the_rebuilt_table()
    {
        const string text = """[{"a": 1, "b": "x"}, {"a": 2, "b": "y"}]""";
        TableModel table = Table(ParseJson(text), RootValue);

        ConfigDocument after = ModelTestSupport.Parse(Edit(text, Strict, table.AddRow(-1)), Strict);
        TableModel rebuilt = Table(after, RootValue);

        Assert.Equal(3, rebuilt.Rows.Count);
        Assert.Equal("0", rebuilt.Cell(0, 0).Display);
        Assert.Equal(string.Empty, rebuilt.Cell(0, 1).Display);
    }

    [Fact]
    public void Adding_a_row_after_a_row_that_does_not_exist_is_refused()
    {
        TableModel table = Table(ParseJson("""[{"a": 1}, {"a": 2}]"""), RootValue);

        Assert.Throws<CfgeditException>(() => table.AddRow(2));
    }

    [Fact]
    public void Moving_a_JSON_row_to_the_end()
    {
        const string text = """[{"n": 1}, {"n": 2}, {"n": 3}]""";

        Assert.Equal("""[{"n": 2}, {"n": 3}, {"n": 1}]""", Edit(text, Strict, Table(ParseJson(text), RootValue).MoveRow(0, 2)));
    }

    [Fact]
    public void Moving_a_JSON_row_to_the_start()
    {
        const string text = """[{"n": 1}, {"n": 2}, {"n": 3}]""";

        Assert.Equal("""[{"n": 3}, {"n": 1}, {"n": 2}]""", Edit(text, Strict, Table(ParseJson(text), RootValue).MoveRow(2, 0)));
    }

    [Fact]
    public void Moving_a_JSON_row_one_place_down()
    {
        const string text = """[{"n": 1}, {"n": 2}, {"n": 3}]""";

        Assert.Equal("""[{"n": 2}, {"n": 1}, {"n": 3}]""", Edit(text, Strict, Table(ParseJson(text), RootValue).MoveRow(0, 1)));
    }

    [Fact]
    public void Moving_a_row_to_its_own_place_changes_nothing()
    {
        const string text = """[{"n": 1}, {"n": 2}, {"n": 3}]""";

        Assert.Equal(text, Edit(text, Strict, Table(ParseJson(text), RootValue).MoveRow(1, 1)));
    }

    // ----- Edits: XML -----

    [Fact]
    public void Editing_an_existing_attribute_sets_its_value()
    {
        const string text = """<root><r a="1" b="2"/><r a="3"/></root>""";

        string after = Edit(text, XmlFormat,Table(ParseXml(text), XmlRoot).EditCell(0, 0, "9"));

        Assert.Equal("""<root><r a="9" b="2"/><r a="3"/></root>""", after);
    }

    [Fact]
    public void A_missing_attribute_is_appended_when_it_is_the_last_column()
    {
        const string text = """<root><r a="1" b="2"/><r a="3"/></root>""";

        string after = Edit(text, XmlFormat,Table(ParseXml(text), XmlRoot).EditCell(1, 1, "8"));

        Assert.Equal("""<root><r a="1" b="2"/><r a="3" b="8"/></root>""", after);
        Assert.Equal("8", Table(ParseXml(after), XmlRoot).Cell(1, 1).Display);
    }

    [Fact]
    public void A_missing_attribute_is_inserted_before_a_later_column_attribute()
    {
        const string text = """<root><r b="1" d="2"/><r d="3"/></root>""";

        string after = Edit(text, XmlFormat,Table(ParseXml(text), XmlRoot).EditCell(1, 0, "X"));

        Assert.Equal("""<root><r b="1" d="2"/><r b="X" d="3"/></root>""", after);
    }

    [Fact]
    public void A_missing_child_element_is_inserted_before_a_later_column_child()
    {
        const string text = "<root><r><n>1</n><m>2</m></r><r><m>3</m></r></root>";

        string after = Edit(text, XmlFormat,Table(ParseXml(text), XmlRoot).EditCell(1, 0, "5"));

        Assert.Equal("<root><r><n>1</n><m>2</m></r><r><n>5</n><m>3</m></r></root>", after);
        Assert.Equal("5", Table(ParseXml(after), XmlRoot).Cell(1, 0).Display);
    }

    [Fact]
    public void A_missing_child_element_is_inserted_after_the_last_earlier_column_child()
    {
        const string text = "<root><r><n>1</n></r><r><m>3</m></r></root>";

        string after = Edit(text, XmlFormat,Table(ParseXml(text), XmlRoot).EditCell(0, 1, "4"));

        Assert.Equal("<root><r><n>1</n><m>4</m></r><r><m>3</m></r></root>", after);
    }

    [Fact]
    public void A_missing_child_element_goes_into_a_row_without_children()
    {
        const string text = "<root><r><n>1</n></r><r/></root>";

        string after = Edit(text, XmlFormat,Table(ParseXml(text), XmlRoot).EditCell(1, 0, "2"));

        // A child goes into an element that had no content on its own line, and the new line is indented one unit.
        Assert.Equal("<root><r><n>1</n></r><r>\n  <n>2</n>\n</r></root>", after);
    }

    [Fact]
    public void Editing_an_existing_self_closing_child_sets_its_text()
    {
        const string text = "<root><r><n/></r><r><n>x</n></r></root>";

        string after = Edit(text, XmlFormat,Table(ParseXml(text), XmlRoot).EditCell(0, 0, "v"));

        Assert.Equal("<root><r><n>v</n></r><r><n>x</n></r></root>", after);
    }

    [Fact]
    public void Editing_an_existing_text_cell_sets_the_text()
    {
        const string text = "<root><r>x</r><r/></root>";

        Assert.Equal("<root><r>y</r><r/></root>", Edit(text, XmlFormat,Table(ParseXml(text), XmlRoot).EditCell(0, 0, "y")));
    }

    [Fact]
    public void Editing_the_text_of_a_self_closing_row_gives_it_text()
    {
        const string text = "<root><r>x</r><r/></root>";

        string after = Edit(text, XmlFormat, Table(ParseXml(text), XmlRoot).EditCell(1, 0, "hi"));

        Assert.Equal("<root><r>x</r><r>hi</r></root>", after);
        Assert.Equal("hi", Table(ParseXml(after), XmlRoot).Cell(1, 0).Display);
    }

    [Fact]
    public void Editing_the_text_of_a_row_with_child_elements_is_refused_with_its_message()
    {
        TableModel table = Table(ParseXml("<root><r>x</r><r><k/><k/></r></root>"), XmlRoot);

        CfgeditException ex = Assert.Throws<CfgeditException>(() => table.EditCell(1, 0, "z"));

        Assert.Equal("this row has child elements; edit its text in the tree", ex.Message);
    }

    [Fact]
    public void Clearing_an_attribute_deletes_it()
    {
        const string text = """<root><r a="1" b="2"/><r a="3"/></root>""";

        EditOp? op = Table(ParseXml(text), XmlRoot).ClearCell(0, 0);
        Assert.NotNull(op);

        Assert.Equal("""<root><r b="2"/><r a="3"/></root>""", Edit(text, XmlFormat,op));
    }

    [Fact]
    public void Clearing_a_child_element_deletes_it()
    {
        const string text = "<root><r><n>1</n><m>2</m></r><r><m>3</m></r></root>";

        EditOp? op = Table(ParseXml(text), XmlRoot).ClearCell(0, 1);
        Assert.NotNull(op);

        Assert.Equal("<root><r><n>1</n></r><r><m>3</m></r></root>", Edit(text, XmlFormat,op));
    }

    [Fact]
    public void Clearing_a_missing_XML_cell_gives_null()
    {
        TableModel table = Table(ParseXml("<root><r><n>1</n><m>2</m></r><r><m>3</m></r></root>"), XmlRoot);

        Assert.Null(table.ClearCell(1, 0));
    }

    [Fact]
    public void Clearing_the_text_empties_the_text_into_an_open_and_close_pair()
    {
        const string text = "<root><r>x</r><r/></root>";

        EditOp? op = Table(ParseXml(text), XmlRoot).ClearCell(0, 0);
        Assert.NotNull(op);
        Assert.Equal("<root><r></r><r/></root>", Edit(text, XmlFormat, op));
    }

    [Fact]
    public void Clearing_the_text_of_a_row_with_child_elements_is_refused()
    {
        TableModel table = Table(ParseXml("<root><r>x</r><r><k/><k/></r></root>"), XmlRoot);

        Assert.Throws<CfgeditException>(() => table.ClearCell(1, 0));
    }

    [Fact]
    public void Deleting_an_XML_row_deletes_the_element()
    {
        const string text = "<root><r>x</r><r>y</r></root>";

        Assert.Equal("<root><r>y</r></root>", Edit(text, XmlFormat,Table(ParseXml(text), XmlRoot).DeleteRow(0)));
    }

    [Fact]
    public void Adding_an_XML_row_copies_its_attributes_and_child_elements_as_empty()
    {
        const string text = """<root><r a="1"><n>x</n><m>y</m></r><r a="2"><n>z</n></r></root>""";

        string after = Edit(text, XmlFormat,Table(ParseXml(text), XmlRoot).AddRow(-1));

        Assert.Equal(
            "<root><r a=\"\">\n  <n />\n  <m />\n</r><r a=\"1\"><n>x</n><m>y</m></r><r a=\"2\"><n>z</n></r></root>",
            after);
    }

    [Fact]
    public void Adding_an_XML_row_after_a_text_only_row_has_an_empty_element()
    {
        const string text = "<root><r>x</r><r>y</r></root>";

        string after = Edit(text, XmlFormat,Table(ParseXml(text), XmlRoot).AddRow(0));

        Assert.Equal("<root><r>x</r><r /><r>y</r></root>", after);
    }

    [Fact]
    public void Moving_XML_rows_past_other_siblings_and_comments()
    {
        const string text = """<root><item n="1"/><other/><item n="2"/><!--c--><item n="3"/></root>""";
        TableModel table = Table(ParseXml(text), XmlRoot);

        Assert.Equal(
            """<root><other/><item n="2"/><!--c--><item n="3"/><item n="1"/></root>""",
            Edit(text, XmlFormat,table.MoveRow(0, 2)));
        Assert.Equal(
            """<root><item n="3"/><item n="1"/><other/><item n="2"/><!--c--></root>""",
            Edit(text, XmlFormat,table.MoveRow(2, 0)));
        Assert.Equal(
            """<root><other/><item n="2"/><!--c--><item n="1"/><item n="3"/></root>""",
            Edit(text, XmlFormat,table.MoveRow(0, 1)));
        Assert.Equal(
            """<root><item n="1"/><other/><item n="3"/><item n="2"/><!--c--></root>""",
            Edit(text, XmlFormat, table.MoveRow(1, 2)));
    }

    [Fact]
    public void A_comment_set_off_by_blank_lines_stays_in_place_when_a_row_moves_past_it()
    {
        const string text = "<root>\n  <item n=\"1\"/>\n  <other/>\n  <item n=\"2\"/>\n\n  <!--c-->\n\n  <item n=\"3\"/>\n</root>";
        TableModel table = Table(ParseXml(text), XmlRoot);

        string after = Edit(text, XmlFormat, table.MoveRow(0, 1));

        Assert.Equal(
            "<root>\n  <other/>\n  <item n=\"2\"/>\n\n  <!--c-->\n\n  <item n=\"1\"/>\n  <item n=\"3\"/>\n</root>",
            after);
    }

    private static ConfigDocument ParseJsonc(string text) => ModelTestSupport.Parse(text, Jsonc);
}
