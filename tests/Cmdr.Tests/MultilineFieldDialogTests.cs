using System.Reflection;
using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Shared.Processes;
using Terminal.Gui.App;
using Terminal.Gui.Views;

// TextView is obsolete in Terminal.Gui 2.5.0; the dialogs under test use it (see CommandDialog).
#pragma warning disable CS0618

namespace Cmdr.Tests;

/// <summary>
/// The args and env fields of the command and group dialogs: a field whose values cannot be written in its line format is
/// read-only, and accepting the dialog keeps the original values exactly.
/// </summary>
[Collection("Terminal GUI")]
public sealed class MultilineFieldDialogTests
{
    private static readonly string[] AwkwardArgs = { "-c", "echo first\necho second", "x\r" };

    private static readonly Dictionary<string, string?> AwkwardEnv = new()
    {
        ["A"] = "first\nB=second",
        ["C"] = "plain",
    };

    [Fact]
    public void Command_with_args_and_env_that_cannot_be_written_as_lines_has_read_only_fields_and_keeps_them_on_accept()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        GroupEntry top = temp.Session.AllGroups.Single(group => group.Name == "top");
        CommandEntry editing = top.Items.OfType<CommandEntry>().Single(command => command.Name == "first");
        var initial = new CommandSpec { Name = "first", Program = "sh", Args = AwkwardArgs.ToList(), Env = new Dictionary<string, string?>(AwkwardEnv) };
        var commits = new List<CommandSpec>();
        using var dialog = new CommandDialog(app, temp.Session, "Edit command", top, initial, editing, (_, spec) =>
        {
            commits.Add(spec);
            return null;
        });

        Assert.True(Private<TextView>(dialog, "_args").ReadOnly);
        Assert.True(Private<TextView>(dialog, "_env").ReadOnly);

        Private<TextField>(dialog, "_name").Text = "renamed";
        Assert.True(TryCommit(dialog));

