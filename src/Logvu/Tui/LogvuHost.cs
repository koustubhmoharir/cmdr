using Logvu.Core.Config;
using Logvu.Core.Store;
using Terminal.Gui.App;

namespace Logvu.Tui;

/// <summary>Runs the Terminal.Gui application for a configured set. The store and the terminal are released on every exit path.</summary>
internal static class LogvuHost
{
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
        finally
        {
            app.Dispose();
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
