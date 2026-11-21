using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Logvu.Core.Config;
using Logvu.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Logvu.Tests;

/// <summary>
/// The picker's load of a config with an <c>envScript</c>: the wait dialog runs while the script does, Esc and Cancel stop the script and
/// keep the dialog until it has ended, and the outcome is the config, the message or nothing. The dialog fits 80x24. Each test runs the
/// application with the ANSI driver. Every wait is bounded by a watchdog that ends the run.
/// </summary>
[Collection("Terminal GUI")]
public sealed class TryLoadEnvScriptTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "logvu-tryload-" + Guid.NewGuid().ToString("N"));
    private readonly IApplication _app;
    private EnvScriptWaitDialog? _dialog;
    private int _shown;
    private bool _watchdogFired;

    public TryLoadEnvScriptTests()
    {
        Directory.CreateDirectory(_dir);
        _app = Application.Create();
        _app.Init("ansi");
        _app.Driver!.SetScreenSize(80, 24);

        // Ends a run that does not end by itself, so that a failure is reported instead of a hang.
        _app.AddTimeout(Bound, () =>
        {
            _watchdogFired = true;
            _app.RequestStop();
            return false;
        });
    }

    public void Dispose()
    {
        _app.Dispose();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void A_script_that_succeeds_returns_the_config_and_its_dialog_has_closed()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string name = "LOGVU_TRYLOAD_" + Guid.NewGuid().ToString("N");
        string path = Write(Config(Script("echo '{\"" + name + "\":\"rel/Logs\"}'"), "${env:" + name + "}"));

        (LogvuConfig? value, string? error) = TryLoad(path);

        Assert.False(_watchdogFired);
        Assert.NotNull(value);
        Assert.Null(error);
        Assert.Equal(Path.Combine(_dir, "rel", "Logs"), value.LogsDirectory);
        Assert.Equal(1, _shown);
        Assert.True(_dialog!.Finished);
    }

    [Fact]
    public void Cancel_while_a_slow_script_runs_returns_nothing_and_the_script_is_gone_when_it_returns()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string pidFile = Path.Combine(_dir, "pids");
        string path = Write(Config(Slow(pidFile), "Logs"));
        int[] pids = Array.Empty<int>();
        string? textAfterCancel = null;
        When(() => PidsWritten(pidFile), () =>
        {
            pids = ReadPids(pidFile);
            // Pressed through the button, as a mouse click or Enter on it would.
            _dialog!.CancelButton.InvokeCommand(Command.Accept);
            textAfterCancel = _dialog.ShownText;
        });

        (LogvuConfig? value, string? error) = TryLoad(path);

        Assert.False(_watchdogFired);
        Assert.Null(value);
        Assert.Null(error);
        Assert.Equal("Cancelling…", textAfterCancel);
        Assert.True(_dialog!.Finished);
        Assert.NotEmpty(pids);
        // The dialog stays until the script has ended, so the script is gone by the time the picker goes on.
        Assert.All(pids, pid => Assert.False(IsAlive(pid)));
    }

    [Fact]
    public void Esc_while_a_slow_script_runs_cancels_it_the_same_way()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string pidFile = Path.Combine(_dir, "pids");
        string path = Write(Config(Slow(pidFile), "Logs"));
        int[] pids = Array.Empty<int>();
        string? textAfterEsc = null;
        When(() => PidsWritten(pidFile), () =>
        {
            pids = ReadPids(pidFile);
            _dialog!.NewKeyDownEvent(Key.Esc);
            textAfterEsc = _dialog.ShownText;
        });

        (LogvuConfig? value, string? error) = TryLoad(path);

        Assert.False(_watchdogFired);
        Assert.Null(value);
        Assert.Null(error);
        Assert.Equal("Cancelling…", textAfterEsc);
        Assert.NotEmpty(pids);
        Assert.All(pids, pid => Assert.False(IsAlive(pid)));
    }

    [Fact]
    public void The_error_dialog_shows_every_line_of_a_multi_line_failure()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // The picker shows the message in an error dialog: the error, then the script's last stderr lines, indented.
        string path = Write(Config(Script("echo 'first problem' >&2; echo 'second problem' >&2; echo 'third problem' >&2; exit 3"), "Logs"));
        (LogvuConfig? value, string? error) = TryLoad(path);
        Assert.Null(value);
        Assert.NotNull(error);

        string screen = "";
        _app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            _app.LayoutAndDraw();
            screen = ScreenText();
            _app.RequestStop();
            return false;
        });
        MessageBox.ErrorQuery(_app, "Configuration", error, "_OK");

        Assert.False(_watchdogFired);
        // The dialog wraps long lines and draws its border, so the screen is read with the border and the line breaks as spaces.
        string flat = Regex.Replace(screen, @"[\s─-╿]+", " ");
        Assert.Contains("envScript: failed: exited with code 3", flat);
        Assert.Contains("first problem", flat);
        Assert.Contains("second problem", flat);
        Assert.Contains("third problem", flat);
    }

    // The text of every row of the screen, joined with newlines.
    private string ScreenText()
    {
        var driver = _app.Driver!;
        var rows = new List<string>();
        for (int r = 0; r < 24; r++)
        {
            var row = new System.Text.StringBuilder();
            for (int c = 0; c < 80; c++)
            {
                row.Append(driver.Contents![r, c].Grapheme);
            }

            rows.Add(row.ToString());
        }

        return string.Join("\n", rows);
    }

    [Fact]
    public void A_failing_script_returns_its_message_and_its_dialog_has_closed()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string path = Write(Config(Script("echo 'boom' >&2; exit 1"), "Logs"));

        (LogvuConfig? value, string? error) = TryLoad(path);

        Assert.False(_watchdogFired);
        Assert.Null(value);
        Assert.NotNull(error);
        Assert.StartsWith($"Configuration file '{path}': envScript: failed: exited with code 1", error);
        Assert.EndsWith("\n  boom", error);
        Assert.True(_dialog!.Finished);
    }

    [Fact]
    public void A_config_without_a_script_returns_at_once_with_no_dialog()
    {
        string path = Write(Config(envScript: null, "Logs"));

        (LogvuConfig? value, string? error) = LogvuHost.TryLoad(_app, path, _ => _shown++);

        Assert.NotNull(value);
        Assert.Null(error);
        Assert.Equal(Path.Combine(_dir, "Logs"), value.LogsDirectory);
        Assert.Equal(0, _shown);
    }

    [Fact]
    public void A_config_that_does_not_load_returns_its_message_with_no_dialog()
    {
        string path = Write("{ not json");

        (LogvuConfig? value, string? error) = LogvuHost.TryLoad(_app, path, _ => _shown++);

        Assert.Null(value);
        Assert.StartsWith("Malformed JSON in '", error);
        Assert.Equal(0, _shown);
    }

    [Fact]
    public void The_wait_dialog_fits_80_by_24_with_its_text_above_the_buttons_before_and_after_a_cancel()
    {
        using var cancel = new CancellationTokenSource();
        var dialog = new EnvScriptWaitDialog(_app, "a-config-with-a-long-file-name.json", cancel);
        var layouts = new List<(Rectangle Frame, int ButtonTop, Rectangle Text, string Shown)>();

        _app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            _app.LayoutAndDraw();
            layouts.Add(Measure(dialog));
            dialog.RequestCancel();
            _app.LayoutAndDraw();
            layouts.Add(Measure(dialog));
            _app.RequestStop(dialog);
            return false;
        });
        _app.Run(dialog);

        Assert.Equal(2, layouts.Count);
        foreach ((Rectangle frame, int buttonTop, Rectangle text, string shown) in layouts)
        {
            Assert.True(frame.Right <= 80 && frame.Bottom <= 24, $"the dialog {frame} does not fit 80x24");
            Assert.True(text.Bottom <= buttonTop, $"the text at {text} is under the button row, which starts at {buttonTop}");
            Assert.True(frame.Contains(text), $"the text at {text} is outside the dialog {frame}");
            // The text wraps onto the label's two rows, so it needs at least its length in cells.
            Assert.True(text.Width * text.Height >= shown.Length, $"the text '{shown}' does not fit the {text.Width}x{text.Height} label");
        }
    }

    // The dialog's frame, the top row of its buttons, the text label's screen rectangle and the text it shows.
    private static (Rectangle Frame, int ButtonTop, Rectangle Text, string Shown) Measure(EnvScriptWaitDialog dialog)
    {
        Label text = dialog.SubViews.OfType<Label>().Single();
        int buttonTop = dialog.Buttons.Min(button => button.FrameToScreen().Y);
        return (dialog.FrameToScreen(), buttonTop, text.FrameToScreen(), text.Text);
    }

    /// <summary>Runs the picker's load in the application. The dialog is seen through the callback before it runs.</summary>
    private (LogvuConfig? Value, string? Error) TryLoad(string path) =>
        LogvuHost.TryLoad(_app, path, dialog =>
        {
            _dialog = dialog;
            _shown++;
        });

    /// <summary>Runs the action once, on the application's thread, when the condition first holds while the dialog runs.</summary>
    private void When(Func<bool> condition, Action action)
    {
        bool done = false;
        _app.AddTimeout(TimeSpan.FromMilliseconds(20), () =>
        {
            if (done)
            {
                return false;
            }

            if (_dialog is null || !condition())
            {
                return true;
            }

            done = true;
            action();
            return false;
        });
    }

    private string Write(string text)
    {
        string path = Path.Combine(_dir, "logvu.json");
        File.WriteAllText(path, text);
        return path;
    }

    // A script that writes the pid of the shell and of a child that sleeps, then waits: a cancel must kill both.
    private static string Slow(string pidFile) =>
        Script("echo $$ > '" + pidFile + "'; sleep 30 & echo $! >> '" + pidFile + "'; wait");

    private static bool PidsWritten(string pidFile) => File.Exists(pidFile) && File.ReadAllLines(pidFile).Length == 2;

    private static int[] ReadPids(string pidFile) =>
        File.ReadAllLines(pidFile).Select(line => int.Parse(line, CultureInfo.InvariantCulture)).ToArray();

    /// <summary>The <c>envScript</c> object for a <c>sh</c> script with the given code.</summary>
    private static string Script(string code) =>
        JsonSerializer.Serialize(new { program = "sh", args = new[] { "-c", code } });

    /// <summary>A configuration with the given <c>envScript</c> object (or none) and <c>logsDirectory</c> text.</summary>
    private static string Config(string? envScript, string logsDirectory) =>
        "{\n"
        + (envScript is null ? string.Empty : "  \"envScript\": " + envScript + ",\n")
        + "  \"logsDirectory\": " + JsonSerializer.Serialize(logsDirectory)
        + ",\n  \"filePrefix\": \"SheetKraft\",\n  \"fileNamePattern\": \"^SheetKraft(?:[0-9]+)?[.]log$\"\n}";

    /// <summary>True while the process exists and is not a zombie (its state is read from /proc).</summary>
    private static bool IsAlive(int pid)
    {
        string stat;
        try
        {
            stat = File.ReadAllText($"/proc/{pid}/stat");
        }
        catch (IOException)
        {
            return false;
        }

        int close = stat.LastIndexOf(')');
        return close < 0 || stat[close + 2] is not ('Z' or 'X');
    }
}
