using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Tests;

/// <summary>
/// The list search over hand-built rows and a fake host: the origin of each Find, the wrap, the reveal, and the counts of the status.
/// The rows of the tree used here are, in list order: g1, "ab ab", xy, sub, ab, g2, "zz ab". The query "ab" matches twice in "ab ab",
/// once in "ab", and once in "zz ab": four matches.
/// </summary>
public sealed class ListSearchTargetTests : IDisposable
{
    private readonly TempSession _temp = TempSession.Open(Tree());
    private readonly FakeHost _host;
    private readonly CommandListSource _source;
    private readonly ListSearchTarget _target;

    /// <summary>Opens the session of <see cref="Tree"/> and the host, source and target over it.</summary>
    public ListSearchTargetTests()
    {
        _host = new FakeHost(_temp.Session);
        _source = new CommandListSource(new Selection(), new HashSet<GroupEntry>(ReferenceEqualityComparer.Instance));
        _target = new ListSearchTarget(_host, _source);
    }

    /// <summary>Disposes the session.</summary>
    public void Dispose() => _temp.Dispose();

    /// <summary>The tree of the tests: group g1 with "ab ab", "xy" and the group sub holding "ab"; group g2 with "zz ab".</summary>
    private static GroupSpec[] Tree() => new[]
    {
        new GroupSpec
        {
            Name = "g1",
            Items =
            {
                Command("ab ab"),
                Command("xy"),
                new GroupSpec { Name = "sub", Items = { Command("ab") } },
            },
        },
        new GroupSpec { Name = "g2", Items = { Command("zz ab") } },
    };

    /// <summary>A command with the given name, run by the shell.</summary>
    private static CommandSpec Command(string name) => new() { Name = name, Program = "sh" };

    /// <summary>The pattern for a case-sensitive literal query.</summary>
    private static SearchPattern Literal(string text)
    {
        SearchPattern? pattern = SearchPattern.Create(new SearchQuery(text, CaseSensitive: true, WholeWord: false, Regex: false), out string? error);
        Assert.Null(error);
        return Assert.IsType<SearchPattern>(pattern);
    }

    /// <summary>The group or command with the given name. Names are unique in the tree.</summary>
    private ItemEntry Item(string name) =>
        _temp.Session.AllGroups.Cast<ItemEntry>().Concat(_temp.Session.AllEntries).Single(item => item.Name == name);

    /// <summary>The active match as the source holds it: its item's name and its start.</summary>
    private (string Name, int Start, int Length)? ActiveOnSource()
    {
        if (_source.ActiveMatch is not { } active)
        {
            return null;
        }

        return (active.Item.Name, active.Start, active.Length);
    }

    [Fact]
    public void Forward_find_from_the_focused_row_takes_the_first_match_at_or_after_it_and_walks_on_in_order()
    {
        _host.FocusedItem = Item("g1");
        _target.SetSearch(Literal("ab"));

        Assert.Equal(SearchResult.Found, _target.Find(backward: false, inclusive: true));
        Assert.Equal(new SearchStatus(4, 1), _target.SearchStatus());
        Assert.Equal(("ab ab", 0, 2), ActiveOnSource());

        // The second match of the same name comes before the next name.
        Assert.Equal(SearchResult.Found, _target.Find(backward: false, inclusive: false));
        Assert.Equal(new SearchStatus(4, 2), _target.SearchStatus());
        Assert.Equal(("ab ab", 3, 2), ActiveOnSource());

        Assert.Equal(SearchResult.Found, _target.Find(backward: false, inclusive: false));
        Assert.Equal(("ab", 0, 2), ActiveOnSource());

        Assert.Equal(SearchResult.Found, _target.Find(backward: false, inclusive: false));
        Assert.Equal(("zz ab", 3, 2), ActiveOnSource());
        Assert.Equal(new SearchStatus(4, 4), _target.SearchStatus());
    }

    [Fact]
    public void Forward_find_past_the_last_match_wraps_to_the_first_one()
    {
        _host.FocusedItem = Item("g1");
        _target.SetSearch(Literal("ab"));
        for (int i = 0; i < 4; i++)
        {
            _target.Find(backward: false, inclusive: false);
        }

        Assert.Equal(SearchResult.Wrapped, _target.Find(backward: false, inclusive: false));
        Assert.Equal(("ab ab", 0, 2), ActiveOnSource());
        Assert.Equal(new SearchStatus(4, 1), _target.SearchStatus());
    }

    [Fact]
    public void Backward_find_from_the_focused_row_takes_the_last_match_before_it_and_walks_up()
    {
        // The focused row is "ab" (sub's command): its own match is before the end of its row, so backward from the row takes it.
        _host.FocusedItem = Item("ab");
        _target.SetSearch(Literal("ab"));

        Assert.Equal(SearchResult.Found, _target.Find(backward: true, inclusive: false));
        Assert.Equal(("ab", 0, 2), ActiveOnSource());

        Assert.Equal(SearchResult.Found, _target.Find(backward: true, inclusive: false));
        Assert.Equal(("ab ab", 3, 2), ActiveOnSource());

        Assert.Equal(SearchResult.Found, _target.Find(backward: true, inclusive: false));
        Assert.Equal(("ab ab", 0, 2), ActiveOnSource());
        Assert.Equal(new SearchStatus(4, 1), _target.SearchStatus());
    }

