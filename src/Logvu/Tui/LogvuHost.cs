using Logvu.Core.Config;
using Logvu.Core.Store;
using Shared.Core;
using Shared.Tui;
using Terminal.Gui.App;

namespace Logvu.Tui;

/// <summary>
/// Runs the Terminal.Gui application for a configured set, either directly or after the recent-configs picker. Ctrl+O in the main
/// window shows the picker again over the running set, and a config chosen there replaces the set in the same application. The
/// stores and the terminal are released on every exit path.
/// </summary>
internal static class LogvuHost
{
    // The key hints of the picker. Open file is listed because it is enabled.
    private const string PickerHints = "Enter Open  Del Remove  o/^O Open file  0-9 Select by number  Esc/^Q Quit";

    // The key hints of the picker opened from the main window: Esc goes back to the set, Ctrl+Q quits logvu.
    internal const string SetPickerHints = "Enter Open  Del Remove  o/^O Open file  0-9 Select by number  Esc back  ^Q quit";

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
            using (var picker = new RecentPicker<LogvuConfig>(app, recent, TryLoad, "Recent configs", PickerHints, openFile: true))
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

    // Loads a config for the picker: the config, or the message to show when it does not load.
    private static (LogvuConfig? Value, string? Error) TryLoad(string path)
    {
        try
        {
            return (LogvuConfig.Load(path), null);
        }
        catch (LogvuConfigException ex)
        {
            return (null, ex.Message);
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
        using var picker = new RecentPicker<LogvuConfig>(app, recent, TryLoad, "Open log set", SetPickerHints, openFile: true);
        app.Run(picker);

        if (picker.Opened is { } config)
        {
            return (TimelineController.PickOutcome.Chosen, config);
        }

        return (picker.QuitRequested ? TimelineController.PickOutcome.Quit : TimelineController.PickOutcome.Back, null);
    }
}
