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
/// The secret field of the group and command dialogs, and the environment script of a group, which the group dialog passes through
/// unchanged when it is not edited. Both dialogs fit 80x24.
/// </summary>
[Collection("Terminal GUI")]
public sealed class SecretAndEnvScriptFieldTests
{
    private static EnvScriptSpec Script() => new()
    {
        Program = "node",
        Args = new List<string> { "tools/env.js" },
        Cwd = "scripts",
        TimeoutSeconds = 9,
        Rerun = EnvScriptRerun.EachStart,
    };

    [Fact]
    public void Group_dialog_fits_80_by_24_with_its_fields_above_the_buttons()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        GroupEntry top = temp.Session.AllGroups.Single(group => group.Name == "top");
        using var dialog = OpenGroup(app, temp.Session, top, Initial(), new List<GroupSettings>());

        DialogFitProbe.AssertFits(DialogFitProbe.Measure(app, dialog));
    }

    [Fact]
    public void Command_dialog_fits_80_by_24_with_its_fields_above_the_buttons()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        GroupEntry top = temp.Session.AllGroups.Single(group => group.Name == "top");
        CommandEntry editing = top.Items.OfType<CommandEntry>().Single(command => command.Name == "first");
        using var dialog = OpenCommand(app, temp.Session, top, WithSecret(editing.Spec, "TOKEN"), editing, new List<CommandSpec>());

        DialogFitProbe.AssertFits(DialogFitProbe.Measure(app, dialog));
    }

    [Fact]
    public void Group_OK_without_edits_keeps_the_environment_script_and_the_secret_names()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        GroupEntry top = temp.Session.AllGroups.Single(group => group.Name == "top");
        var commits = new List<GroupSettings>();
        using var dialog = OpenGroup(app, temp.Session, top, Initial(), commits);

        Assert.True(TryCommit(dialog));

        GroupSettings committed = Assert.Single(commits);
        Assert.Equal("node", committed.EnvScript!.Program);
        Assert.Equal(new[] { "tools/env.js" }, committed.EnvScript.Args);
        Assert.Equal("scripts", committed.EnvScript.Cwd);
        Assert.Equal(9, committed.EnvScript.TimeoutSeconds);
        Assert.Equal(EnvScriptRerun.EachStart, committed.EnvScript.Rerun);
        Assert.Equal(new[] { "GROUP_SECRET", "SECOND" }, committed.Secret);
    }

    [Fact]
    public void Group_secret_field_edited_is_committed_as_comma_separated_names()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        GroupEntry top = temp.Session.AllGroups.Single(group => group.Name == "top");
        var commits = new List<GroupSettings>();
        using var dialog = OpenGroup(app, temp.Session, top, Initial(), commits);

        Assert.False(Private<TextField>(dialog, "_secret").ReadOnly);
        Private<TextField>(dialog, "_secret").Text = "TOKEN,  OTHER ,";
        Assert.True(TryCommit(dialog));

        Assert.Equal(new[] { "TOKEN", "OTHER" }, Assert.Single(commits).Secret);
    }

    [Fact]
    public void Group_secret_with_a_comma_in_a_name_is_read_only_and_kept_exactly()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        GroupEntry top = temp.Session.AllGroups.Single(group => group.Name == "top");
        var commits = new List<GroupSettings>();
        GroupSettings initial = new("top", null, GroupMode.Parallel, null, null, true, null, new[] { "A,B", " C" });
        using var dialog = OpenGroup(app, temp.Session, top, initial, commits);

        Assert.True(Private<TextField>(dialog, "_secret").ReadOnly);
        Private<TextField>(dialog, "_name").Text = "renamed";
        Assert.True(TryCommit(dialog));

        Assert.Equal(new[] { "A,B", " C" }, Assert.Single(commits).Secret);
    }

    [Fact]
    public void Command_secret_field_shows_the_names_and_commits_the_edit()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        GroupEntry top = temp.Session.AllGroups.Single(group => group.Name == "top");
        CommandEntry editing = top.Items.OfType<CommandEntry>().Single(command => command.Name == "first");
        var commits = new List<CommandSpec>();
        using var dialog = OpenCommand(app, temp.Session, top, WithSecret(editing.Spec, "TOKEN"), editing, commits);

        Assert.Equal("TOKEN", Private<TextField>(dialog, "_secret").Text);
        Private<TextField>(dialog, "_secret").Text = "TOKEN, OTHER";
        Assert.True(TryCommit(dialog));

        Assert.Equal(new[] { "TOKEN", "OTHER" }, Assert.Single(commits).Secret);
    }

    [Fact]
    public void Command_secret_cleared_is_committed_as_no_secret()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        GroupEntry top = temp.Session.AllGroups.Single(group => group.Name == "top");
        CommandEntry editing = top.Items.OfType<CommandEntry>().Single(command => command.Name == "first");
        var commits = new List<CommandSpec>();
        using var dialog = OpenCommand(app, temp.Session, top, WithSecret(editing.Spec, "TOKEN"), editing, commits);

        Private<TextField>(dialog, "_secret").Text = " , ";
        Assert.True(TryCommit(dialog));

        Assert.Null(Assert.Single(commits).Secret);
    }

    [Fact]
    public void Command_secret_with_a_comma_in_a_name_is_read_only_and_kept_exactly()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        GroupEntry top = temp.Session.AllGroups.Single(group => group.Name == "top");
        CommandEntry editing = top.Items.OfType<CommandEntry>().Single(command => command.Name == "first");
        var commits = new List<CommandSpec>();
        using var dialog = OpenCommand(app, temp.Session, top, WithSecret(editing.Spec, "A,B"), editing, commits);

        Assert.True(Private<TextField>(dialog, "_secret").ReadOnly);
        Private<TextField>(dialog, "_program").Text = "other";
        Assert.True(TryCommit(dialog));

        Assert.Equal(new[] { "A,B" }, Assert.Single(commits).Secret);
    }

    [Fact]
    public void Group_OK_without_edits_leaves_the_session_clean()
    {
        using TempSession temp = TempSession.Open(SessionMode.Edit, GroupWithEnvScriptEnvAndSecret());
        using IApplication app = Application.Create();
        app.Init("ansi");
        GroupEntry group = temp.Session.Groups[0];
        var initial = new GroupSettings(group.Name, group.Description, group.Mode, group.Cwd, group.Env, group.Expanded, group.EnvScript, group.Secret);
        using var dialog = new GroupDialog(app, temp.Session, "Edit group", group, group.Parent, initial, (parent, settings) =>
        {
            temp.Session.UpdateGroup(group, settings, parent);
            return null;
        });

        Assert.True(TryCommit(dialog));

        Assert.False(temp.Session.IsDirty);
    }

    [Fact]
    public void Command_OK_without_edits_leaves_the_session_clean()
    {
        using TempSession temp = TempSession.Open(SessionMode.Edit, GroupWithEnvScriptEnvAndSecret());
        using IApplication app = Application.Create();
        app.Init("ansi");
        GroupEntry group = temp.Session.Groups[0];
        CommandEntry entry = group.Commands[0];
        using var dialog = new CommandDialog(app, temp.Session, "Edit command", group, entry.Spec, entry, (target, spec) =>
        {
            temp.Session.UpdateCommand(entry, spec, target);
            return null;
        });

        Assert.True(TryCommit(dialog));

        Assert.False(temp.Session.IsDirty);
    }

    /// <summary>A group with an environment, an environment script and a secret, holding one command with its own environment and secret.</summary>
    private static GroupSpec GroupWithEnvScriptEnvAndSecret() => new()
    {
        Name = "top",
        Cwd = "src",
        Env = new Dictionary<string, string?> { ["G"] = "1" },
        EnvScript = Script(),
        Secret = new List<string> { "GROUP_SECRET", "SECOND" },
        Items =
        {
            new CommandSpec
            {
                Name = "first",
                Program = "sh",
                Args = new List<string> { "-c", "true" },
                Env = new Dictionary<string, string?> { ["C"] = "2" },
                Secret = new List<string> { "TOKEN" },
            },
        },
    };

    /// <summary>A group as the group dialog starts with it: an environment script and two secret names.</summary>
    private static GroupSettings Initial() =>
        new("top", null, GroupMode.Parallel, "src", null, true, Script(), new[] { "GROUP_SECRET", "SECOND" });

    /// <summary>A copy of the command's spec with the given secret names.</summary>
    private static CommandSpec WithSecret(CommandSpec spec, params string[] names)
    {
        CommandSpec copy = spec.Clone();
        copy.Secret = names.ToList();
        return copy;
    }

    private static GroupDialog OpenGroup(IApplication app, Session session, GroupEntry top, GroupSettings initial, List<GroupSettings> commits) =>
        new(app, session, "Edit group", top, null, initial, (_, settings) =>
        {
            commits.Add(settings);
            return null;
        });

    private static CommandDialog OpenCommand(
        IApplication app,
        Session session,
        GroupEntry top,
        CommandSpec initial,
        CommandEntry editing,
        List<CommandSpec> commits) =>
        new(app, session, "Edit command", top, initial, editing, (_, spec) =>
        {
            commits.Add(spec);
            return null;
        });

    private static T Private<T>(object target, string member) =>
        (T)target.GetType().GetField(member, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    /// <summary>Runs the dialog's validation and commit without showing it. Only valid values may be passed: an error opens a message box.</summary>
    private static bool TryCommit(object dialog) =>
        (bool)dialog.GetType().GetMethod("TryCommit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null)!;
}
