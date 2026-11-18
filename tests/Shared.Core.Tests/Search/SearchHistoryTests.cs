using Shared.Core;

namespace Shared.Core.Tests.Search;

/// <summary>The search terms kept for the ↑ and ↓ keys: what is kept, in which order, and how the navigation steps.</summary>
public sealed class SearchHistoryTests
{
    [Fact]
    public void An_empty_text_is_not_kept()
    {
        var history = new SearchHistory();

        history.Add(string.Empty);

        Assert.Equal(0, history.Count);
        Assert.Null(history.Older(string.Empty));
    }

    [Fact]
    public void A_text_that_is_already_kept_moves_to_the_newest_place_and_is_not_kept_twice()
    {
        var history = new SearchHistory();
        history.Add("alpha");
        history.Add("beta");

        history.Add("alpha");

        Assert.Equal(2, history.Count);
        Assert.Equal("alpha", history.Older(string.Empty));
        Assert.Equal("beta", history.Older(string.Empty));
    }

    [Fact]
    public void Only_the_newest_50_terms_are_kept_and_the_oldest_are_dropped()
    {
        var history = new SearchHistory();
        for (int i = 0; i < SearchHistory.MaxEntries + 3; i++)
        {
            history.Add($"term {i}");
        }

        Assert.Equal(SearchHistory.MaxEntries, history.Count);
        string? last = null;
        for (int i = 0; i < SearchHistory.MaxEntries; i++)
        {
            last = history.Older(string.Empty);
        }

        Assert.Equal("term 3", last);
        Assert.Null(history.Older(string.Empty));
    }

    [Fact]
    public void Older_on_an_empty_history_returns_null()
    {
        var history = new SearchHistory();

        Assert.Null(history.Older("draft"));
        Assert.Null(history.Newer());
    }

    [Fact]
    public void Older_steps_from_the_newest_term_to_the_oldest_and_then_returns_null_at_the_oldest()
    {
        var history = new SearchHistory();
        history.Add("alpha");
        history.Add("beta");
        history.Add("gamma");

        Assert.Equal("gamma", history.Older(string.Empty));
        Assert.Equal("beta", history.Older(string.Empty));
        Assert.Equal("alpha", history.Older(string.Empty));
        Assert.Null(history.Older(string.Empty));
        Assert.Null(history.Older(string.Empty));
    }

    [Fact]
    public void Only_the_first_Older_of_a_navigation_remembers_the_draft()
    {
        var history = new SearchHistory();
        history.Add("alpha");
        history.Add("beta");

        history.Older("draft");
        history.Older("ignored");

        Assert.Equal("beta", history.Newer());
        Assert.Equal("draft", history.Newer());
    }

    [Fact]
    public void Newer_steps_back_toward_the_newest_term()
    {
        var history = new SearchHistory();
        history.Add("alpha");
        history.Add("beta");
        history.Add("gamma");
        history.Older(string.Empty);
        history.Older(string.Empty);
        history.Older(string.Empty);

        Assert.Equal("beta", history.Newer());
        Assert.Equal("gamma", history.Newer());
    }

    [Fact]
    public void Newer_past_the_newest_term_returns_the_draft_and_ends_the_navigation()
    {
        var history = new SearchHistory();
        history.Add("alpha");
        history.Add("beta");
        history.Older("draft");
        history.Older(string.Empty);

        Assert.Equal("beta", history.Newer());
        Assert.Equal("draft", history.Newer());
        Assert.Null(history.Newer());
    }

    [Fact]
    public void Older_after_the_draft_was_restored_starts_a_new_navigation_with_a_new_draft()
    {
        var history = new SearchHistory();
        history.Add("alpha");
        history.Older("first draft");
        history.Newer();

        Assert.Equal("alpha", history.Older("second draft"));
        Assert.Equal("second draft", history.Newer());
    }

    [Fact]
    public void Newer_when_no_navigation_is_in_progress_returns_null()
    {
        var history = new SearchHistory();
        history.Add("alpha");

        Assert.Null(history.Newer());
    }

    [Fact]
    public void Add_ends_a_navigation_and_forgets_its_draft()
    {
        var history = new SearchHistory();
        history.Add("alpha");
        history.Add("beta");
        history.Older("draft");

        history.Add("gamma");

        Assert.Null(history.Newer());
        Assert.Equal("gamma", history.Older(string.Empty));
    }

    [Fact]
    public void Add_of_an_empty_text_still_ends_a_navigation()
    {
        var history = new SearchHistory();
        history.Add("alpha");
        history.Older("draft");

        history.Add(string.Empty);

        Assert.Null(history.Newer());
    }

    [Fact]
    public void Reset_ends_a_navigation_and_forgets_the_draft_but_keeps_the_terms()
    {
        var history = new SearchHistory();
        history.Add("alpha");
        history.Add("beta");
        history.Older("draft");

        history.Reset();

        Assert.Null(history.Newer());
        Assert.Equal(2, history.Count);
        Assert.Equal("beta", history.Older(string.Empty));
    }
}
