using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Tests;

/// <summary>
/// The details lines of a command. Every test passes an explicit base environment, so the real process environment does not
/// matter.
/// </summary>
public sealed class DetailsCommandTests : IDisposable
{
    private static readonly IReadOnlyDictionary<string, string?> BaseEnvironment = new Dictionary<string, string?>
    {
        ["PATHX"] = "base",
        ["GONE"] = "was",
        ["KEEP"] = "k",
    };

    private readonly TempSession _temp = TempSession.Open(NestedTree());

    public void Dispose() => _temp.Dispose();

    private CmdrSession Session => _temp.Session;

    private string ConfigDir => Path.GetFullPath(Session.ConfigDir);

    private CommandEntry Run => Session.AllEntries.Single(e => e.Name == "run");

    private string Resolved(params string[] relative) =>
        Path.GetFullPath(Path.Combine(new[] { ConfigDir }.Concat(relative).ToArray()));

    /// <summary>
    /// top (cwd "src", env TOP, PATHX, SHARED)
    ///   mid (cwd "sub", env SHARED, GONE removed, HERE)
    ///     run (cwd "bin", args, description of five lines, env SHARED, OWN)
    /// </summary>
    private static GroupSpec[] NestedTree() => new[]
    {
        new GroupSpec
        {
            Name = "top",
            Cwd = "src",
            Env = new Dictionary<string, string?>
            {
                ["TOP"] = "t",
                ["PATHX"] = "${PATHX}${pathsep}/top",
                ["SHARED"] = "outer",
            },
            Items =
            {
                new GroupSpec
                {
                    Name = "mid",
                    Cwd = "sub",
                    Env = new Dictionary<string, string?>
                    {
                        ["SHARED"] = "${SHARED}-mid",
                        ["GONE"] = null,
                        ["HERE"] = "${cwd}/x",
                    },
                    Items =
                    {
                        new CommandSpec
                        {
                            Name = "run",
                            Program = "tool",
                            Args = new List<string> { "--flag", "two words" },
                            Cwd = "bin",
                            Description = "a\nb\nc\nd\ne",
                            Env = new Dictionary<string, string?>
                            {
                                ["SHARED"] = "${SHARED}+cmd",
                                ["OWN"] = "${configDir}|${SHARED}",
                            },
                        },
                    },
                },
            },
        },
    };

    private static (string Text, DetailsStyle Style)[] Spans(DetailsLine line) =>
        line.Spans.Select(span => (span.Text, span.Style)).ToArray();

    [Fact]
    public void Command_lines_style_each_piece_of_text()
    {
        IReadOnlyList<DetailsLine> lines = DetailsText.CommandLines(Run, ConfigDir, windows: false, BaseEnvironment, ':');

        Assert.Equal(
            new[] { ("$ ", DetailsStyle.Prompt), ("tool --flag 'two words'", DetailsStyle.Command) },
            Spans(lines[0]));
        Assert.Equal(
            new[] { ("cwd: ", DetailsStyle.Label), (Resolved("src", "sub", "bin"), DetailsStyle.Path) },
            Spans(lines[1]));
        Assert.Equal(
            new[] { ("TOP", DetailsStyle.EnvName), ("=", DetailsStyle.Faint), ("t", DetailsStyle.EnvValue) },
            Spans(lines[2]));
        Assert.Equal(
            new[] { ("GONE", DetailsStyle.EnvName), (" (unset)", DetailsStyle.Unset) },
            Spans(lines[5]));
        Assert.Equal(
            new[] { ("HERE", DetailsStyle.EnvName), ("=", DetailsStyle.Faint), (Resolved("src", "sub") + "/x", DetailsStyle.EnvValue) },
            Spans(lines[6]));
        Assert.Equal(new[] { ("pid — · log: off", DetailsStyle.Faint) }, Spans(lines[8]));
        Assert.Equal(new[] { ("a", DetailsStyle.Normal) }, Spans(lines[9]));
        Assert.Equal(new[] { ("e", DetailsStyle.Normal) }, Spans(lines[13]));
    }

