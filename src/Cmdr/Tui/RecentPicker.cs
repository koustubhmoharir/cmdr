using System.Collections.ObjectModel;
using Cmdr.Core.Config;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Session = Cmdr.Core.Session.Session;

namespace Cmdr.Tui;

/// <summary>
/// The screen shown by <c>cmdr</c> with no arguments: the recently opened config files, numbered. Enter opens the selected
/// one (<see cref="Session"/> is then set and the window stops); Esc or Ctrl+Q quits with no session.
/// </summary>
internal sealed class RecentPicker : Window
{
    private readonly IApplication _app;
    private readonly RecentFiles _recent;
    private readonly string? _home;
    private readonly NumberTyping _typing = new();
    private readonly FrameView _frame;
    private readonly RecentList _list;
    private readonly StatusLine _status;

    /// <summary>Creates the picker over the recent list. Removals and opened files are saved to the list's store.</summary>
    public RecentPicker(IApplication app, RecentFiles recent)
    {
        _app = app;
        _recent = recent;
        _home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // No outer frame: the list has its own border, as the panes of the main window do.
        BorderStyle = LineStyle.None;

        _list = new RecentList(OnListKey)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            // Type-to-search would take the digit keys; numbers select instead.
            KeystrokeNavigator = null,
        };
        _list.Accepting += (_, e) =>
        {
            e.Handled = true;
            Open();
        };

        _frame = new FrameView { Title = "Recent configs", X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(1) };
        _frame.Add(_list);

        _status = new StatusLine { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1 };
        _status.Show("Enter Open  Del Remove  0-9 Select by number  Esc/^Q Quit", message: false);

        Add(_frame, _status);
        Rebuild(0);
        _list.SetFocus();
    }

    /// <summary>The session of the config the user opened, or null when the user quit the picker.</summary>
    public Session? Session { get; private set; }

    /// <summary>Esc keeps its default Quit binding; Ctrl+Q quits too.</summary>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Q.WithCtrl)
        {
            _app.RequestStop(this);
            return true;
        }

        return base.OnKeyDown(key);
    }

    // Digits select a row; Del removes it. Any other key ends a number being typed.
    private bool OnListKey(Key key)
    {
        if (DigitOf(key) is char digit)
        {
            int? index = _typing.Type(digit, DateTimeOffset.UtcNow, _recent.Files.Count);
            if (index is int selected)
            {
                SelectRow(selected);
            }

            return true;
        }

        _typing.Reset();
        if (key == Key.Delete)
        {
            Remove();
            return true;
        }

        return false;
    }

    private void Open()
    {
        if (_list.SelectedItem is not int row || row < 0 || row >= _recent.Files.Count)
        {
            return;
        }

        string path = _recent.Files[row];
        Session session;
        try
        {
            session = Session.Load(path);
        }
        catch (ConfigException ex)
        {
            MessageBox.ErrorQuery(_app, "Configuration", ex.Message, "_OK");
            return;
        }

        _recent.Add(path);
        _recent.TrySave();
        Session = session;
        _app.RequestStop(this);
    }

    private void Remove()
    {
        if (_list.SelectedItem is not int row || row < 0 || row >= _recent.Files.Count)
        {
            return;
        }

        _recent.Remove(_recent.Files[row]);
        _recent.TrySave();
        if (_recent.Files.Count == 0)
        {
            _app.RequestStop(this);
            return;
        }

        Rebuild(Math.Min(row, _recent.Files.Count - 1));
    }

    // Rebuilds the rows from the recent list and selects the row at the index.
    private void Rebuild(int selected)
    {
        IReadOnlyList<string> rows = RecentRows.Build(_recent.Files, File.Exists, _home);
        _list.Source = new ListWrapper<string>(new ObservableCollection<string>(rows));
        SelectRow(selected);
    }

    private void SelectRow(int row)
    {
        _list.SelectedItem = row;
        _list.EnsureSelectedItemVisible();
    }

    // The digit a key types, or null. Keys with Ctrl or Alt are not digits.
    private static char? DigitOf(Key key)
    {
        if (key.IsCtrl || key.IsAlt)
        {
            return null;
        }

        System.Text.Rune rune = key.AsRune;
        return rune.Value is >= '0' and <= '9' ? (char)rune.Value : null;
    }

    /// <summary>The list. Key handling is delegated to the picker, which sees the keys before the list's own handling.</summary>
    private sealed class RecentList : ListView
    {
        private readonly Func<Key, bool> _onKey;

        public RecentList(Func<Key, bool> onKey)
        {
            _onKey = onKey;
            CanFocus = true;
            ShowMarks = false;
        }

        protected override bool OnKeyDown(Key key) => _onKey(key) || base.OnKeyDown(key);
    }
}
