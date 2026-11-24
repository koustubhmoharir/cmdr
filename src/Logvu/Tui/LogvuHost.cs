using Logvu.Core.Config;
using Logvu.Core.Store;
using Shared.Core;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Logvu.Tui;

/// <summary>
/// Runs the Terminal.Gui application for a configured set, either directly or after the recent-configs picker. Ctrl+O in the main
/// window shows the picker again over the running set, and a config chosen there replaces the set in the same application. The
/// stores and the terminal are released on every exit path.
/// </summary>
internal static class LogvuHost
{
    // The Open file hint of both pickers: Ctrl+O opens a config through a file dialog.
    private static readonly Hint OpenFileHint = KeyHints.Bound(Key.O.WithCtrl, "^O Open file");

    // The key hints of the picker. Open file is listed because it is enabled.
    internal static readonly Hint PickerHints = KeyHints.Join(
        KeyHints.Bound(Key.Enter, "Enter Open"),
        KeyHints.Bound(Key.Delete, "Del Remove"),
        OpenFileHint,
        KeyHints.Plain("0-9 Select by number"),
        KeyHints.Concat(KeyHints.Bound(Key.Esc, "Esc"), KeyHints.Plain("/"), KeyHints.Bound(Key.Q.WithCtrl, "^Q"), KeyHints.Plain(" Quit")));

    // The key hints of the picker opened from the main window: Esc goes back to the set, Ctrl+Q quits logvu.
    internal static readonly Hint SetPickerHints = KeyHints.Join(
        KeyHints.Bound(Key.Enter, "Enter Open"),
        KeyHints.Bound(Key.Delete, "Del Remove"),
        OpenFileHint,
        KeyHints.Plain("0-9 Select by number"),
        KeyHints.Bound(Key.Esc, "Esc back"),
        KeyHints.Bound(Key.Q.WithCtrl, "^Q quit"));

    /// <summary>
    /// Runs the main window of a set until the user quits. Ctrl+O can switch to another set from <paramref name="recent"/>. The
    /// stores and the terminal are released before this returns.
    /// </summary>
    /// <param name="config">The configuration of the set to show first.</param>
    /// <param name="recent">The recent list that Ctrl+O picks from, or null when there is no store location.</param>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is <c>null</c>.</exception>
    public static void Run(LogvuConfig config, RecentFiles? recent)
    {
        ArgumentNullException.ThrowIfNull(config);

        IApplication app = Application.Create();
        try
        {
            app.Init();
            RunSets(config, next => RunSet(app, next, recent));
        }
        finally
        {
            app.Dispose();
        }
    }

    /// <summary>
    /// Shows the recent-configs picker, then the main window for the config chosen there. Returns without showing the main
    /// window when the user quits the picker. The terminal is restored before this returns.
    /// </summary>
    /// <param name="recent">The recent list, or null when there is no store location.</param>
    public static void RunPicker(RecentFiles? recent)
    {
        IApplication app = Application.Create();
        try
        {
            app.Init();

            LogvuConfig? config;
            using (var picker = new RecentPicker<LogvuConfig>(app, recent, path => TryLoad(app, path), "Recent configs", PickerHints, openFile: true))
            {
                app.Run(picker);
                config = picker.Opened;
            }

            if (config is not null)
            {
                RunSets(config, next => RunSet(app, next, recent));
            }
        }
        finally
        {
            app.Dispose();
        }
    }

    /// <summary>
    /// Runs sets one after another: <paramref name="runSet"/> runs the main window of a set, releases the set, and returns the config
    /// to switch to, or <c>null</c> when the run ended without a switch. The loop ends with the first run that returns <c>null</c>.
    /// </summary>
    /// <param name="first">The configuration of the first set.</param>
    /// <param name="runSet">Runs one set and returns the config chosen while it ran, or <c>null</c>.</param>
    internal static void RunSets(LogvuConfig first, Func<LogvuConfig, LogvuConfig?> runSet)
    {
        LogvuConfig? config = first;
        while (config is not null)
        {
            config = runSet(config);
        }
    }

