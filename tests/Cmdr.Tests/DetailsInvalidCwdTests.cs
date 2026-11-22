using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;

namespace Cmdr.Tests;

/// <summary>A cwd that loads but cannot be resolved (it contains a NUL) is shown as invalid instead of throwing.</summary>
public sealed class DetailsInvalidCwdTests : IDisposable
{
    private static readonly IReadOnlyDictionary<string, string?> NoEnvironment = new Dictionary<string, string?>();

    private readonly TempSession _temp = TempSession.Open(new GroupSpec
    {
        Name = "bad",
        Cwd = "bad\0path",
        Env = new Dictionary<string, string?> { ["X"] = "1" },
        Items = { new CommandSpec { Name = "cmd", Program = "echo", Args = { "hi" } } },
    });

    public void Dispose() => _temp.Dispose();

    private string ConfigDir => _temp.Session.ConfigDir;

    [Fact]
    public void Group_with_unresolvable_cwd_shows_it_as_invalid()
    {
        GroupEntry group = _temp.Session.Groups.Single();

        IReadOnlyList<DetailsLine> lines = DetailsText.GroupLines(group, ConfigDir);

        DetailsLine cwd = lines.Single(line => line.Text.StartsWith("cwd: ", StringComparison.Ordinal));
        Assert.StartsWith("cwd: invalid (", cwd.Text);
        Assert.Equal(DetailsStyle.Unset, cwd.Spans[^1].Style);
        Assert.Contains(lines, line => line.Text == "X=1");
    }

    [Fact]
    public void Command_under_unresolvable_cwd_shows_command_line_and_invalid_cwd()
    {
        CommandEntry entry = _temp.Session.AllEntries.Single();

        IReadOnlyList<string> lines = DetailsText.Plain(DetailsText.CommandLines(entry, ConfigDir, windows: false, NoEnvironment, ':'));

        Assert.Equal("$ echo hi", lines[0]);
        Assert.StartsWith("cwd: invalid (", lines[1]);
        Assert.DoesNotContain(lines, line => line.StartsWith("X=", StringComparison.Ordinal));
    }

    [Fact]
    public void Unset_variable_in_cwd_is_shown_as_written_and_does_not_throw()
    {
        using var temp = TempSession.Open(new GroupSpec
        {
            Name = "later",
            Cwd = "${env:CMDR_TEST_UNSET_DETAILS}/x",
            Items = { new CommandSpec { Name = "cmd", Program = "echo", Args = { "hi" } } },
        });
        GroupEntry group = temp.Session.Groups.Single();
        CommandEntry entry = temp.Session.AllEntries.Single();

        IReadOnlyList<DetailsLine> groupLines = DetailsText.GroupLines(group, temp.Session.ConfigDir);
        IReadOnlyList<string> commandLines = DetailsText.Plain(DetailsText.CommandLines(entry, temp.Session.ConfigDir, windows: false, NoEnvironment, ':'));
        string copy = CommandLineFormatter.ForCopy(entry, powerShell: false, NoEnvironment, ':');

        Assert.Contains(groupLines, line => line.Text == "cwd: ${env:CMDR_TEST_UNSET_DETAILS}/x (resolved at start)");
        Assert.Equal("cwd: (inherited, resolved at start)", commandLines[1]);
        Assert.Equal("echo hi", copy);
    }
}
