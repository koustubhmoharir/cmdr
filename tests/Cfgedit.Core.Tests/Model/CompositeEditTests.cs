using Cfgedit.Core;
using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>Composite edits: order, focus, failure atomicity, nesting, the empty composite, and undo.</summary>
public sealed class CompositeEditTests
{
    private static readonly NodePath FirstItem = At(Item(0), Item(0));

    [Fact]
    public void Steps_apply_in_order_and_each_sees_the_result_of_the_one_before()
    {
        var composite = new CompositeEdit(
        [
            new InsertNode(RootValue, 1, new NewJsonValue(null, JsonValueKind.Number, "2")),
            new SetPart(At(Item(0), Item(1)), PartKind.Value, -1, "3"),
        ]);

        Assert.Equal("[1, 3]", Edit("[1]", Strict, composite));
    }

    [Fact]
    public void The_focus_is_the_focus_of_the_last_step()
    {
        var composite = new CompositeEdit(
        [
            new SetPart(FirstItem, PartKind.Value, -1, "5"),
            new InsertNode(RootValue, 1, new NewJsonValue(null, JsonValueKind.Number, "2")),
        ]);

        (string text, NodePath? focus) = EditWithFocus("[1]", Strict, composite);

        Assert.Equal("[5, 2]", text);
        Assert.Equal(At(Item(0), Item(1)), focus);
    }

    [Fact]
    public void A_failing_step_throws_and_leaves_the_document_unchanged()
    {
        ConfigDocument document = ModelTestSupport.Parse("[1]", Strict);
        var composite = new CompositeEdit(
        [
            new SetPart(FirstItem, PartKind.Value, -1, "5"),
            new InsertNode(RootValue, 0, new NewJsonValue("k", JsonValueKind.Number, "2")),
        ]);

        CfgeditException ex = Assert.Throws<CfgeditException>(() => document.Apply(composite));

        Assert.Equal("[1]", document.Source.Text);
        Assert.Equal("Items of an array have no key", ex.Message);
    }

    [Fact]
    public void A_composite_whose_only_step_fails_throws_and_leaves_the_document_unchanged()
    {
        ConfigDocument document = ModelTestSupport.Parse("[1]", Strict);
        var composite = new CompositeEdit([new SetPart(At(Item(0), Item(9)), PartKind.Value, -1, "5")]);

        Assert.Throws<CfgeditException>(() => document.Apply(composite));
        Assert.Equal("[1]", document.Source.Text);
    }

    [Fact]
    public void A_nested_composite_is_an_argument_exception_and_changes_nothing()
    {
        ConfigDocument document = ModelTestSupport.Parse("[1]", Strict);
        var inner = new CompositeEdit([new SetPart(FirstItem, PartKind.Value, -1, "5")]);
        var outer = new CompositeEdit([new SetPart(FirstItem, PartKind.Value, -1, "6"), inner]);

        Assert.Throws<ArgumentException>(() => document.Apply(outer));
        Assert.Equal("[1]", document.Source.Text);
    }

    [Fact]
    public void An_empty_composite_is_a_no_op()
    {
        ConfigDocument document = ModelTestSupport.Parse("[1]", Strict);

        EditResult result = document.Apply(new CompositeEdit([]));

        Assert.Same(document, result.Document);
        Assert.Null(result.Focus);
        Assert.Equal("[1]", result.Document.Source.Text);
    }

    [Fact]
    public void A_composite_is_one_undo_step()
    {
        var history = new EditHistory(ModelTestSupport.Parse("[1]", Strict));
        var composite = new CompositeEdit(
        [
            new InsertNode(RootValue, 1, new NewJsonValue(null, JsonValueKind.Number, "2")),
            new SetPart(At(Item(0), Item(1)), PartKind.Value, -1, "3"),
        ]);

        history.Apply(composite);

        Assert.Equal("[1, 3]", history.Current.Source.Text);
        Assert.True(history.CanUndo);

        NodePath? focus = history.Undo();

        Assert.Equal("[1]", history.Current.Source.Text);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
        Assert.Equal("/Item(0)", focus?.ToString());

        history.Redo();

        Assert.Equal("[1, 3]", history.Current.Source.Text);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void A_failing_composite_records_no_undo_step()
    {
        var history = new EditHistory(ModelTestSupport.Parse("[1]", Strict));
        var composite = new CompositeEdit(
        [
            new SetPart(FirstItem, PartKind.Value, -1, "5"),
            new SetPart(At(Item(0), Item(9)), PartKind.Value, -1, "6"),
        ]);

        Assert.Throws<CfgeditException>(() => history.Apply(composite));

        Assert.False(history.CanUndo);
        Assert.Equal("[1]", history.Current.Source.Text);
    }

    [Fact]
    public void An_empty_composite_records_no_undo_step()
    {
        var history = new EditHistory(ModelTestSupport.Parse("[1]", Strict));

        history.Apply(new CompositeEdit([]));

        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Adding_a_table_row_is_one_undo_step_for_all_its_inserts()
    {
        const string text = """[{"a": 1, "b": "x"}]""";
        var history = new EditHistory(ModelTestSupport.Parse(text, Strict));
        TableModel table = TableModel.TryCreate(history.Current, RootValue)!;

        history.Apply(table.AddRow(0));

        Assert.Equal("""[{"a": 1, "b": "x"}, {"a": 0, "b": ""}]""", history.Current.Source.Text);

        history.Undo();

        Assert.Equal(text, history.Current.Source.Text);
        Assert.False(history.CanUndo);
    }
}
