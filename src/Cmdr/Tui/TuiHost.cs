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

            // Esc keeps its default Quit binding so dialogs can still be cancelled with it. In the main window the
            // list and output views consume Esc ("stop") before it reaches that binding; Ctrl+Q quits (MainController).

            using var controller = new MainController(app, session);
            controller.Build();
            controller.Start();
            app.Run(controller.Window);
        }
        finally
        {
            app.Dispose();
        }
    }
}
