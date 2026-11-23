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

    // ----- TOML -----

    private const string TwoItems = "[[items]]\nname = \"web\"\nport = 8080\n[[items]]\nname = \"db\"\ntags = \"x\"\n";

    private const string TwoPoints = "points = [{x = 1, y = 2}, {x = 3}]\n";

    private static ConfigDocument ParseToml(string text) => ModelTestSupport.Parse(text, EditTestSupport.Toml);

    private static NodePath ArrayAt(string key) => At(Item(0), Member(key));

    private static NodePath TomlItem(string key, int index) => At(Item(0), Member(key), Item(index));

    // ----- TryCreate: TOML -----

    [Fact]
    public void An_array_of_tables_is_a_table_whose_rows_are_its_items()
    {
        TableModel table = Table(ParseToml(TwoItems), ArrayAt("items"));

        Assert.Equal(ArrayAt("items"), table.Container);
        Assert.Equal(new[] { TomlItem("items", 0), TomlItem("items", 1) }, table.Rows);
    }

    [Fact]
    public void A_row_of_an_array_of_tables_gives_the_array_as_the_container()
    {
        TableModel table = Table(ParseToml(TwoItems), TomlItem("items", 1));

        Assert.Equal(ArrayAt("items"), table.Container);
        Assert.Equal(2, table.Rows.Count);
    }

    [Fact]
    public void An_inline_array_of_inline_tables_is_a_table_and_its_comments_are_not_rows()
    {
        const string text = "points = [\n  # first\n  {x = 1},\n  {x = 2}, # second\n]\n";

        TableModel table = Table(ParseToml(text), TomlItem("points", 1));

        Assert.Equal(ArrayAt("points"), table.Container);
        Assert.Equal(new[] { TomlItem("points", 0), TomlItem("points", 1) }, table.Rows);
    }

    [Fact]
    public void An_inline_array_of_scalars_is_a_table_with_one_value_column()
    {
        TableModel table = Table(ParseToml("nums = [10, \"x\", true]\n"), TomlItem("nums", 2));

        Assert.Equal(ArrayAt("nums"), table.Container);
        Assert.Equal(new[] { new TableColumn(TableColumnKind.Value, "value") }, table.Columns);
        Assert.Equal(3, table.Rows.Count);
    }

    [Theory]
    [InlineData("a = [{x = 1}, 2]\n")]
    [InlineData("a = [[1], [2]]\n")]
    [InlineData("a = []\n")]
    [InlineData("a = [{x = 1}, [2]]\n")]
    public void A_TOML_array_that_is_not_a_table_gives_null(string text)
    {
        Assert.Null(TableModel.TryCreate(ParseToml(text), ArrayAt("a")));
    }

    [Fact]
    public void A_TOML_table_member_is_not_a_table()
    {
        Assert.Null(TableModel.TryCreate(ParseToml("[t]\nx = 1\n"), ArrayAt("t")));
    }

    // ----- Columns and cells: TOML -----

    [Fact]
    public void TOML_columns_are_the_union_of_the_keys_in_first_seen_order()
    {
        TableModel table = Table(ParseToml(TwoItems), ArrayAt("items"));

        Assert.Equal(
            new[] { new TableColumn(TableColumnKind.Key, "name"), new TableColumn(TableColumnKind.Key, "port"), new TableColumn(TableColumnKind.Key, "tags") },
            table.Columns);
        Assert.Equal("web", table.Cell(0, 0).Display);
        Assert.Equal("8080", table.Cell(0, 1).Display);
        Assert.Equal("x", table.Cell(1, 2).Display);
    }

    [Fact]
    public void A_TOML_key_absent_from_a_row_is_a_missing_cell()
    {
        TableModel table = Table(ParseToml(TwoItems), ArrayAt("items"));

        TableCell missing = table.Cell(0, 2);
        Assert.True(missing.IsMissing);
        Assert.Null(missing.Node);
        Assert.Equal(string.Empty, missing.Display);
        Assert.True(table.Cell(1, 1).IsMissing);
    }

    [Fact]
    public void A_TOML_subtable_and_array_are_nested_cells_that_show_their_counts()
    {
        const string text = "[[items]]\nname = \"a\"\nlist = [1, 2]\n[items.meta]\nx = 1\ny = 2\n";
        TableModel table = Table(ParseToml(text), ArrayAt("items"));

        Assert.Equal(
            new[]
            {
                new TableColumn(TableColumnKind.Key, "name"), new TableColumn(TableColumnKind.Key, "list"),
                new TableColumn(TableColumnKind.Key, "meta"),
            },
            table.Columns);
        Assert.True(table.Cell(0, 1).IsNested);
        Assert.Null(table.Cell(0, 1).Part);
        Assert.Equal("[2]", table.Cell(0, 1).Display);
        Assert.Equal("{2}", table.Cell(0, 2).Display);
    }

    [Fact]
    public void Inline_table_members_are_cells_and_an_absent_one_is_missing()
    {
        TableModel table = Table(ParseToml(TwoPoints), ArrayAt("points"));

        Assert.Equal("1", table.Cell(0, 0).Display);
        Assert.True(table.Cell(1, 1).IsMissing);
    }

    [Fact]
    public void Scalar_array_cells_are_the_items_themselves()
    {
        TableModel table = Table(ParseToml("nums = [10, \"x\", true]\n"), ArrayAt("nums"));

        Assert.Equal("10", table.Cell(0, 0).Display);
        Assert.Equal(TomlItem("nums", 0), table.Cell(0, 0).Node!.Path);
        Assert.Equal("x", table.Cell(1, 0).Display);
        Assert.Equal("true", table.Cell(2, 0).Display);
    }

    [Fact]
    public void A_TOML_cell_has_its_kind_for_the_editor_and_its_value_part()
    {
        TableModel table = Table(ParseToml(TwoItems), ArrayAt("items"));

        Assert.Equal(TomlValueKind.Integer, table.Cell(0, 1).Node!.TomlKind);
        Assert.Equal(PartKind.Value, table.Cell(0, 1).Part!.Kind);
    }

    // ----- Edits: TOML -----

    [Fact]
    public void Setting_a_TOML_cell_sets_its_value_part()
    {
        TableModel table = Table(ParseToml(TwoItems), ArrayAt("items"));

        EditOp op = table.EditCell(0, 0, "Web");

        Assert.IsType<SetPart>(op);
        Assert.Equal("[[items]]\nname = \"Web\"\nport = 8080\n[[items]]\nname = \"db\"\ntags = \"x\"\n", Edit(TwoItems, EditTestSupport.Toml, op));
    }

    [Fact]
    public void Setting_a_TOML_integer_cell_writes_the_number()
    {
        TableModel table = Table(ParseToml(TwoItems), ArrayAt("items"));

        EditOp op = table.EditCell(0, 1, "9000");

        Assert.Equal("[[items]]\nname = \"web\"\nport = 9000\n[[items]]\nname = \"db\"\ntags = \"x\"\n", Edit(TwoItems, EditTestSupport.Toml, op));
    }

    [Fact]
    public void A_missing_TOML_cell_gets_the_most_common_kind_of_its_column_with_the_typed_text()
    {
        TableModel table = Table(ParseToml(TwoItems), ArrayAt("items"));

        EditOp op = table.EditCell(1, 1, "5432");

        Assert.Equal(new InsertNode(TomlItem("items", 1), 1, new NewTomlValue("port", TomlNewKind.Integer, "5432")), op);
        Assert.Equal("[[items]]\nname = \"web\"\nport = 8080\n[[items]]\nname = \"db\"\nport = 5432\ntags = \"x\"\n", Edit(TwoItems, EditTestSupport.Toml, op));
    }

    [Fact]
    public void A_missing_TOML_string_cell_is_inserted_as_a_string_at_the_end_of_its_row()
    {
        TableModel table = Table(ParseToml(TwoItems), ArrayAt("items"));

        EditOp op = table.EditCell(0, 2, "t");

        Assert.Equal(new InsertNode(TomlItem("items", 0), 2, new NewTomlValue("tags", TomlNewKind.String, "t")), op);
        Assert.Equal("[[items]]\nname = \"web\"\nport = 8080\ntags = \"t\"\n[[items]]\nname = \"db\"\ntags = \"x\"\n", Edit(TwoItems, EditTestSupport.Toml, op));
    }

    [Fact]
    public void Date_time_forms_count_as_one_kind_for_a_missing_cell()
    {
        // Offset (1), integer (1) and local date (1): a tie if the four date-time forms were counted apart, a date-time once they are one kind.
        const string text = "[[e]]\nat = 1979-05-27T07:32:00Z\n[[e]]\nat = 3\n[[e]]\nat = 1979-05-28\n[[e]]\n";
        TableModel table = Table(ParseToml(text), ArrayAt("e"));

        EditOp op = table.EditCell(3, 0, "1979-06-01");

        Assert.Equal(new InsertNode(TomlItem("e", 3), 0, new NewTomlValue("at", TomlNewKind.DateTime, "1979-06-01")), op);
        Assert.Equal(text + "at = 1979-06-01\n", Edit(text, EditTestSupport.Toml, op));
    }

    [Fact]
    public void A_missing_TOML_cell_refuses_text_that_its_kind_does_not_take()
    {
        TableModel table = Table(ParseToml(TwoItems), ArrayAt("items"));

        CfgeditException error = Fails(TwoItems, EditTestSupport.Toml, table.EditCell(1, 1, "x"));

        Assert.Contains("'x' is not a valid integer", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_typed_edit_by_kind_is_refused_for_TOML_because_its_type_is_changed_in_the_tree()
    {
        TableModel table = Table(ParseToml(TwoItems), ArrayAt("items"));

        Assert.Throws<CfgeditException>(() => table.EditCell(0, 1, JsonValueKind.Number, "1"));
    }

    [Fact]
    public void Clearing_a_TOML_cell_deletes_its_key()
    {
        TableModel table = Table(ParseToml(TwoItems), ArrayAt("items"));

        EditOp? op = table.ClearCell(0, 1);

        Assert.IsType<DeleteNode>(op);
        Assert.Equal("[[items]]\nname = \"web\"\n[[items]]\nname = \"db\"\ntags = \"x\"\n", Edit(TwoItems, EditTestSupport.Toml, op));
    }

    [Fact]
    public void Clearing_a_missing_or_nested_TOML_cell()
    {
        const string text = "[[items]]\nname = \"a\"\nlist = [1, 2]\n[items.meta]\nx = 1\n[[items]]\nname = \"b\"\n";
        TableModel table = Table(ParseToml(text), ArrayAt("items"));

        Assert.Null(table.ClearCell(1, 1));
        Assert.Throws<CfgeditException>(() => table.ClearCell(0, 1));
    }

    [Fact]
    public void Adding_a_row_after_copies_the_shape_of_the_row_with_empty_values()
    {
        TableModel table = Table(ParseToml(TwoItems), ArrayAt("items"));

        CompositeEdit op = table.AddRow(0);

        Assert.Equal(
            "[[items]]\nname = \"web\"\nport = 8080\n[[items]]\nname = \"\"\nport = 0\n[[items]]\nname = \"db\"\ntags = \"x\"\n",
            Edit(TwoItems, EditTestSupport.Toml, op));
    }

    [Fact]
    public void Adding_a_row_before_the_first_copies_its_shape()
    {
        TableModel table = Table(ParseToml(TwoItems), ArrayAt("items"));

        Assert.Equal(
            "[[items]]\nname = \"\"\nport = 0\n[[items]]\nname = \"web\"\nport = 8080\n[[items]]\nname = \"db\"\ntags = \"x\"\n",
            Edit(TwoItems, EditTestSupport.Toml, table.AddRow(-1)));
    }

    [Fact]
    public void Adding_a_row_after_the_last_copies_the_last_row_at_the_end()
    {
        TableModel table = Table(ParseToml(TwoItems), ArrayAt("items"));

        Assert.Equal(
            TwoItems + "[[items]]\nname = \"\"\ntags = \"\"\n",
            Edit(TwoItems, EditTestSupport.Toml, table.AddRow(1)));
    }

    [Fact]
    public void Deleting_a_row_deletes_the_array_of_tables_item()
    {
        TableModel table = Table(ParseToml(TwoItems), ArrayAt("items"));

        Assert.Equal("[[items]]\nname = \"db\"\ntags = \"x\"\n", Edit(TwoItems, EditTestSupport.Toml, table.DeleteRow(0)));
    }

    [Fact]
    public void Moving_a_row_moves_the_array_of_tables_item()
    {
        TableModel table = Table(ParseToml(TwoItems), ArrayAt("items"));

        Assert.Equal(
            "[[items]]\nname = \"db\"\ntags = \"x\"\n[[items]]\nname = \"web\"\nport = 8080\n",
            Edit(TwoItems, EditTestSupport.Toml, table.MoveRow(0, 1)));
    }

    [Fact]
    public void Editing_an_inline_table_cell_sets_it_and_a_missing_one_is_inserted_in_the_column_order()
    {
        TableModel table = Table(ParseToml(TwoPoints), ArrayAt("points"));

        Assert.Equal("points = [{x = 5, y = 2}, {x = 3}]\n", Edit(TwoPoints, EditTestSupport.Toml, table.EditCell(0, 0, "5")));
        Assert.Equal("points = [{x = 1, y = 2}, {x = 3, y = 4}]\n", Edit(TwoPoints, EditTestSupport.Toml, table.EditCell(1, 1, "4")));
    }

    [Fact]
    public void Adding_a_row_of_an_inline_array_of_inline_tables_copies_the_shape_inline()
    {
        TableModel table = Table(ParseToml(TwoPoints), ArrayAt("points"));

        Assert.Equal("points = [{x = 1, y = 2}, {x = 0, y = 0}, {x = 3}]\n", Edit(TwoPoints, EditTestSupport.Toml, table.AddRow(0)));
    }

    [Fact]
    public void Deleting_and_moving_inline_table_rows()
    {
        TableModel table = Table(ParseToml(TwoPoints), ArrayAt("points"));

        Assert.Equal("points = [{x = 1, y = 2}]\n", Edit(TwoPoints, EditTestSupport.Toml, table.DeleteRow(1)));
        Assert.Equal("points = [{x = 3}, {x = 1, y = 2}]\n", Edit(TwoPoints, EditTestSupport.Toml, table.MoveRow(0, 1)));
    }

    [Fact]
    public void Scalar_array_rows_are_set_added_moved_and_deleted_and_cannot_be_cleared()
    {
        const string text = "nums = [10, 20, 30]\n";
        TableModel table = Table(ParseToml(text), ArrayAt("nums"));

        EditOp set = table.EditCell(1, 0, "25");
        Assert.IsType<SetPart>(set);
        Assert.Equal("nums = [10, 25, 30]\n", Edit(text, EditTestSupport.Toml, set));
        Assert.Equal("nums = [10, 0, 20, 30]\n", Edit(text, EditTestSupport.Toml, table.AddRow(0)));
        Assert.Equal("nums = [20, 30, 10]\n", Edit(text, EditTestSupport.Toml, table.MoveRow(0, 2)));
        Assert.Equal("nums = [10, 30]\n", Edit(text, EditTestSupport.Toml, table.DeleteRow(1)));
        Assert.Throws<CfgeditException>(() => table.ClearCell(0, 0));
    }

    [Fact]
    public void A_TOML_row_outside_the_table_is_refused()
    {
        TableModel table = Table(ParseToml(TwoItems), ArrayAt("items"));

        Assert.Throws<CfgeditException>(() => table.DeleteRow(2));
        Assert.Throws<CfgeditException>(() => table.EditCell(2, 0, "x"));
    }

    // ----- YAML -----

    private const string YamlItems = "services:\n  - name: web\n    port: 80\n  - name: db\n    tags: x\n";

    private const string YamlNumbers = "nums:\n  - 10\n  - 20\n  - 30\n";

    private static ConfigDocument ParseYaml(string text) => ModelTestSupport.Parse(text, EditTestSupport.Yaml);

    private static NodePath YamlSequence(string key) => At(Item(0), Member(key));

    private static NodePath YamlItem(string key, int index) => At(Item(0), Member(key), Item(index));

    // ----- TryCreate: YAML -----

    [Fact]
    public void A_YAML_sequence_of_mappings_is_a_table_whose_rows_are_its_items()
    {
        TableModel table = Table(ParseYaml(YamlItems), YamlSequence("services"));

        Assert.Equal(new[] { YamlItem("services", 0), YamlItem("services", 1) }, table.Rows);
        Assert.Equal(YamlSequence("services"), table.Container);
    }

    [Fact]
    public void A_row_path_gives_the_YAML_sequence_as_the_container()
    {
        TableModel table = Table(ParseYaml(YamlItems), YamlItem("services", 1));

        Assert.Equal(YamlSequence("services"), table.Container);
        Assert.Equal(2, table.Rows.Count);
    }

    [Fact]
    public void A_YAML_sequence_of_scalars_is_a_table_with_one_value_column()
    {
        TableModel table = Table(ParseYaml(YamlNumbers), YamlSequence("nums"));

        Assert.Equal(new[] { new TableColumn(TableColumnKind.Value, "value") }, table.Columns);
        Assert.Equal(YamlItem("nums", 2), table.Rows[2]);
        Assert.Equal("30", table.Cell(2, 0).Display);
        Assert.Equal(YamlValueKind.Integer, table.Cell(2, 0).Node!.YamlKind);
    }

    [Fact]
    public void A_YAML_sequence_mixing_mappings_and_scalars_is_not_a_table()
    {
        Assert.Null(TableModel.TryCreate(ParseYaml("items:\n  - name: a\n  - 3\n"), YamlSequence("items")));
    }

    [Fact]
    public void A_YAML_sequence_with_an_alias_item_is_not_a_table()
    {
        const string text = "base: &a\n  name: x\nitems:\n  - *a\n  - name: b\n";

        Assert.Null(TableModel.TryCreate(ParseYaml(text), YamlSequence("items")));
    }

    [Fact]
    public void An_empty_YAML_sequence_and_a_sequence_of_sequences_are_not_tables()
    {
        Assert.Null(TableModel.TryCreate(ParseYaml("items: []\n"), YamlSequence("items")));
        Assert.Null(TableModel.TryCreate(ParseYaml("items:\n  - [1, 2]\n  - [3]\n"), YamlSequence("items")));
    }

    [Fact]
    public void A_YAML_root_mapping_is_not_a_table()
    {
        Assert.Null(TableModel.TryCreate(ParseYaml(YamlItems), At(Item(0))));
    }

    [Fact]
    public void Comments_in_a_YAML_sequence_are_not_rows()
    {
        TableModel table = Table(ParseYaml("items:\n  # first\n  - name: a\n  # last\n  - name: b\n"), YamlSequence("items"));

        Assert.Equal(2, table.Rows.Count);
    }

    // ----- Columns and cells: YAML -----

    [Fact]
    public void YAML_columns_are_the_union_of_the_keys_in_first_seen_order()
    {
        TableModel table = Table(ParseYaml("items:\n  - b: 1\n    a: 2\n  - c: 3\n    a: 4\n"), YamlSequence("items"));

        Assert.Equal(
            new[] { new TableColumn(TableColumnKind.Key, "b"), new TableColumn(TableColumnKind.Key, "a"), new TableColumn(TableColumnKind.Key, "c") },
            table.Columns);
        Assert.Equal("4", table.Cell(1, 1).Display);
    }

    [Fact]
    public void A_YAML_key_absent_from_a_row_is_a_missing_cell()
    {
        TableModel table = Table(ParseYaml(YamlItems), YamlSequence("services"));

        Assert.True(table.Cell(0, 2).IsMissing);
        Assert.True(table.Cell(1, 1).IsMissing);
        Assert.Equal("web", table.Cell(0, 0).Display);
    }

    [Fact]
    public void A_YAML_nested_mapping_and_sequence_are_nested_cells_that_show_their_counts()
    {
        const string text = "items:\n  - name: a\n    list: [1, 2]\n    meta:\n      x: 1\n  - name: b\n";
        TableModel table = Table(ParseYaml(text), YamlSequence("items"));

        Assert.True(table.Cell(0, 1).IsNested);
        Assert.Equal("[2]", table.Cell(0, 1).Display);
        Assert.True(table.Cell(0, 2).IsNested);
        Assert.Equal("{1}", table.Cell(0, 2).Display);
    }

    [Fact]
    public void A_YAML_alias_member_is_a_read_only_cell_that_shows_its_name()
    {
        const string text = "base: &b\n  restart: always\nitems:\n  - name: a\n    opts: *b\n  - name: b\n";
        TableModel table = Table(ParseYaml(text), YamlSequence("items"));

        TableCell cell = table.Cell(0, 1);
        Assert.Equal(NodeKind.YamlAlias, cell.Node!.Kind);
        Assert.Equal(PartKind.Alias, cell.Part!.Kind);
        Assert.Equal("*b", cell.Display);
        Assert.True(table.Cell(1, 1).IsMissing);
    }

    [Fact]
    public void Each_YAML_scalar_cell_has_its_kind_for_the_editor()
    {
        TableModel table = Table(ParseYaml("items:\n  - a: 1.5\n    b: 'x'\n    c: true\n    d: ~\n"), YamlSequence("items"));

        Assert.Equal(YamlValueKind.Float, table.Cell(0, 0).Node!.YamlKind);
        Assert.Equal(YamlValueKind.String, table.Cell(0, 1).Node!.YamlKind);
        Assert.Equal(YamlValueKind.Boolean, table.Cell(0, 2).Node!.YamlKind);
        Assert.Equal(YamlValueKind.Null, table.Cell(0, 3).Node!.YamlKind);
    }

    // ----- Edits: YAML -----

    [Fact]
    public void Setting_a_YAML_cell_keeps_its_style()
    {
        const string text = "items:\n  - name: 'web'\n    port: 80\n";
        TableModel table = Table(ParseYaml(text), YamlSequence("items"));

        Assert.Equal("items:\n  - name: 'db'\n    port: 80\n", Edit(text, EditTestSupport.Yaml, table.EditCell(0, 0, "db")));
        Assert.Equal("items:\n  - name: 'web'\n    port: 8080\n", Edit(text, EditTestSupport.Yaml, table.EditCell(0, 1, "8080")));
    }

    [Fact]
    public void A_missing_YAML_number_cell_gets_the_number_kind_of_its_column()
    {
        TableModel table = Table(ParseYaml(YamlItems), YamlSequence("services"));

        EditOp op = table.EditCell(1, 1, "5432");

        Assert.Equal(new InsertNode(YamlItem("services", 1), 1, new NewYamlValue("port", YamlNewKind.Number, "5432")), op);
        Assert.Equal("services:\n  - name: web\n    port: 80\n  - name: db\n    port: 5432\n    tags: x\n", Edit(YamlItems, EditTestSupport.Yaml, op));
    }

    [Fact]
    public void A_missing_YAML_string_cell_is_inserted_as_a_string_at_the_end_of_its_row()
    {
        TableModel table = Table(ParseYaml(YamlItems), YamlSequence("services"));

        EditOp op = table.EditCell(0, 2, "t");

        Assert.Equal(new InsertNode(YamlItem("services", 0), 2, new NewYamlValue("tags", YamlNewKind.String, "t")), op);
        Assert.Equal("services:\n  - name: web\n    port: 80\n    tags: t\n  - name: db\n    tags: x\n", Edit(YamlItems, EditTestSupport.Yaml, op));
    }

    [Fact]
    public void A_missing_YAML_boolean_cell_takes_the_boolean_kind_of_its_column()
    {
        const string text = "items:\n  - name: a\n    on: true\n  - name: b\n";
        TableModel table = Table(ParseYaml(text), YamlSequence("items"));

        Assert.Equal("items:\n  - name: a\n    on: true\n  - name: b\n    on: false\n", Edit(text, EditTestSupport.Yaml, table.EditCell(1, 1, "false")));
    }

    [Fact]
    public void A_missing_YAML_cell_in_a_column_with_a_tie_of_kinds_is_a_string()
    {
        const string text = "items:\n  - name: a\n    port: 80\n  - name: b\n    port: x\n  - name: c\n";
        TableModel table = Table(ParseYaml(text), YamlSequence("items"));

        EditOp op = table.EditCell(2, 1, "9");

        Assert.Equal(new InsertNode(YamlItem("items", 2), 1, new NewYamlValue("port", YamlNewKind.String, "9")), op);
        Assert.Equal("items:\n  - name: a\n    port: 80\n  - name: b\n    port: x\n  - name: c\n    port: '9'\n", Edit(text, EditTestSupport.Yaml, op));
    }

    [Fact]
    public void Clearing_a_YAML_cell_deletes_its_key()
    {
        TableModel table = Table(ParseYaml(YamlItems), YamlSequence("services"));

        EditOp? op = table.ClearCell(0, 1);

        Assert.NotNull(op);
        Assert.Equal(new DeleteNode(At(Item(0), Member("services"), Item(0), Member("port"))), op);
        Assert.Equal("services:\n  - name: web\n  - name: db\n    tags: x\n", Edit(YamlItems, EditTestSupport.Yaml, op));
        Assert.Null(table.ClearCell(0, 2));
    }

    [Fact]
    public void A_YAML_alias_cell_cannot_be_edited_or_cleared_and_a_nested_cell_cannot_be_cleared()
    {
        const string text = "base: &b\n  restart: always\nitems:\n  - name: a\n    opts: *b\n    list: [1]\n";
        TableModel table = Table(ParseYaml(text), YamlSequence("items"));

        CfgeditException edit = Assert.Throws<CfgeditException>(() => table.EditCell(0, 1, "x"));
        Assert.Equal("an alias cannot be edited", edit.Message);
        CfgeditException clear = Assert.Throws<CfgeditException>(() => table.ClearCell(0, 1));
        Assert.Equal("an alias cannot be cleared in the table; delete it in the tree", clear.Message);
        Assert.Throws<CfgeditException>(() => table.EditCell(0, 2, "x"));
        Assert.Throws<CfgeditException>(() => table.ClearCell(0, 2));
    }

    [Fact]
    public void A_missing_cell_in_a_column_of_aliases_or_merge_keys_is_refused()
    {
        const string text = "base: &b\n  restart: always\nitems:\n  - name: a\n    <<: *b\n    opts: *b\n  - name: c\n";
        TableModel table = Table(ParseYaml(text), YamlSequence("items"));

        CfgeditException merge = Assert.Throws<CfgeditException>(() => table.EditCell(1, 1, "x"));
        Assert.Equal("this column holds aliases; add one in the tree", merge.Message);
        CfgeditException alias = Assert.Throws<CfgeditException>(() => table.EditCell(1, 2, "x"));
        Assert.Equal("this column holds aliases; add one in the tree", alias.Message);
    }

    [Fact]
    public void The_typed_edit_by_kind_is_refused_for_YAML_because_its_type_is_changed_in_the_tree()
    {
        TableModel table = Table(ParseYaml(YamlItems), YamlSequence("services"));

        Assert.Throws<CfgeditException>(() => table.EditCell(0, 1, JsonValueKind.Number, "1"));
    }

    [Fact]
    public void Adding_a_YAML_row_after_copies_the_shape_in_compact_form_with_empty_values()
    {
        TableModel table = Table(ParseYaml(YamlItems), YamlSequence("services"));

        Assert.Equal(
            "services:\n  - name: web\n    port: 80\n  - name: ''\n    port: 0\n  - name: db\n    tags: x\n",
            Edit(YamlItems, EditTestSupport.Yaml, table.AddRow(0)));
    }

    [Fact]
    public void Adding_a_YAML_row_before_the_first_copies_its_shape()
    {
        TableModel table = Table(ParseYaml(YamlItems), YamlSequence("services"));

        Assert.Equal(
            "services:\n  - name: ''\n    port: 0\n  - name: web\n    port: 80\n  - name: db\n    tags: x\n",
            Edit(YamlItems, EditTestSupport.Yaml, table.AddRow(-1)));
    }

    [Fact]
    public void Deleting_and_moving_YAML_rows()
    {
        TableModel table = Table(ParseYaml(YamlItems), YamlSequence("services"));

        Assert.Equal("services:\n  - name: db\n    tags: x\n", Edit(YamlItems, EditTestSupport.Yaml, table.DeleteRow(0)));
        Assert.Equal(
            "services:\n  - name: db\n    tags: x\n  - name: web\n    port: 80\n",
            Edit(YamlItems, EditTestSupport.Yaml, table.MoveRow(0, 1)));
    }

    [Fact]
    public void YAML_scalar_rows_are_set_added_moved_and_deleted_and_cannot_be_cleared()
    {
        TableModel table = Table(ParseYaml(YamlNumbers), YamlSequence("nums"));

        Assert.Equal("nums:\n  - 10\n  - 25\n  - 30\n", Edit(YamlNumbers, EditTestSupport.Yaml, table.EditCell(1, 0, "25")));
        Assert.Equal("nums:\n  - 10\n  - 0\n  - 20\n  - 30\n", Edit(YamlNumbers, EditTestSupport.Yaml, table.AddRow(0)));
        Assert.Equal("nums:\n  - 20\n  - 30\n  - 10\n", Edit(YamlNumbers, EditTestSupport.Yaml, table.MoveRow(0, 2)));
        Assert.Equal("nums:\n  - 10\n  - 30\n", Edit(YamlNumbers, EditTestSupport.Yaml, table.DeleteRow(1)));
        Assert.Throws<CfgeditException>(() => table.ClearCell(0, 0));
    }

    [Fact]
    public void A_YAML_row_outside_the_table_is_refused()
    {
        TableModel table = Table(ParseYaml(YamlItems), YamlSequence("services"));

        Assert.Throws<CfgeditException>(() => table.DeleteRow(2));
        Assert.Throws<CfgeditException>(() => table.EditCell(2, 0, "x"));
    }

    private static ConfigDocument ParseJsonc(string text) => ModelTestSupport.Parse(text, Jsonc);
}
