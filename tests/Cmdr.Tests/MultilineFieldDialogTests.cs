using System.Reflection;
using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;
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

    private static T Private<T>(object target, string member) =>
        (T)target.GetType().GetField(member, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    /// <summary>Runs the dialog's validation and commit without showing it. Only valid values may be passed: an error opens a message box.</summary>
    private static bool TryCommit(object dialog) =>
        (bool)dialog.GetType().GetMethod("TryCommit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null)!;
}
