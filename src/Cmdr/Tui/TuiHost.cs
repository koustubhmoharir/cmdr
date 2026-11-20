using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Terminal.Gui.App;
using Terminal.Gui.Views;

namespace Cmdr.Tui;

/// <summary>
/// Runs the Terminal.Gui application for the startup flows. Every flow runs in one application that is disposed on every
/// exit path, which restores the terminal. The session a flow shows is disposed when its main window closes, on every path.
/// </summary>
internal static class TuiHost
{
    /// <summary>
    /// Loads the config in <paramref name="mode"/> and adds it to the front of <paramref name="recent"/>, which is saved (a failed
    /// save is ignored). A null list is not recorded. Throws <see cref="ConfigException"/> when the config is invalid, and then
    /// records nothing. Used by the command line and the picker.
    /// </summary>
    public static Session LoadAndRecord(string path, SessionMode mode, RecentFiles? recent)
    {
        Session session = Session.Load(path, mode);
        if (recent is not null)
        {
            recent.Add(path);
            recent.TrySave();
        }

        return session;
    }

    /// <summary>Shows the main window for a session. Disposes the session when the window closes, or when the application cannot start.</summary>
    public static void RunMain(Session session)
    {
        ArgumentNullException.ThrowIfNull(session);

        using (session)
        {
            WithApp(app => ShowMain(app, session));
        }
    }

    /// <summary>
    /// Shows the recent-configs picker. The config chosen there is shown in the main window, in the mode the picker opened it in.
    /// Returns without showing the main window when the user quits the picker.
    /// </summary>
    public static void RunPicker(RecentFiles recent)
    {
        ArgumentNullException.ThrowIfNull(recent);

        WithApp(app => ShowPicker(app, recent));
    }

    /// <summary>
    /// The run-mode start for a config file that does not exist. Asks whether to create it (Yes is the default). Yes shows the
    /// main window in edit mode, with an empty list, and records the file. No shows the picker when <paramref name="recent"/>
    /// has files, and otherwise returns without showing anything.
    /// </summary>
    public static void RunMissingConfig(string path, RecentFiles? recent)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        WithApp(app =>
        {
            if (!AskCreate(app, path))
            {
                if (recent is { Files.Count: > 0 })
                {
                    ShowPicker(app, recent);
                }

                return;
            }

            Session session;
            try
            {
                session = LoadAndRecord(path, SessionMode.Edit, recent);
            }
            catch (ConfigException ex)
            {
                MessageBox.ErrorQuery(app, "Configuration", ex.Message, "_OK");
                return;
            }

            using (session)
            {
                ShowMain(app, session);
            }
        });
    }

    /// <summary>Asks whether to create a config file that does not exist. Yes (the default) means create it, in edit mode.</summary>
    public static bool AskCreate(IApplication app, string path) =>
        Confirm.Ask(app, "New config", $"{path} does not exist. Create it?");

    // Creates the application, initializes it, runs the show, and disposes the application on every path.
    private static void WithApp(Action<IApplication> show)
    {
        IApplication app = Application.Create();
        try
        {
            app.Init();
            show(app);
        }
        finally
        {
            app.Dispose();
        }
    }

    // The picker, then the main window for the config the user opened there. The session is disposed when the window closes.
    private static void ShowPicker(IApplication app, RecentFiles recent)
    {
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
            ShowMain(app, session);
        }
    }

    // The main window for a session, on an initialized application. Esc keeps its default Quit binding so dialogs can still
    // be cancelled with it. In the main window the list, info and output views consume Esc (it does nothing there) before
    // it reaches that binding; Ctrl+Q quits (MainController).
    private static void ShowMain(IApplication app, Session session)
    {
        using var controller = new MainController(app, session);
        controller.Build();
        controller.Start();
        app.Run(controller.Window);
    }
}
