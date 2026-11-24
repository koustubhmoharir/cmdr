using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Shared.Core;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
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
    /// The key hints of the recent-configs picker's bottom row. Each label's key is a click target.
    /// </summary>
    internal static readonly Hint PickerHints = KeyHints.Join(
        KeyHints.Bound(Key.Enter, "Enter Open"),
        KeyHints.Bound(Key.F2, "F2 Edit"),
        KeyHints.Bound(Key.Delete, "Del Remove"),
        KeyHints.Bound(Key.O.WithCtrl, "^O Open file"),
        KeyHints.Plain("0-9 Select"),
        KeyHints.Concat(
            KeyHints.Bound(Key.Esc, "Esc"),
            KeyHints.Plain("/"),
            KeyHints.Bound(Key.Q.WithCtrl, "^Q"),
            KeyHints.Plain(" Quit")));

    /// <summary>
    /// The recent-configs picker as cmdr shows it. Enter opens the selected config in run mode; a file that does not exist is first
    /// offered for creation (No stays in the picker, Yes opens it in edit mode). F2 opens the selected config in edit mode, without
    /// asking for a missing file, which opens with an empty list. Ctrl+O opens a config chosen in a file dialog in run mode (the
    /// dialog only offers existing files) and records it. The picker records the file it opens in the recent list.
    /// </summary>
    /// <param name="app">The running application, for the creation question and the error dialog.</param>
    /// <param name="recent">The recent list, or null when there is no store. Opened files and removals are saved to its store.</param>
    internal static RecentPicker<Session> CreatePicker(IApplication app, RecentFiles? recent)
    {
        ArgumentNullException.ThrowIfNull(app);

        return new RecentPicker<Session>(
            app,
            recent,
            path => OpenInRunMode(app, path),
            "Recent configs",
            PickerHints,
            openFile: true,
            new PickerOpen<Session>(Key.F2, path => TryLoad(path, SessionMode.Edit)));
    }

    /// <summary>
    /// Shows the recent-configs picker, which shows even when the list is empty or null (no store). The config chosen there is
    /// shown in the main window, in the mode the picker opened it in. Returns without showing the main window when the user quits
    /// the picker.
    /// </summary>
    public static void RunPicker(RecentFiles? recent)
    {
        WithApp(app => ShowPicker(app, recent));
    }

    /// <summary>
    /// The run-mode start for a config file that does not exist. Asks whether to create it (Yes is the default). Yes shows the
    /// main window in edit mode, with an empty list, and records the file. No shows the picker.
    /// </summary>
    public static void RunMissingConfig(string path, RecentFiles? recent)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        WithApp(app =>
        {
            if (!AskCreate(app, path))
            {
                ShowPicker(app, recent);
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
    private static void ShowPicker(IApplication app, RecentFiles? recent)
    {
        Session? session;
        using (var picker = CreatePicker(app, recent))
        {
            app.Run(picker);
            session = picker.Opened;
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

    // Enter in the picker: a file that does not exist is first offered for creation, and a No leaves the picker as it is (no value, no
    // error). Otherwise the file is loaded in run mode.
    private static (Session? Value, string? Error) OpenInRunMode(IApplication app, string path)
    {
        SessionMode mode = SessionMode.Run;
        if (!File.Exists(path))
        {
            if (!AskCreate(app, path))
            {
                return (null, null);
            }

            mode = SessionMode.Edit;
        }

        return TryLoad(path, mode);
    }

    // Loads the config in the mode. An invalid config gives its message, which the picker shows.
    private static (Session? Value, string? Error) TryLoad(string path, SessionMode mode)
    {
        try
        {
            return (Session.Load(path, mode), null);
        }
        catch (ConfigException ex)
        {
            return (null, ex.Message);
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
