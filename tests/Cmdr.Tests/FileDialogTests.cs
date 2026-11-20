using System.Reflection;
using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;
using FileDialog = Cmdr.Tui.FileDialog;
using Terminal.Gui.App;
using Terminal.Gui.Views;
using CmdrSession = Cmdr.Core.Session.Session;

// TextView is obsolete in Terminal.Gui 2.5.0; the dialog under test uses it (see CommandDialog).
#pragma warning disable CS0618

namespace Cmdr.Tests;

/// <summary>
/// The file dialog of the opened file: it fits 80x24, shows the file's settings, keeps them on OK when they are unchanged, passes the
/// edits to the commit function, and its base env field is read-only without a base file.
/// </summary>
[Collection("Terminal GUI")]
public sealed class FileDialogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("cmdr-file-dialog-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: the temporary folder is left behind when it cannot be removed.
        }
    }

    [Fact]
    public void Dialog_fits_80_by_24_with_its_fields_above_the_buttons()
    {
        using TempChain chain = TempChain.Open(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var dialog = new FileDialog(app, chain.Session.MainSource, _ => null);

        DialogFitProbe.AssertFits(DialogFitProbe.Measure(app, dialog));
    }

    [Fact]
    public void Base_env_is_read_only_with_a_hint_when_the_file_has_no_base()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var dialog = new FileDialog(app, temp.Session.MainSource, _ => null);

        Assert.True(Private<TextField>(dialog, "_baseEnv").ReadOnly);
        Assert.Contains(DialogFitProbe.Descendants(dialog).OfType<Label>(), label => label.Text.StartsWith("No base", StringComparison.Ordinal));
    }

    [Fact]
    public void Base_env_is_editable_when_the_file_has_a_base()
    {
        using TempChain chain = TempChain.Open(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var dialog = new FileDialog(app, chain.Session.MainSource, _ => null);

        Assert.False(Private<TextField>(dialog, "_baseEnv").ReadOnly);
    }

    [Fact]
    public void The_envScript_summary_shows_the_files_script()
    {
        WriteFile();
        using CmdrSession session = CmdrSession.Load(Path.Combine(_dir, "cmdr.json"), SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var dialog = new FileDialog(app, session.MainSource, _ => null);

        Assert.Equal("sh -c true", Private<Label>(dialog, "_envScriptSummary").Text);
    }

    [Fact]
    public void Settings_that_are_unchanged_are_committed_as_they_were()
    {
        WriteFile();
        using CmdrSession session = CmdrSession.Load(Path.Combine(_dir, "cmdr.json"), SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        var committed = new List<FileSettings>();
        using var dialog = new FileDialog(app, session.MainSource, settings =>
        {
            committed.Add(settings);
            return null;
        });

        Assert.True(TryCommit(dialog));

        FileSettings settings = Assert.Single(committed);
        Assert.Equal("top-${cwd}", settings.Env!["TOPVAR"]);
        Assert.Equal(new[] { "TOKEN" }, settings.Secret);
        Assert.Equal(new[] { "IMPORTED" }, settings.BaseEnv);
        Assert.Equal("sh", settings.EnvScript!.Program);
        Assert.Equal(new[] { "-c", "true" }, settings.EnvScript.Args);
        Assert.Equal(5, settings.EnvScript.TimeoutSeconds);
        Assert.Equal(EnvScriptRerun.EachStart, settings.EnvScript.Rerun);
    }

    [Fact]
    public void Edited_fields_reach_the_commit_function_as_typed()
    {
        using TempChain chain = TempChain.Open(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        var committed = new List<FileSettings>();
        using var dialog = new FileDialog(app, chain.Session.MainSource, settings =>
        {
            committed.Add(settings);
            return null;
        });
        Private<TextView>(dialog, "_env").Text = "A=1\n!B";
        Private<TextField>(dialog, "_baseEnv").Text = "IMPORTED";
        Private<TextField>(dialog, "_secret").Text = " TOKEN , OTHER ";

        Assert.True(TryCommit(dialog));

        FileSettings settings = Assert.Single(committed);
        Assert.Equal(new Dictionary<string, string?> { ["A"] = "1", ["B"] = null }, settings.Env);
        Assert.Equal(new[] { "IMPORTED" }, settings.BaseEnv);
        Assert.Equal(new[] { "TOKEN", "OTHER" }, settings.Secret);
        Assert.Null(settings.EnvScript);
    }

    /// <summary>A file with every top-level setting, importing a base file: the same settings as the top-level settings tests use.</summary>
    private void WriteFile()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "shared"));
        File.WriteAllText(
            Path.Combine(_dir, "shared", "base.json"),
            """{ "env": { "IMPORTED": "from-base" }, "groups": [ { "name": "shared" } ] }""");
        File.WriteAllText(
            Path.Combine(_dir, "cmdr.json"),
            """
            {
              "base": "shared/base.json",
              "baseEnv": [ "IMPORTED" ],
              "env": { "TOPVAR": "top-${cwd}" },
              "envScript": { "program": "sh", "args": [ "-c", "true" ], "timeoutSeconds": 5, "rerun": "eachStart" },
              "secret": [ "TOKEN" ],
              "groups": [ { "name": "main", "items": [ { "name": "mine", "program": "tool" } ] } ]
            }
            """);
    }

    private static T Private<T>(object target, string member) =>
        (T)target.GetType().GetField(member, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    /// <summary>Runs the dialog's validation and commit without showing it. Only valid values may be passed: an error opens a message box.</summary>
    private static bool TryCommit(object dialog) =>
        (bool)dialog.GetType().GetMethod("TryCommit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null)!;
}
