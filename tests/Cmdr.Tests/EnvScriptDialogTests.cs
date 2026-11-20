using System.Reflection;
using Cmdr.Core.Config;
using Cmdr.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

// TextView is obsolete in Terminal.Gui 2.5.0; the dialog under test uses it (see CommandDialog).
#pragma warning disable CS0618

namespace Cmdr.Tests;

/// <summary>The environment script dialog: its fields show the script, it fits 80x24, OK keeps the edits, and Remove removes the script.</summary>
[Collection("Terminal GUI")]
public sealed class EnvScriptDialogTests
{
    private static EnvScriptSpec Script() => new()
    {
        Program = "node",
        Args = new List<string> { "tools/env.js", "--json" },
        Cwd = "scripts",
        TimeoutSeconds = 5,
        Rerun = EnvScriptRerun.EachStart,
    };

    [Fact]
    public void Dialog_fits_80_by_24_with_its_fields_above_the_buttons()
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var dialog = new EnvScriptDialog(app, "Environment script", Script());

        DialogFit fit = DialogFitProbe.Measure(app, dialog);

        DialogFitProbe.AssertFits(fit);
    }

    [Fact]
    public void Dialog_with_no_script_fits_80_by_24_too()
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var dialog = new EnvScriptDialog(app, "Environment script", null);

        DialogFitProbe.AssertFits(DialogFitProbe.Measure(app, dialog));
    }

    [Fact]
    public void The_fields_show_the_script_as_it_is()
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var dialog = new EnvScriptDialog(app, "Environment script", Script());

        Assert.Equal("node", Private<TextField>(dialog, "_program").Text);
        Assert.Equal("tools/env.js\n--json", Private<TextView>(dialog, "_args").Text);
        Assert.Equal("scripts", Private<TextField>(dialog, "_cwd").Text);
        Assert.Equal("5", Private<TextField>(dialog, "_timeout").Text);
        Assert.Equal(EnvScriptRerun.EachStart, Private<OptionSelector<EnvScriptRerun>>(dialog, "_rerun").Value);
    }

    [Fact]
    public void Ok_keeps_the_edited_script_with_the_program_trimmed_and_a_blank_timeout_as_the_default()
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var dialog = new EnvScriptDialog(app, "Environment script", Script());
        Private<TextField>(dialog, "_program").Text = "  bash  ";
        Private<TextField>(dialog, "_timeout").Text = string.Empty;

        Assert.True(TryCommit(dialog));

        Assert.True(dialog.Result == true);
        Assert.Equal("bash", dialog.Spec!.Program);
        Assert.Null(dialog.Spec.TimeoutSeconds);
        Assert.Equal(new[] { "tools/env.js", "--json" }, dialog.Spec.Args);
        Assert.Equal("scripts", dialog.Spec.Cwd);
        Assert.Equal(EnvScriptRerun.EachStart, dialog.Spec.Rerun);
    }

    [Fact]
    public void Ok_on_an_unchanged_script_keeps_it_exactly()
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var dialog = new EnvScriptDialog(app, "Environment script", Script());

        Assert.True(TryCommit(dialog));

        EnvScriptSpec kept = dialog.Spec!;
        Assert.Equal("node", kept.Program);
        Assert.Equal(new[] { "tools/env.js", "--json" }, kept.Args);
        Assert.Equal("scripts", kept.Cwd);
        Assert.Equal(5, kept.TimeoutSeconds);
        Assert.Equal(EnvScriptRerun.EachStart, kept.Rerun);
    }

    [Fact]
    public void Remove_ends_the_dialog_with_no_script()
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var dialog = new EnvScriptDialog(app, "Environment script", Script());
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            dialog.Buttons.Single(button => button.Text.TrimStart('_') == "Remove").InvokeCommand(Command.Accept);
            return false;
        });
        // Safety: the dialog closes even if the button does not.
        app.AddTimeout(TimeSpan.FromSeconds(5), () =>
        {
            app.RequestStop(dialog);
            return false;
        });

        app.Run(dialog);

        Assert.True(dialog.Result == true);
        Assert.Null(dialog.Spec);
    }

    private static T Private<T>(object target, string member) =>
        (T)target.GetType().GetField(member, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    /// <summary>Runs the dialog's validation and keep without showing it. Only valid values may be passed: an error opens a message box.</summary>
    private static bool TryCommit(object dialog) =>
        (bool)dialog.GetType().GetMethod("TryCommit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null)!;
}