    [Fact]
    public void Backward_find_past_the_first_match_wraps_to_the_last_one()
    {
        _host.FocusedItem = Item("g1");
        _target.SetSearch(Literal("ab"));

        // From the top row going backward nothing is before the origin, so the search wraps from the end.
        Assert.Equal(SearchResult.Wrapped, _target.Find(backward: true, inclusive: false));
        Assert.Equal(("zz ab", 3, 2), ActiveOnSource());
        Assert.Equal(new SearchStatus(4, 4), _target.SearchStatus());
    }

    [Fact]
    public void Inclusive_find_after_a_longer_query_keeps_the_active_match_and_a_non_inclusive_one_moves_on()
    {
        _host.FocusedItem = Item("g1");
        _target.SetSearch(Literal("a"));
        Assert.Equal(SearchResult.Found, _target.Find(backward: false, inclusive: true));
        Assert.Equal(("ab ab", 0, 1), ActiveOnSource());

        // The longer query still matches at the active match, so an inclusive find (as while typing) stays on it.
        _target.SetSearch(Literal("ab"));
        Assert.Equal(SearchResult.Found, _target.Find(backward: false, inclusive: true));
        Assert.Equal(("ab ab", 0, 2), ActiveOnSource());
        Assert.Equal(new SearchStatus(4, 1), _target.SearchStatus());

        Assert.Equal(SearchResult.Found, _target.Find(backward: false, inclusive: false));
        Assert.Equal(("ab ab", 3, 2), ActiveOnSource());
    }

    [Fact]
    public void Reveal_is_called_with_the_matched_item()
    {
        _host.FocusedItem = Item("g1");
        _target.SetSearch(Literal("zz"));

        Assert.Equal(SearchResult.Found, _target.Find(backward: false, inclusive: true));

        Assert.Equal(new[] { Item("zz ab") }, _host.Revealed);
        Assert.True(_host.Redraws > 0);
    }

    [Fact]
    public void Find_without_a_match_calls_nothing_and_changes_no_active_match()
    {
        _host.FocusedItem = Item("g1");
        _target.SetSearch(Literal("ab"));
        Assert.Equal(SearchResult.Found, _target.Find(backward: false, inclusive: true));
        (string Name, int Start, int Length)? before = ActiveOnSource();
        int revealed = _host.Revealed.Count;

        _target.SetSearch(Literal("nothing"));
        Assert.Equal(SearchResult.NotFound, _target.Find(backward: false, inclusive: true));

        Assert.Equal(revealed, _host.Revealed.Count);
        Assert.Equal(before, ActiveOnSource());
    }

    [Fact]
    public void Find_without_a_pattern_is_not_found_and_reveals_nothing()
    {
        _host.FocusedItem = Item("g1");

        Assert.Equal(SearchResult.NotFound, _target.Find(backward: false, inclusive: true));
        Assert.Empty(_host.Revealed);
    }

    [Fact]
    public void Search_status_counts_every_match_and_numbers_the_active_one_in_list_order()
    {
        _host.FocusedItem = Item("g1");
        _target.SetSearch(Literal("ab"));
        Assert.Equal(new SearchStatus(4, 0), _target.SearchStatus());

        _target.Find(backward: false, inclusive: true);
        _target.Find(backward: false, inclusive: false);
        Assert.Equal(new SearchStatus(4, 2), _target.SearchStatus());
    }

    [Fact]
    public void Active_number_is_zero_when_the_active_match_no_longer_matches_the_pattern()
    {
        _host.FocusedItem = Item("g1");
        _target.SetSearch(Literal("ab"));
        _target.Find(backward: false, inclusive: true);

        _target.SetSearch(Literal("xy"));

        Assert.Equal(new SearchStatus(1, 0), _target.SearchStatus());
    }

    [Fact]
    public void Ending_the_search_clears_the_status_and_the_active_match()
    {
        _host.FocusedItem = Item("g1");
        _target.SetSearch(Literal("ab"));
        _target.Find(backward: false, inclusive: true);

        _target.SetSearch(null);

        Assert.Equal(new SearchStatus(0, 0), _target.SearchStatus());
        Assert.Null(_source.ActiveMatch);
        Assert.Null(_source.Search);

        _target.SetSearch(Literal("ab"));
        Assert.Equal(new SearchStatus(4, 0), _target.SearchStatus());
    }

    /// <summary>
    /// A host over the rows of a session: every row is listed, the focused item is set by the test, and reveals are recorded
    /// instead of being done.
    /// </summary>
    private sealed class FakeHost : IListSearchHost
    {
        private readonly CmdrSession _session;

        /// <summary>Lists the rows of the session.</summary>
        public FakeHost(CmdrSession session)
        {
            _session = session;
        }

        /// <summary>Every row of the session.</summary>
        public IReadOnlyList<ListRow> AllRows => _session.BuildRows();

        /// <summary>The item the test focuses.</summary>
        public ItemEntry? FocusedItem { get; set; }

        /// <summary>The items passed to <see cref="Reveal"/>, in call order.</summary>
        public List<ItemEntry> Revealed { get; } = new();

        /// <summary>How many times the list was asked to redraw.</summary>
        public int Redraws { get; private set; }

        /// <summary>Records the item.</summary>
        public void Reveal(ItemEntry item) => Revealed.Add(item);

        /// <summary>Counts the redraw.</summary>
        public void RedrawList() => Redraws++;
    }
}