        CommandSpec committed = Assert.Single(commits);
        Assert.Equal("renamed", committed.Name);
        Assert.Equal(AwkwardArgs, committed.Args);
        Assert.Equal(AwkwardEnv, committed.Env);
    }

    [Fact]
    public void Command_with_ordinary_args_and_env_has_editable_fields_and_commits_the_edits()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        GroupEntry top = temp.Session.AllGroups.Single(group => group.Name == "top");
        var initial = new CommandSpec
        {
            Name = "fresh",
            Program = "sh",
            Args = new List<string> { "-c", "echo one" },
            Env = new Dictionary<string, string?> { ["A"] = "1" },
        };
        var commits = new List<CommandSpec>();
        using var dialog = new CommandDialog(app, temp.Session, "Add command", top, initial, null, (_, spec) =>
        {
            commits.Add(spec);
            return null;
        });

        Assert.False(Private<TextView>(dialog, "_args").ReadOnly);
        Assert.False(Private<TextView>(dialog, "_env").ReadOnly);

        Private<TextView>(dialog, "_args").Text = "-c\necho two";
        Private<TextView>(dialog, "_env").Text = "A=2\n!B";
        Assert.True(TryCommit(dialog));

        CommandSpec committed = Assert.Single(commits);
        Assert.Equal(new[] { "-c", "echo two" }, committed.Args);
        Assert.Equal(new Dictionary<string, string?> { ["A"] = "2", ["B"] = null }, committed.Env);
    }

    [Fact]
    public void Group_with_an_env_value_that_cannot_be_written_as_lines_has_a_read_only_env_and_keeps_it_on_accept()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        GroupEntry top = temp.Session.AllGroups.Single(group => group.Name == "top");
        var env = new Dictionary<string, string?> { ["A"] = "x\r\ny" };
        var initial = new GroupSettings("top", null, GroupMode.Parallel, "src", env, true);
        var commits = new List<GroupSettings>();
        using var dialog = new GroupDialog(app, temp.Session, "Edit group", top, null, initial, (_, settings) =>
        {
            commits.Add(settings);
            return null;
        });

        Assert.True(Private<TextView>(dialog, "_env").ReadOnly);

        Private<TextField>(dialog, "_name").Text = "renamed";
        Assert.True(TryCommit(dialog));

        GroupSettings committed = Assert.Single(commits);
        Assert.Equal("renamed", committed.Name);
        Assert.Equal(env, committed.Env);
    }

    /// <summary>The single-line fields of the command dialog, by control name: how a command holds each value.</summary>
    private static readonly IReadOnlyDictionary<string, (Action<CommandSpec, string> Set, Func<CommandSpec, string?> Get)> CommandLines =
        new Dictionary<string, (Action<CommandSpec, string> Set, Func<CommandSpec, string?> Get)>
        {
            ["_name"] = ((spec, value) => spec.Name = value, spec => spec.Name),
            ["_program"] = ((spec, value) => spec.Program = value, spec => spec.Program),
            ["_cwd"] = ((spec, value) => spec.Cwd = value, spec => spec.Cwd),
            ["_logPath"] = ((spec, value) => spec.Log.Path = value, spec => spec.Log.Path),
            ["_readyPattern"] = ((spec, value) => spec.Ready = new ReadySpec { Pattern = value, Busy = spec.Ready?.Busy }, spec => spec.Ready?.Pattern),
            ["_busyPattern"] = ((spec, value) =>
            {
                spec.Ready ??= new ReadySpec { Pattern = "ready" };
                spec.Ready.Busy = value;
            }, spec => spec.Ready?.Busy),
        };

    public static IEnumerable<object[]> CommandLineNames() => CommandLines.Keys.Select(field => new object[] { field });

    public static IEnumerable<object[]> CommandLinesWithLineBreaks() =>
        from field in CommandLines.Keys
        from separator in new[] { "\n", "\r\n" }
        select new object[] { field, "first" + separator + "second" };

    public static IEnumerable<object[]> GroupLinesWithLineBreaks() =>
        from field in new[] { "_name", "_cwd" }
        from separator in new[] { "\n", "\r\n" }
        select new object[] { field, "nav" + separator + "more" };

    [Theory]
    [MemberData(nameof(CommandLinesWithLineBreaks))]
    public void Command_single_line_field_with_a_line_break_is_read_only_and_saved_exactly(string field, string value)
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        var commits = new List<CommandSpec>();
        using var dialog = OpenCommand(app, temp.Session, CommandWith(field, value), commits);

        Assert.True(Private<TextField>(dialog, field).ReadOnly);

        Private<TextField>(dialog, Unrelated(field)).Text = "edited";
        Assert.True(TryCommit(dialog));

        CommandSpec committed = Assert.Single(commits);
        Assert.Equal(value, CommandLines[field].Get(committed));
    }

    [Theory]
    [MemberData(nameof(CommandLineNames))]
    public void Command_single_line_field_left_unchanged_keeps_its_surrounding_spaces(string field)
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        var commits = new List<CommandSpec>();
        using var dialog = OpenCommand(app, temp.Session, CommandWith(field, "  padded  "), commits);

        Assert.False(Private<TextField>(dialog, field).ReadOnly);

        Private<TextField>(dialog, Unrelated(field)).Text = "edited";
        Assert.True(TryCommit(dialog));

        CommandSpec committed = Assert.Single(commits);
        Assert.Equal("  padded  ", CommandLines[field].Get(committed));
    }

    [Fact]
    public void Command_optional_fields_left_unchanged_keep_null_and_empty()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        var initial = new CommandSpec { Name = "first", Program = "sh", Cwd = null, Log = new LogSpec { Path = string.Empty } };
        var commits = new List<CommandSpec>();
        using var dialog = OpenCommand(app, temp.Session, initial, commits);

        Private<TextField>(dialog, "_name").Text = "edited";
        Assert.True(TryCommit(dialog));

        CommandSpec committed = Assert.Single(commits);
        Assert.Null(committed.Cwd);
        Assert.Equal(string.Empty, committed.Log.Path);
    }

    [Theory]
    [InlineData("_name")]
    [InlineData("_program")]
    [InlineData("_cwd")]
    [InlineData("_logPath")]
    public void Command_single_line_field_edited_is_trimmed(string field)
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        var commits = new List<CommandSpec>();
        using var dialog = OpenCommand(app, temp.Session, CommandWith(field, "  padded  "), commits);

        Private<TextField>(dialog, field).Text = "  typed  ";
        Assert.True(TryCommit(dialog));

        CommandSpec committed = Assert.Single(commits);
        Assert.Equal("typed", CommandLines[field].Get(committed));
    }

    [Fact]
    public void Command_blank_edited_working_dir_and_log_path_become_null()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        var initial = new CommandSpec { Name = "first", Program = "sh", Cwd = "../web", Log = new LogSpec { Path = "logs/x.log" } };
        var commits = new List<CommandSpec>();
        using var dialog = OpenCommand(app, temp.Session, initial, commits);

        Private<TextField>(dialog, "_cwd").Text = "   ";
        Private<TextField>(dialog, "_logPath").Text = " ";
        Assert.True(TryCommit(dialog));

        CommandSpec committed = Assert.Single(commits);
        Assert.Null(committed.Cwd);
        Assert.Null(committed.Log.Path);
    }

    [Fact]
    public void Command_ready_and_busy_edited_text_is_saved_as_typed()
    {
        // As before: a regular expression is not trimmed, and blank means no pattern.
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        var commits = new List<CommandSpec>();
        using var dialog = OpenCommand(app, temp.Session, CommandWith("_readyPattern", "ready"), commits);

        Private<TextField>(dialog, "_readyPattern").Text = "  ok  ";
        Private<TextField>(dialog, "_busyPattern").Text = "  busy  ";
        Assert.True(TryCommit(dialog));

        CommandSpec committed = Assert.Single(commits);
        Assert.Equal("  ok  ", committed.Ready!.Pattern);
        Assert.Equal("  busy  ", committed.Ready.Busy);
    }

    [Theory]
    [MemberData(nameof(GroupLinesWithLineBreaks))]
    public void Group_single_line_field_with_a_line_break_is_read_only_and_saved_exactly(string field, string value)
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        GroupEntry top = temp.Session.AllGroups.Single(group => group.Name == "top");
        var commits = new List<GroupSettings>();
        using var dialog = OpenGroup(app, temp.Session, top, GroupWith(field, value), commits);

        Assert.True(Private<TextField>(dialog, field).ReadOnly);

        Private<TextField>(dialog, field == "_name" ? "_cwd" : "_name").Text = "edited";
        Assert.True(TryCommit(dialog));

        GroupSettings committed = Assert.Single(commits);
        Assert.Equal(value, field == "_name" ? committed.Name : committed.Cwd);
    }

    [Theory]
    [InlineData("_name", "  padded  ")]
    [InlineData("_cwd", "  padded  ")]
    public void Group_single_line_field_left_unchanged_keeps_its_surrounding_spaces(string field, string value)
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        GroupEntry top = temp.Session.AllGroups.Single(group => group.Name == "top");
        var commits = new List<GroupSettings>();
        using var dialog = OpenGroup(app, temp.Session, top, GroupWith(field, value), commits);

        Private<TextField>(dialog, field == "_name" ? "_cwd" : "_name").Text = "edited";
        Assert.True(TryCommit(dialog));

        GroupSettings committed = Assert.Single(commits);
        Assert.Equal(value, field == "_name" ? committed.Name : committed.Cwd);
    }

    [Theory]
    [InlineData("_name")]
    [InlineData("_cwd")]
    public void Group_single_line_field_edited_is_trimmed(string field)
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        GroupEntry top = temp.Session.AllGroups.Single(group => group.Name == "top");
        var commits = new List<GroupSettings>();
        using var dialog = OpenGroup(app, temp.Session, top, GroupWith(field, "  padded  "), commits);

        Private<TextField>(dialog, field).Text = "  typed  ";
        Assert.True(TryCommit(dialog));

        GroupSettings committed = Assert.Single(commits);
        Assert.Equal("typed", field == "_name" ? committed.Name : committed.Cwd);
    }

    /// <summary>The other single-line field edited in a test, so that the field under test is only read.</summary>
    private static string Unrelated(string field) => field == "_name" ? "_program" : "_name";

    private static CommandSpec CommandWith(string field, string value)
    {
        var spec = new CommandSpec { Name = "first", Program = "sh" };
        CommandLines[field].Set(spec, value);
        return spec;
    }

    private static GroupSettings GroupWith(string field, string value) =>
        new(field == "_name" ? value : "top", null, GroupMode.Parallel, field == "_cwd" ? value : null, null, true);

    private static CommandDialog OpenCommand(IApplication app, Session session, CommandSpec initial, List<CommandSpec> commits)
    {
        GroupEntry top = session.AllGroups.Single(group => group.Name == "top");
        CommandEntry editing = top.Items.OfType<CommandEntry>().Single(command => command.Name == "first");
        return new CommandDialog(app, session, "Edit command", top, initial, editing, (_, spec) =>
        {
            commits.Add(spec);
            return null;
        });
    }

    private static GroupDialog OpenGroup(IApplication app, Session session, GroupEntry top, GroupSettings initial, List<GroupSettings> commits) =>
        new(app, session, "Edit group", top, null, initial, (_, settings) =>
        {
            commits.Add(settings);
            return null;
        });

    private static T Private<T>(object target, string member) =>
        (T)target.GetType().GetField(member, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    /// <summary>Runs the dialog's validation and commit without showing it. Only valid values may be passed: an error opens a message box.</summary>
    private static bool TryCommit(object dialog) =>
        (bool)dialog.GetType().GetMethod("TryCommit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null)!;
}
