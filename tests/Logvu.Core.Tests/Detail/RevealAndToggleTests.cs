using Logvu.Core.Detail;
using Logvu.Core.Parsing;

namespace Logvu.Core.Tests.Detail;

/// <summary>Navigation: revealing a line inside collapsed nodes and folds, toggles, and the index mappings.</summary>
public sealed class RevealAndToggleTests
{
    private static DetailDocument Text(DetailOptions? options = null) =>
        DetailRig.Build(DetailRig.Loaded(TextEventDocumentTests.ErrorEvent(), BuiltInSchemes.Log4NetCore), options: options);

    private static DetailDocument Json(DetailOptions? options = null) =>
        DetailRig.Build(DetailRig.Loaded(SerilogDocumentTests.Event(), BuiltInSchemes.SerilogJson), options: options);

    private static int FullIndexWhere(DetailDocument doc, Func<DetailLine, bool> predicate)
    {
        int index = -1;
        for (int i = 0; i < doc.FullLines.Count && index < 0; i++)
        {
            if (predicate(doc.FullLines[i]))
            {
                index = i;
            }
        }

        Assert.True(index >= 0, "no full line matched");
        return index;
    }

    [Fact]
    public void Reveal_opens_a_collapsed_fold_so_that_a_frame_inside_it_is_visible()
    {
        DetailDocument collapsed = Text(Text().ToggleAllFolds());
        int frame = FullIndexWhere(collapsed, l => l.Text.Contains("ExecutionContext.Run(", StringComparison.Ordinal));
        Assert.Equal(-1, collapsed.VisibleIndex(frame));

        DetailOptions options = collapsed.ExpandToReveal(frame);
        DetailDocument revealed = Text(options);

        Assert.True(revealed.IsExpanded("fold:message#3"));
        int row = revealed.VisibleIndex(frame);
        Assert.True(row >= 0);
        Assert.Contains("ExecutionContext.Run(", revealed.Lines[row].Text);
    }

    [Fact]
    public void Reveal_opens_a_collapsed_json_node_so_that_a_value_inside_it_is_visible()
    {
        DetailDocument collapsed = Json(Json().Toggle("Properties.Context.Tags"));
        Assert.Equal(["Properties.Context.Tags"], collapsed.Options.Toggled.ToArray());
        DetailDocument defaults = Json();
        int tier = FullIndexWhere(defaults, l => l.Scope == "Properties.Context.Tags.tier");
        Assert.Equal(-1, defaults.VisibleIndex(tier));

        DetailDocument revealed = Json(defaults.ExpandToReveal(tier));

        Assert.True(revealed.IsExpanded("Properties.Context.Tags"));
        Assert.True(revealed.VisibleIndex(tier) >= 0);
        Assert.Equal("tier: \"gold\"", revealed.Lines[revealed.VisibleIndex(tier)].Text.TrimStart());
    }

    [Fact]
    public void Reveal_of_a_visible_line_changes_no_state()
    {
        DetailDocument doc = Json();
        int timestamp = FullIndexWhere(doc, l => l.Scope == "Timestamp");

        DetailOptions options = doc.ExpandToReveal(timestamp);

        Assert.True(options.Toggled.SetEquals(doc.Options.Toggled));
    }

    [Fact]
    public void Full_and_visible_indices_map_to_each_other()
    {
        DetailDocument doc = Json();

        for (int visible = 0; visible < doc.Lines.Count; visible++)
        {
            int full = doc.FullIndex(visible);
            Assert.Equal(visible, doc.VisibleIndex(full));
        }

        int hidden = FullIndexWhere(doc, l => l.Scope == "Properties.Context.Tags.tier");
        Assert.Equal(-1, doc.VisibleIndex(hidden));
    }

    [Fact]
    public void Toggling_a_fold_twice_restores_the_original_options()
    {
        DetailDocument doc = Text();

        DetailOptions once = doc.Toggle("fold:message#3");
        DetailOptions twice = Text(once).Toggle("fold:message#3");

        Assert.True(once.Toggled.Contains("fold:message#3"));
        Assert.True(twice.Toggled.SetEquals(doc.Options.Toggled));
    }

    [Fact]
    public void Toggle_of_an_unknown_id_is_an_argument_error()
    {
        DetailDocument doc = Json();

        Assert.Throws<ArgumentException>(() => doc.Toggle("Nope.Missing"));
    }

    [Fact]
    public void Index_arguments_out_of_range_are_argument_errors()
    {
        DetailDocument doc = Json();

        Assert.Throws<ArgumentOutOfRangeException>(() => doc.ExpandToReveal(doc.FullLines.Count));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.VisibleIndex(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.FullIndex(doc.Lines.Count));
    }

    [Fact]
    public void Toggle_all_folds_does_nothing_when_the_event_has_no_folds()
    {
        string slogError = DetailRig.JsonEvents("slog.json").Single(l => l.Contains("\"err\":{", StringComparison.Ordinal));
        DetailDocument doc = DetailRig.Build(DetailRig.Loaded(slogError, BuiltInSchemes.SlogJson));
        Assert.Empty(doc.FoldIds);

        Assert.Same(doc.Options, doc.ToggleAllFolds());
    }

    [Fact]
    public void Expansion_state_is_stored_as_toggled_ids_only()
    {
        DetailOptions afterProperties = Json().Toggle("Properties");
        DetailOptions options = Json(afterProperties).Toggle("Properties.Scope");

        Assert.True(options.Toggled.SetEquals(["Properties", "Properties.Scope"]));
        Assert.False(options.Raw);
    }
}