    [Fact]
    public void Console_stdin_is_listed_and_the_pipe_default_is_not()
    {
        Assert.DoesNotContain("stdin: console", DetailsText.Plain(DetailsText.CommandLines(Run, ConfigDir, windows: false, BaseEnvironment, ':')));

        Run.Spec.Stdin = StdinMode.Console;
        IReadOnlyList<DetailsLine> lines = DetailsText.CommandLines(Run, ConfigDir, windows: false, BaseEnvironment, ':');

        int index = DetailsText.Plain(lines).ToList().IndexOf("stdin: console");
        Assert.True(index > 0);
        Assert.Equal(
            new[] { ("stdin: ", DetailsStyle.Label), ("console", DetailsStyle.Normal) },
            Spans(lines[index]));
    }

    [Fact]
    public void Ready_and_busy_patterns_are_listed_with_their_labels()
    {
        Run.Spec.Ready = new ReadySpec { Pattern = "listening on \\d+", Busy = "rebuilding" };
        IReadOnlyList<DetailsLine> lines = DetailsText.CommandLines(Run, ConfigDir, windows: false, BaseEnvironment, ':');
        IReadOnlyList<string> plain = DetailsText.Plain(lines);

        int index = plain.ToList().IndexOf("ready: listening on \\d+");
        Assert.True(index > 0);
        Assert.Equal(
            new[] { ("ready: ", DetailsStyle.Label), ("listening on \\d+", DetailsStyle.Normal) },
            Spans(lines[index]));
        Assert.Equal("busy: rebuilding", plain[index + 1]);
        Assert.Equal(
            new[] { ("busy: ", DetailsStyle.Label), ("rebuilding", DetailsStyle.Normal) },
            Spans(lines[index + 1]));
        Assert.StartsWith("pid ", plain[index + 2]);
    }

    [Fact]
    public void Ready_pattern_alone_lists_no_busy_line()
    {
        Run.Spec.Ready = new ReadySpec { Pattern = "ready" };
        IReadOnlyList<string> plain = DetailsText.Plain(DetailsText.CommandLines(Run, ConfigDir, windows: false, BaseEnvironment, ':'));

        Assert.Contains("ready: ready", plain);
        Assert.DoesNotContain(plain, line => line.StartsWith("busy:", StringComparison.Ordinal));
    }

    [Fact]
    public void Command_without_ready_pattern_lists_no_ready_lines()
    {
        IReadOnlyList<string> plain = DetailsText.Plain(DetailsText.CommandLines(Run, ConfigDir, windows: false, BaseEnvironment, ':'));

        Assert.DoesNotContain(plain, line => line.StartsWith("ready:", StringComparison.Ordinal));
        Assert.DoesNotContain(plain, line => line.StartsWith("busy:", StringComparison.Ordinal));
    }

    [Fact]
    public void Windows_command_row_uses_the_prompt_style_for_its_prompt()
    {
        IReadOnlyList<DetailsLine> lines = DetailsText.CommandLines(Run, ConfigDir, windows: true, BaseEnvironment, ';');

        Assert.Equal(
            new[] { ("> ", DetailsStyle.Prompt), ("tool --flag \"two words\"", DetailsStyle.Command) },
            Spans(lines[0]));
    }

    [Fact]
    public void Command_lines_come_in_order_with_the_command_line_first()
    {
        IReadOnlyList<string> lines = DetailsText.Plain(DetailsText.CommandLines(Run, ConfigDir, windows: false, BaseEnvironment, ':'));

        Assert.Equal(
            new[]
            {
                "$ tool --flag 'two words'",
                "cwd: " + Resolved("src", "sub", "bin"),
                "TOP=t",
                "PATHX=base:/top",
                "SHARED=outer-mid+cmd",
                "GONE (unset)",
                "HERE=" + Resolved("src", "sub") + "/x",
                "OWN=" + ConfigDir + "|outer-mid",
                "pid — · log: off",
                "a",
                "b",
                "c",
                "d",
                "e",
            },
            lines);
    }

