using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Terminal.Gui.App;

namespace Cmdr.Tui;

/// <summary>Runs the Terminal.Gui application for a session. The terminal is restored on every exit path.</summary>
internal static class TuiHost
{
    /// <summary>Runs the TUI until the user quits. Does not dispose the session.</summary>
    public static void Run(Session session)
    {
        ArgumentNullException.ThrowIfNull(session);

        IApplication app = Application.Create();
        try
        {
            app.Init();
            RunMain(app, session);
        }
        finally
        {
            app.Dispose();
        }
    }

    /// <summary>
    /// Shows the recent-configs picker, then the main window for the config chosen there. Returns without showing the main
    /// window when the user quits the picker. The session of the chosen config is disposed here when the main window closes
    /// (unlike <see cref="Run"/>, whose caller owns the session).
    /// </summary>
    public static void RunPicker(RecentFiles recent)
    {
        ArgumentNullException.ThrowIfNull(recent);

        IApplication app = Application.Create();
        try
        {
            app.Init();

            Session? session;
            using (var picker = new RecentPicker(app, recent))
            {
                app.Run(picker);
                session = picker.Session;
            }

            if (session is null)
            {
                return;
            }

            using (session)
            {
                RunMain(app, session);
            }
        }
        finally
        {
            app.Dispose();
        }
    }

    // The main window for a session, on an initialized application. Esc keeps its default Quit binding so dialogs can still
    // be cancelled with it. In the main window the list and output views consume Esc ("stop") before it reaches that
    // binding; Ctrl+Q quits (MainController).
    private static void RunMain(IApplication app, Session session)
    {
        using var controller = new MainController(app, session);
        controller.Build();
        controller.Start();
        app.Run(controller.Window);
    }
}