    /// <summary>
    /// Loads a config for a picker: the config, or the message to show when it does not load. A config with an <c>envScript</c> runs
    /// it under an <see cref="EnvScriptWaitDialog"/>, so the picker's window stays responsive and Esc or Cancel can stop the script.
    /// A cancel returns no value and no message, which leaves the picker as it is.
    /// </summary>
    /// <param name="app">The running application, which the wait dialog runs on.</param>
    /// <param name="path">The config file to load.</param>
    /// <param name="onShown">Called with the wait dialog before it runs, when there is one. A test seam.</param>
    internal static (LogvuConfig? Value, string? Error) TryLoad(IApplication app, string path, Action<EnvScriptWaitDialog>? onShown = null)
    {
        LogvuConfigFile file;
        try
        {
            file = LogvuConfigFile.Read(path);
        }
        catch (LogvuConfigException ex)
        {
            return (null, ex.Message);
        }

        if (!file.HasEnvScript)
        {
            // Completes at once without a script, so waiting for it blocks for no time.
            return ResolveNow(file);
        }

        return ResolveWithWait(app, file, onShown);
    }

    // Resolves a config without a script, which completes at once: the config, or the message of the error.
    private static (LogvuConfig? Value, string? Error) ResolveNow(LogvuConfigFile file)
    {
        try
        {
            return (file.ResolveAsync(CancellationToken.None).GetAwaiter().GetResult(), null);
        }
        catch (LogvuConfigException ex)
        {
            return (null, ex.Message);
        }
    }

    // Runs the script of a config under the wait dialog. The dialog closes on the UI thread once the script's task has ended, and it
    // only stops then, so the task is complete when the dialog returns. A cancel gives no value and no message, even when the script
    // had already finished, because the user asked for it.
    private static (LogvuConfig? Value, string? Error) ResolveWithWait(IApplication app, LogvuConfigFile file, Action<EnvScriptWaitDialog>? onShown)
    {
        using var cancel = new CancellationTokenSource();
        Task<LogvuConfig> resolving = file.ResolveAsync(cancel.Token);

        using var dialog = new EnvScriptWaitDialog(app, Path.GetFileName(file.ConfigPath), cancel);
        // The task ends on a thread-pool thread. Invoke queues the close for the UI thread, which runs it in the dialog's loop, even
        // when the script has already ended by the time the dialog runs.
        _ = resolving.ContinueWith(_ => app.Invoke(() => dialog.Finish()), TaskScheduler.Default);
        onShown?.Invoke(dialog);
        app.Run(dialog);

        if (cancel.IsCancellationRequested)
        {
            return (null, null);
        }

        try
        {
            return (resolving.GetAwaiter().GetResult(), null);
        }
        catch (LogvuConfigException ex)
        {
            return (null, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return (null, null);
        }
    }

    // The store and the main window of a set, on an initialized application. The store is disposed before this returns. Returns the
    // config chosen with Ctrl+O while the set ran, or null.
    private static LogvuConfig? RunSet(IApplication app, LogvuConfig config, RecentFiles? recent)
    {
        var store = new LogStore(config);
        try
        {
            return RunMain(app, store, config, recent);
        }
        finally
        {
            store.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    // The controller unsubscribes before it returns, so the store can be disposed after the application has stopped.
    private static LogvuConfig? RunMain(IApplication app, LogStore store, LogvuConfig config, RecentFiles? recent)
    {
        using var controller = new TimelineController(app, store, config, pickSet: () => ShowSetPicker(app, recent));
        controller.Start();
        app.Run(controller.Window);
        return controller.SwitchTo;
    }

    // The picker shown over a running set, on the same application. Esc returns without a choice; Ctrl+Q asks to quit.
    private static (TimelineController.PickOutcome Outcome, LogvuConfig? Config) ShowSetPicker(IApplication app, RecentFiles? recent)
    {
        using var picker = new RecentPicker<LogvuConfig>(app, recent, path => TryLoad(app, path), "Open log set", SetPickerHints, openFile: true);
        app.Run(picker);

        if (picker.Opened is { } config)
        {
            return (TimelineController.PickOutcome.Chosen, config);
        }

        return (picker.QuitRequested ? TimelineController.PickOutcome.Quit : TimelineController.PickOutcome.Back, null);
    }
}
