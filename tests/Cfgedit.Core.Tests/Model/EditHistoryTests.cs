using Cfgedit.Core;
using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>Undo, redo, the dirty flag across save, reload and external edits, and no-op edits.</summary>
public sealed class EditHistoryTests
{
    private static readonly NodePath A = At(Item(0), Member("a"));

    private static EditHistory History(string text)
    {
        return new EditHistory(ModelTestSupport.Parse(text, Strict));
    }

    [Fact]
    public void Undo_restores_the_text_and_returns_the_focus_before_the_edit()
    {
        EditHistory history = History("{\"a\": 1}");
        history.Apply(new SetPart(A, PartKind.Value, -1, "2"));

        NodePath? focus = history.Undo();

        Assert.Equal("{\"a\": 1}", history.Current.Source.Text);
        Assert.Equal(A, focus);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
    }

    [Fact]
    public void Redo_reapplies_the_text_and_returns_the_focus_after_the_edit()
    {
        EditHistory history = History("{\"a\": 1}");
        history.Apply(new SetPart(A, PartKind.Value, -1, "2"));
        history.Undo();

        NodePath? focus = history.Redo();

        Assert.Equal("{\"a\": 2}", history.Current.Source.Text);
        Assert.Equal(A, focus);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void A_new_edit_after_an_undo_clears_the_redo_stack()
    {
        EditHistory history = History("{\"a\": 1}");
        history.Apply(new SetPart(A, PartKind.Value, -1, "2"));
        history.Undo();

        history.Apply(new SetPart(A, PartKind.Value, -1, "3"));

        Assert.False(history.CanRedo);
        Assert.Null(history.Redo());
        Assert.Equal("{\"a\": 3}", history.Current.Source.Text);
    }

    [Fact]
    public void Undo_and_redo_with_nothing_to_do_return_null_and_change_nothing()
    {
        EditHistory history = History("{\"a\": 1}");

        Assert.Null(history.Undo());
        Assert.Null(history.Redo());
        Assert.Equal("{\"a\": 1}", history.Current.Source.Text);
    }

    [Fact]
    public void A_no_op_edit_records_nothing()
    {
        EditHistory history = History("{\"a\": 1}");

        EditResult result = history.Apply(new SetPart(A, PartKind.Value, -1, "1"));

        Assert.Same(history.Current, result.Document);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void A_refused_edit_leaves_the_history_unchanged()
    {
        EditHistory history = History("{\"a\": 1}");

        Assert.Throws<CfgeditException>(() => history.Apply(new SetPart(A, PartKind.Value, -1, "1.")));

        Assert.False(history.CanUndo);
        Assert.Equal("{\"a\": 1}", history.Current.Source.Text);
    }

    [Fact]
    public void Dirty_is_false_at_load_true_after_an_edit_and_false_again_after_undo()
    {
        EditHistory history = History("{\"a\": 1}");
        Assert.False(history.IsDirty);

        history.Apply(new SetPart(A, PartKind.Value, -1, "2"));
        Assert.True(history.IsDirty);

        history.Undo();
        Assert.False(history.IsDirty);
    }

    [Fact]
    public void Dirty_follows_the_saved_text_across_save_undo_and_redo()
    {
        EditHistory history = History("{\"a\": 1}");
        history.Apply(new SetPart(A, PartKind.Value, -1, "2"));
        history.MarkSaved();
        Assert.False(history.IsDirty);

        history.Undo();
        Assert.True(history.IsDirty);

        history.Redo();
        Assert.False(history.IsDirty);
    }

    [Fact]
    public void Undoing_past_the_saved_text_and_back_is_dirty_until_the_saved_text_returns()
    {
        EditHistory history = History("{\"a\": 1}");
        history.Apply(new SetPart(A, PartKind.Value, -1, "2"));
        history.MarkSaved();
        history.Apply(new SetPart(A, PartKind.Value, -1, "3"));
        history.Undo();
        history.Undo();

        Assert.Equal("{\"a\": 1}", history.Current.Source.Text);
        Assert.True(history.IsDirty);
    }

    [Fact]
    public void A_recorded_external_edit_is_one_undo_step()
    {
        EditHistory history = History("{\"a\": 1}");
        ConfigDocument external = ModelTestSupport.Parse("{\"a\": 9}", Strict);

        history.RecordExternal(external, A, A);

        Assert.Equal("{\"a\": 9}", history.Current.Source.Text);
        Assert.True(history.IsDirty);
        Assert.Equal(A, history.Undo());
        Assert.Equal("{\"a\": 1}", history.Current.Source.Text);
    }

    [Fact]
    public void A_recorded_external_edit_with_the_same_text_records_nothing()
    {
        EditHistory history = History("{\"a\": 1}");

        history.RecordExternal(ModelTestSupport.Parse("{\"a\": 1}", Strict), A, A);

        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Reset_clears_both_stacks_and_the_reloaded_text_is_clean()
    {
        EditHistory history = History("{\"a\": 1}");
        history.Apply(new SetPart(A, PartKind.Value, -1, "2"));

        history.Reset(ModelTestSupport.Parse("{\"a\": 5}", Strict));

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.False(history.IsDirty);
        Assert.Equal("{\"a\": 5}", history.Current.Source.Text);
    }

    [Fact]
    public void Several_edits_undo_one_at_a_time_in_reverse_order()
    {
        EditHistory history = History("{\"a\": 1}");
        history.Apply(new SetPart(A, PartKind.Value, -1, "2"));
        history.Apply(new InsertNode(At(Item(0)), 1, new NewJsonValue("b", JsonValueKind.True, null)));
        history.Apply(new DeleteNode(A));

        Assert.Equal("{\"b\": true}", history.Current.Source.Text);
        history.Undo();
        Assert.Equal("{\"a\": 2, \"b\": true}", history.Current.Source.Text);
        history.Undo();
        Assert.Equal("{\"a\": 2}", history.Current.Source.Text);
        history.Undo();
        Assert.Equal("{\"a\": 1}", history.Current.Source.Text);
        Assert.False(history.CanUndo);
    }
}
