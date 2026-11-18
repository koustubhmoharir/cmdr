using Logvu.Core.Config;
using Logvu.Core.Store;
using Shared.Core;
using Shared.Tui;
using Terminal.Gui.App;

namespace Logvu.Tui;

/// <summary>
/// Runs the Terminal.Gui application for a configured set, either directly or after the recent-configs picker. The store and
/// the terminal are released on every exit path.
/// </summary>
internal static class LogvuHost
{
    // The key hints of the picker. Open file is listed because it is enabled.
    private const string PickerHints = "Enter Open  Del Remove  o/^O Open file  0-9 Select by number  Esc/^Q Quit";

    /// <summary>Runs the main window until the user quits. The store is disposed and the terminal restored before this returns.</summary>
    /// <param name="config">The configuration of the set to show.</param>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is <c>null</c>.</exception>
    public static void Run(LogvuConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        IApplication app = Application.Create();
        try
        {
            app.Init();
            RunSet(app, config);
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
                RunSet(app, config);
            }
        }
        finally
        {
            app.Dispose();
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

    // The store for a config and the main window, on an initialized application. The store is disposed before this returns.
    private static void RunSet(IApplication app, LogvuConfig config)
    {
        var store = new LogStore(config);
        try
        {
            RunMain(app, store, config.TimeZone);
        }
        finally
        {
            store.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    // The controller unsubscribes before it returns, so the store can be disposed after the application has stopped.
    private static void RunMain(IApplication app, LogStore store, TimeZoneInfo zone)
    {
        using var controller = new TimelineController(app, store, zone);
        controller.Start();
        app.Run(controller.Window);
    }
}
