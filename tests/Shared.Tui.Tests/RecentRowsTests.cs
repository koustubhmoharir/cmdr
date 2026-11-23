using Shared.Tui;

namespace Shared.Tui.Tests;

public sealed class RecentRowsTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "h");
    private static readonly string Home = Path.Combine(Base, "me");
    private static readonly string Elsewhere = Path.Combine(Base, "srv", "sk");
    private static readonly string Repo = Path.Combine(Base, "srv", "cmdr");

    private static IReadOnlyList<RecentRow> Rows(
        IReadOnlyList<string> files,
        string? home = null,
        Func<string, bool>? exists = null,
        Func<string, string?>? repoRootOf = null) =>
        RecentRows.Build(files, exists ?? (_ => true), home, repoRootOf ?? (_ => null));

    private static IReadOnlyList<string> Build(IReadOnlyList<string> files, string? home = null, Func<string, bool>? exists = null) =>
        Rows(files, home, exists).Select(row => row.Text).ToList();

    // The repository name the picker colours, as it is in the shown directory; empty when none is coloured.
    private static string Coloured(RecentRow row) =>
        row.RepoLength == 0 ? string.Empty : row.Directory.Substring(row.RepoStart, row.RepoLength);

    private static Func<string, string?> RepoAt(string repo) => directory => RepoRoot.Outermost(directory, folder => folder == repo);

    [Fact]
    public void Rows_are_numbered_and_padded_to_the_widest_name()
    {
        var files = new[]
        {
            Path.Combine(Home, "proj", "web", "cmdr.json"),
            Path.Combine(Elsewhere, "kraft.json"),
        };

        IReadOnlyList<string> rows = Build(files, Home);

        Assert.Equal(
            "1  cmdr.json   " + Path.Combine("~", "proj", "web"),
            rows[0]);
        Assert.Equal("2  kraft.json  " + Elsewhere, rows[1]);
    }

    [Fact]
    public void Numbers_are_right_aligned_when_there_are_ten_or_more_rows()
    {
        var files = Enumerable.Range(1, 12).Select(i => Path.Combine(Elsewhere, $"c{i}.json")).ToList();

        IReadOnlyList<string> rows = Build(files);

        Assert.Equal(12, rows.Count);
        Assert.StartsWith(" 1  c1.json ", rows[0]);
        Assert.StartsWith(" 9  c9.json ", rows[8]);
        Assert.StartsWith("10  c10.json", rows[9]);
        Assert.StartsWith("12  c12.json", rows[11]);
    }

    [Fact]
    public void Names_are_padded_by_display_columns()
    {
        // Each wide character takes two columns: "日本.json" is 9 columns, "abcdefghi.json" is 14.
        var files = new[]
        {
            Path.Combine(Elsewhere, "日本.json"),
            Path.Combine(Elsewhere, "abcdefghi.json"),
        };

        IReadOnlyList<string> rows = Build(files);

        Assert.Equal("1  日本.json" + new string(' ', 5) + "  " + Elsewhere, rows[0]);
        Assert.Equal("2  abcdefghi.json  " + Elsewhere, rows[1]);
    }

    [Fact]
    public void The_home_folder_itself_is_shown_as_tilde()
    {
        IReadOnlyList<string> rows = Build(new[] { Path.Combine(Home, "cmdr.json") }, Home);

        Assert.Equal("1  cmdr.json  ~", rows[0]);
    }

    [Fact]
    public void A_sibling_that_only_starts_with_the_home_name_is_not_shortened()
    {
        string sibling = Path.Combine(Base, "meX");

        IReadOnlyList<string> rows = Build(new[] { Path.Combine(sibling, "a.json") }, Home);

        Assert.Equal("1  a.json  " + sibling, rows[0]);
    }

    [Fact]
    public void Without_a_home_folder_directories_are_shown_in_full()
    {
        IReadOnlyList<string> rows = Build(new[] { Path.Combine(Home, "cmdr.json") }, home: null);

        Assert.Equal("1  cmdr.json  " + Home, rows[0]);
    }

    [Fact]
    public void An_empty_home_folder_shortens_nothing()
    {
        IReadOnlyList<string> rows = Build(new[] { Path.Combine(Home, "cmdr.json") }, home: string.Empty);

        Assert.Equal("1  cmdr.json  " + Home, rows[0]);
    }

    [Fact]
    public void A_missing_file_ends_with_not_found()
    {
        string present = Path.Combine(Elsewhere, "a.json");
        string missing = Path.Combine(Elsewhere, "b.json");

        IReadOnlyList<string> rows = Build(new[] { present, missing }, exists: path => path == present);

        Assert.Equal("1  a.json               " + Elsewhere, rows[0]);
        Assert.Equal("2  b.json  (not found)  " + Elsewhere, rows[1]);
    }

    [Fact]
    public void Found_rows_have_no_trailing_spaces()
    {
        IReadOnlyList<string> rows = Build(new[] { Path.Combine(Elsewhere, "a.json") });

        Assert.Equal(rows[0].TrimEnd(), rows[0]);
    }

    [Fact]
    public void No_files_give_no_rows()
    {
        Assert.Empty(Build(Array.Empty<string>()));
    }

    [Fact]
    public void Rows_carry_their_parts_as_data()
    {
        string present = Path.Combine(Home, "proj", "web", "cmdr.json");
        string missing = Path.Combine(Elsewhere, "b.json");

        IReadOnlyList<RecentRow> rows = Rows(new[] { present, missing }, Home, exists: path => path == present);

        Assert.Equal(new RecentRow("1", "cmdr.json", false, true, Path.Combine("~", "proj", "web"), 0, 0), rows[0]);
        Assert.Equal(new RecentRow("2", "b.json   ", true, true, Elsewhere, 0, 0), rows[1]);
        Assert.Equal("2  b.json     (not found)  " + Elsewhere, rows[1].Text);
    }

    [Fact]
    public void The_not_found_column_is_absent_when_every_file_exists()
    {
        IReadOnlyList<RecentRow> rows = Rows(new[] { Path.Combine(Elsewhere, "a.json") });

        Assert.False(rows[0].MissingColumn);
        Assert.False(rows[0].Missing);
    }

    [Fact]
    public void The_repository_name_is_located_in_the_middle_of_the_directory()
    {
        string file = Path.Combine(Repo, "docs", "a.json");

        IReadOnlyList<RecentRow> rows = Rows(new[] { file }, Home, repoRootOf: RepoAt(Repo));

        RecentRow row = rows[0];
        Assert.Equal(Path.Combine(Repo, "docs"), row.Directory);
        Assert.Equal("cmdr", Coloured(row));
        Assert.Equal(Path.Combine(Base, "srv") + Path.DirectorySeparatorChar, row.Directory[..row.RepoStart]);
        Assert.Equal(Path.DirectorySeparatorChar + "docs", row.Directory[(row.RepoStart + row.RepoLength)..]);
    }

    [Fact]
    public void A_file_directly_in_the_repository_root_colours_the_last_segment_of_the_directory()
    {
        IReadOnlyList<RecentRow> rows = Rows(new[] { Path.Combine(Repo, "a.json") }, Home, repoRootOf: RepoAt(Repo));

        RecentRow row = rows[0];
        Assert.Equal(Repo, row.Directory);
        Assert.Equal("cmdr", Coloured(row));
        Assert.Equal(row.Directory.Length, row.RepoStart + row.RepoLength);
    }

    [Fact]
    public void The_outermost_repository_wins_over_a_nested_one()
    {
        string inner = Path.Combine(Repo, "sub");
        string file = Path.Combine(inner, "x.json");

        IReadOnlyList<RecentRow> rows = Rows(
            new[] { file },
            Home,
            repoRootOf: directory => RepoRoot.Outermost(directory, folder => folder == Repo || folder == inner));

        RecentRow row = rows[0];
        Assert.Equal(inner, row.Directory);
        Assert.Equal("cmdr", Coloured(row));
        Assert.Equal(Path.DirectorySeparatorChar + "sub", row.Directory[(row.RepoStart + row.RepoLength)..]);
    }

    [Fact]
    public void A_repository_that_is_the_home_folder_colours_the_tilde()
    {
        IReadOnlyList<RecentRow> rows = Rows(
            new[] { Path.Combine(Home, "cmdr.json"), Path.Combine(Home, "proj", "x.json") },
            Home,
            repoRootOf: RepoAt(Home));

        Assert.Equal("~", rows[0].Directory);
        Assert.Equal("~", Coloured(rows[0]));
        Assert.Equal(0, rows[0].RepoStart);

        Assert.Equal("~" + Path.DirectorySeparatorChar + "proj", rows[1].Directory);
        Assert.Equal("~", Coloured(rows[1]));
        Assert.Equal(0, rows[1].RepoStart);
    }

    [Fact]
    public void A_repository_above_the_home_folder_colours_nothing()
    {
        string above = Base;
        IReadOnlyList<RecentRow> rows = Rows(
            new[] { Path.Combine(Home, "cmdr.json"), Path.Combine(Home, "proj", "x.json") },
            Home,
            repoRootOf: RepoAt(above));

        Assert.Equal("~", rows[0].Directory);
        Assert.Equal(string.Empty, Coloured(rows[0]));
        Assert.Equal("~" + Path.DirectorySeparatorChar + "proj", rows[1].Directory);
        Assert.Equal(string.Empty, Coloured(rows[1]));
    }

    [Fact]
    public void A_file_outside_every_repository_colours_nothing()
    {
        IReadOnlyList<RecentRow> rows = Rows(new[] { Path.Combine(Elsewhere, "a.json") }, Home, repoRootOf: _ => null);

        Assert.Equal(Elsewhere, rows[0].Directory);
        Assert.Equal(0, rows[0].RepoLength);
        Assert.Equal(string.Empty, Coloured(rows[0]));
    }

    [Fact]
    public void A_repository_at_the_file_system_root_colours_nothing()
    {
        string root = Path.GetPathRoot(Elsewhere)!;

        IReadOnlyList<RecentRow> rows = Rows(
            new[] { Path.Combine(Elsewhere, "a.json") },
            Home,
            repoRootOf: RepoAt(root));

        Assert.Equal(Elsewhere, rows[0].Directory);
        Assert.Equal(string.Empty, Coloured(rows[0]));
    }

    [Fact]
    public void The_repository_is_looked_up_once_per_distinct_directory()
    {
        var looked = new List<string>();
        Func<string, string?> repoRootOf = directory =>
        {
            looked.Add(directory);
            return null;
        };

        Rows(
            new[] { Path.Combine(Repo, "a.json"), Path.Combine(Repo, "b.json"), Path.Combine(Elsewhere, "c.json") },
            Home,
            repoRootOf: repoRootOf);

        Assert.Equal(new[] { Repo, Elsewhere }, looked);
    }
}
