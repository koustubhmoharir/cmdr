using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class RecentRowsTests
{
    private static readonly string Home = Path.Combine(Path.GetTempPath(), "h", "me");
    private static readonly string Elsewhere = Path.Combine(Path.GetTempPath(), "h", "srv", "sk");

    private static IReadOnlyList<string> Build(IReadOnlyList<string> files, string? home = null, Func<string, bool>? exists = null) =>
        RecentRows.Build(files, exists ?? (_ => true), home);

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
        string sibling = Path.Combine(Path.GetTempPath(), "h", "meX");

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
}
