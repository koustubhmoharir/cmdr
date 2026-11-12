using Cmdr.Core.Session;
using Cmdr.Tui;

namespace Cmdr.Tests;

/// <summary>The source of read-only items in the details strip, and the directory their relative paths resolve against.</summary>
public sealed class DetailsSourceTests : IDisposable
{
    private static readonly IReadOnlyDictionary<string, string?> NoEnvironment = new Dictionary<string, string?>();

    private readonly TempChain _chain = TempChain.Open();

    public void Dispose() => _chain.Dispose();

    private GroupEntry Group(string name) => _chain.Session.AllGroups.Single(g => g.Name == name);

    private CommandEntry Command(string name) => _chain.Session.AllEntries.Single(e => e.Name == name);

    private static string FromText(string file) => "from: " + file + " (read-only)";

    [Fact]
    public void Base_group_shows_its_file_as_read_only()
    {
        IReadOnlyList<string> lines = DetailsText.Plain(DetailsText.GroupLines(Group("shared"), _chain.BaseDir));

        Assert.Contains(FromText(Path.Combine("shared", "base.json")), lines);
    }

    [Fact]
    public void Base_command_shows_its_file_as_read_only()
    {
        IReadOnlyList<string> lines = DetailsText.Plain(CommandLines(Command("tool")));

        Assert.Contains(FromText(Path.Combine("shared", "base.json")), lines);
    }

    [Fact]
    public void Items_of_the_opened_file_have_no_source_line()
    {
        IReadOnlyList<string> group = DetailsText.Plain(DetailsText.GroupLines(Group("main"), _chain.Dir));
        IReadOnlyList<string> command = DetailsText.Plain(CommandLines(Command("mine")));

        Assert.DoesNotContain(group, line => line.StartsWith("from:", StringComparison.Ordinal));
        Assert.DoesNotContain(command, line => line.StartsWith("from:", StringComparison.Ordinal));
    }

    [Fact]
    public void Config_dir_of_a_base_row_is_the_base_file_directory()
    {
        ListRow row = new CommandRow(Command("tool"));

        Assert.Equal(_chain.BaseDir, DetailsText.ConfigDirFor(row, _chain.Dir));
    }

    [Fact]
    public void Config_dir_of_an_opened_file_row_is_the_opened_dir_and_no_row_gives_the_fallback()
    {
        Assert.Equal(_chain.Dir, DetailsText.ConfigDirFor(new CommandRow(Command("mine")), _chain.Dir));
        Assert.Equal(_chain.Dir, DetailsText.ConfigDirFor(null, _chain.Dir));
    }

    [Fact]
    public void Base_command_cwd_and_configdir_resolve_against_the_base_directory()
    {
        // The details of the base command, shown with the directory the row would be given.
        string configDir = DetailsText.ConfigDirFor(new CommandRow(Command("tool")), _chain.Dir);
        IReadOnlyList<string> lines = DetailsText.Plain(CommandLines(Command("tool"), configDir));

        Assert.Contains("cwd: " + Path.GetFullPath(Path.Combine(_chain.BaseDir, "work", "bin")), lines);
        Assert.Contains("OWN=" + Path.GetFullPath(_chain.BaseDir), lines);
    }

    private IReadOnlyList<DetailsLine> CommandLines(CommandEntry entry, string? configDir = null) =>
        DetailsText.CommandLines(entry, configDir ?? _chain.Dir, windows: false, NoEnvironment, '/');
}