    [Fact]
    public void The_prompt_is_not_part_of_the_copied_command_line()
    {
        string copied = CommandLineFormatter.ForSpec(Run.Spec, windows: false);
        IReadOnlyList<string> lines = DetailsText.Plain(DetailsText.CommandLines(Run, ConfigDir, windows: false, BaseEnvironment, ':'));

        Assert.Equal("tool --flag 'two words'", copied);
        Assert.Equal("$ " + copied, lines[0]);
    }

    [Fact]
    public void Windows_uses_its_prompt_and_quoting()
    {
        IReadOnlyList<string> lines = DetailsText.Plain(DetailsText.CommandLines(Run, ConfigDir, windows: true, BaseEnvironment, ';'));

        Assert.Equal("> tool --flag \"two words\"", lines[0]);
        Assert.Equal("PATHX=base;/top", lines[3]);
    }

    [Fact]
    public void The_cwd_is_resolved_through_every_enclosing_group()
    {
        IReadOnlyList<string> lines = DetailsText.Plain(DetailsText.CommandLines(Run, ConfigDir, windows: false, BaseEnvironment, ':'));

        Assert.Equal("cwd: " + Resolved("src", "sub", "bin"), lines[1]);
    }

    [Fact]
    public void A_removed_variable_is_listed_as_unset_even_when_the_base_lacks_it()
    {
        var levels = new List<EnvLevel>
        {
            new(new Dictionary<string, string?> { ["NOPE"] = null }, "/work"),
        };

        IReadOnlyList<string> lines = DetailsText.Plain(DetailsText.EnvLines(levels, "/conf", BaseEnvironment, windows: false, '/'));

        Assert.Equal(new[] { "NOPE (unset)" }, lines);
    }

    [Fact]
    public void Inherited_group_env_is_applied_before_the_commands_own()
    {
        // The command's SHARED expands the value the group left, and OWN sees the same value, not the command's own SHARED.
        IReadOnlyList<string> lines = DetailsText.Plain(DetailsText.CommandLines(Run, ConfigDir, windows: false, BaseEnvironment, ':'));

        Assert.Contains("SHARED=outer-mid+cmd", lines);
        Assert.Contains("OWN=" + ConfigDir + "|outer-mid", lines);
    }

    [Fact]
    public void Env_values_are_expanded_against_the_base_environment()
    {
        IReadOnlyList<string> lines = DetailsText.Plain(DetailsText.CommandLines(Run, ConfigDir, windows: false, BaseEnvironment, ':'));

        Assert.Equal("PATHX=base:/top", lines.Single(l => l.StartsWith("PATHX=", StringComparison.Ordinal)));
    }

    [Fact]
    public void Keys_are_listed_in_first_mention_order_once_each()
    {
        var levels = new List<EnvLevel>
        {
            new(new Dictionary<string, string?> { ["B"] = "1", ["A"] = "2" }, "/w"),
            new(new Dictionary<string, string?> { ["B"] = "3", ["C"] = "${B}" }, "/w"),
        };

        IReadOnlyList<string> lines = DetailsText.Plain(DetailsText.EnvLines(levels, "/conf", new Dictionary<string, string?>(), windows: false, '/'));

        Assert.Equal(new[] { "B=3", "A=2", "C=1" }, lines);
    }

    [Fact]
    public void A_command_without_any_env_has_no_env_block()
    {
        using var plain = TempSession.Open(new GroupSpec
        {
            Name = "g",
            Items = { new CommandSpec { Name = "sh1", Program = "sh" } },
        });
        CommandEntry entry = plain.Session.AllEntries.Single();
        string dir = Path.GetFullPath(plain.Session.ConfigDir);

        IReadOnlyList<string> lines = DetailsText.Plain(DetailsText.CommandLines(entry, dir, windows: false, BaseEnvironment, ':'));

        Assert.Equal(new[] { "$ sh", "cwd: " + dir, "pid — · log: off" }, lines);
    }
}
