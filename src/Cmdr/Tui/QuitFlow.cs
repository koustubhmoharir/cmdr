using Cmdr.Core.Session;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Cmdr.Tui;

/// <summary>
/// The quit sequence of the main window: unsaved changes first, then running commands. Each step is a modal dialog; the
/// waiting for processes to exit polls with a timer, so the UI keeps running. Returns true when the application may exit.
/// </summary>
internal sealed class QuitFlow
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IApplication _app;
    private readonly Session _session;
    private readonly Func<bool> _save;

    /// <param name="app">The application, for the dialogs and the timer.</param>
    /// <param name="session">The session being quit.</param>
    /// <param name="save">Saves the session, showing any error. Returns false when the save failed.</param>
    public QuitFlow(IApplication app, Session session, Func<bool> save)
    {
        _app = app;
        _session = session;
        _save = save;
    }

    /// <summary>Runs the quit steps. Returns false when the user cancelled or a save failed.</summary>
    public bool Run()
    {
        if (_session.IsDirty && !ResolveUnsaved())
        {
            return false;
        }

        int active = _session.ActiveEntries.Count;
        if (active == 0)
        {
            return true;
        }

        string text = $"{active} command(s) are running or have leftover processes.";
        int? choice = Confirm.Choose(_app, "Quit", text, "_Stop and wait", "_Force kill", "_Cancel");
        switch (choice)
        {
            case 0:
                _session.StopAll();
                return WaitForProcesses();
            case 1:
                _session.ForceKillAll();
                return WaitForProcesses();
            default:
                return false;
        }
    }

    /// <summary>Save, discard or cancel. Returns true when the quit may continue.</summary>
    private bool ResolveUnsaved()
    {
        string file = Path.GetFileName(_session.ConfigPath);
        int? choice = Confirm.Choose(_app, "Quit", $"Save changes to {file}?", "_Save", "_Discard", "_Cancel");
        return choice switch
        {
            0 => _save(),
            1 => true,
            _ => false,
        };
    }

    /// <summary>
    /// Shows the waiting dialog until no command is active (returns true) or the user cancels (returns false). Processes
    /// keep stopping after a cancel.
    /// </summary>
    private bool WaitForProcesses()
    {
        using var dialog = new WaitDialog(_app, _session.ActiveEntries.Count, _session.ForceKillAll);

        object? timer = _app.AddTimeout(PollInterval, () =>
        {
            int remaining = _session.ActiveEntries.Count;
            dialog.SetRemaining(remaining);
            if (remaining == 0)
            {
                dialog.Finish();
                return false;
            }

            return true;
        });

        try
        {
            _app.Run(dialog);
        }
        finally
        {
            if (timer is not null)
            {
                _app.RemoveTimeout(timer);
            }
        }

        return dialog.Finished;
    }

    /// <summary>
    /// The waiting dialog. Shows the number of active commands; "Force kill" kills them and keeps waiting; "Cancel" or Esc
    /// closes it. The dialog stops itself through <see cref="Finish"/> when nothing is active.
    /// </summary>
    private sealed class WaitDialog : Dialog<bool>
    {
        private readonly IApplication _app;
        private readonly Action _forceKill;
        private readonly Label _text;

        public WaitDialog(IApplication app, int remaining, Action forceKill)
        {
            _app = app;
            _forceKill = forceKill;
            Title = "Quit";
            Width = 56;
            Height = 7;

            _text = new Label { X = 1, Y = 0, Width = 52, Height = 2 };
            Add(_text);
            SetRemaining(remaining);

            var force = new Button { Text = "_Force kill" };
            force.Accepting += (_, e) =>
            {
                // Keep the dialog open: the processes are being killed and the timer closes it when they are gone.
                e.Handled = true;
                _forceKill();
            };
            AddButton(force);
            AddButton(new Button { Text = "_Cancel", IsDefault = true });
        }

        /// <summary>True when the dialog closed because no command remained active.</summary>
        public bool Finished { get; private set; }

        public void SetRemaining(int remaining)
        {
            _text.Text = $"Stopping {remaining} command(s)…";
        }

        /// <summary>Closes the dialog as finished. Call on the UI thread.</summary>
        public void Finish()
        {
            Finished = true;
            Result = true;
            _app.RequestStop(this);
        }
    }
}
