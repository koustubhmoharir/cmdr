using System.Collections.ObjectModel;
using Shared.Core;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Shared.Tui;

/// <summary>
/// An alternate way to open the selected row of a <see cref="RecentPicker{T}"/>: the key that opens it, with the loader it uses. The
/// loader works as the picker's <c>tryOpen</c> does (see <see cref="RecentPicker{T}"/>).
/// </summary>
/// <typeparam name="T">The loaded config.</typeparam>
/// <param name="Key">The key that, pressed in the list, opens the selected row with <paramref name="TryOpen"/>.</param>
/// <param name="TryOpen">Loads the config at a path, as the picker's <c>tryOpen</c> does.</param>
public sealed record PickerOpen<T>(Key Key, Func<string, (T? Value, string? Error)> TryOpen);

/// <summary>
/// The screen shown by an application with no arguments: the recently opened config files, numbered. Enter opens the selected
/// one (<see cref="Opened"/> is then set and the window stops); Esc or Ctrl+Q quits with nothing opened. With
/// <paramref name="openFile"/>, <c>o</c> and Ctrl+O open a file chosen in a file dialog, and the picker shows even when the
/// recent list is empty.
/// </summary>
/// <typeparam name="T">The loaded config. <see cref="Opened"/> holds the value <c>tryOpen</c> returned for the config opened.</typeparam>
public sealed class RecentPicker<T> : Window
    where T : class
{
    private readonly IApplication _app;
    private readonly RecentFiles? _recent;
    private readonly Func<string, (T? Value, string? Error)> _tryOpen;
    private readonly PickerOpen<T>? _alternate;
    private readonly bool _openFile;
    private readonly Action<string> _showError;
    private readonly Func<string?> _pickFile;
    private readonly string? _home;
    private readonly NumberTyping _typing = new();
    private readonly FrameView _frame;
    private readonly RecentList _list;
    private readonly Label _empty;
    private readonly HintsLine _hints;

    /// <summary>
    /// Creates the picker over the recent list. Opened files and removals are saved to the list's store. A null
    /// <paramref name="recent"/> is a list with no store: it is always empty and nothing is saved.
    /// </summary>
    /// <param name="app">The running application. The picker stops it.</param>
    /// <param name="recent">The recent list, or null when there is no store.</param>
    /// <param name="tryOpen">
    /// Loads the config at a path. Returns the loaded value with a null error when the config opened, or a null value with the
    /// message to show when it did not. The value is kept as <see cref="Opened"/>, so it is not loaded twice. Returns a null value
    /// with a null error when the user cancelled inside the load (for example by declining a question): the picker then stays as it
    /// is, with the list unchanged and no error shown.
    /// </param>
    /// <param name="title">The title of the frame around the list.</param>
    /// <param name="hint">
    /// The key hints on the bottom row. A click on a key's label performs the key, as typing it does. Lists the Open file keys when
    /// <paramref name="openFile"/> is set.
    /// </param>
    /// <param name="openFile">
    /// Whether the Open file action is enabled: <c>o</c> and Ctrl+O show a file dialog, filtered by <paramref name="fileTypes"/>.
    /// </param>
    /// <param name="alternate">
    /// An alternate way to open the selected row, or null for none. Its key opens the row with its loader, as Enter opens it with
    /// <paramref name="tryOpen"/>: the file is recorded at the front of the list when it opens, and an error or a cancel is handled the same way.
    /// </param>
    /// <param name="fileTypes">
    /// The file types the Open file dialog offers, the first being selected. Null offers <c>.json</c> config files, as cmdr and logvu
    /// need.
    /// </param>
    public RecentPicker(
        IApplication app,
        RecentFiles? recent,
        Func<string, (T? Value, string? Error)> tryOpen,
        string title,
        Hint hint,
        bool openFile,
        PickerOpen<T>? alternate = null,
        IReadOnlyList<IAllowedType>? fileTypes = null)
        : this(app, recent, tryOpen, title, hint, openFile, showError: null, pickFile: null, alternate, fileTypes)
    {
    }

    /// <summary>
    /// Creates the picker with replaceable dialogs, so that tests can run the picker without showing them. The public
    /// constructor passes null for both.
    /// </summary>
    /// <param name="showError">Shows an error message. Null shows it in an error dialog.</param>
    /// <param name="pickFile">Returns the path chosen in the file dialog, or null when cancelled. Null shows the file dialog.</param>
    /// <param name="alternate">The alternate way to open the selected row, or null for none. See the public constructor.</param>
    internal RecentPicker(
        IApplication app,
        RecentFiles? recent,
        Func<string, (T? Value, string? Error)> tryOpen,
        string title,
        Hint hint,
        bool openFile,
        Action<string>? showError,
        Func<string?>? pickFile,
        PickerOpen<T>? alternate = null,
        IReadOnlyList<IAllowedType>? fileTypes = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(tryOpen);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(hint);

        _app = app;
        _recent = recent;
        _tryOpen = tryOpen;
        _alternate = alternate;
        _openFile = openFile;
        _showError = showError ?? (message => MessageBox.ErrorQuery(app, "Configuration", message, "_OK"));
        _pickFile = pickFile ?? (() => ShowOpenDialog(app, fileTypes ?? [new AllowedType("Config files", ".json")]));
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
            OpenSelected(_tryOpen);
        };

        _empty = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            Text = openFile ? "No recent configs. Press o to open one." : "No recent configs.",
            Visible = false,
        };

        _frame = new FrameView { Title = title, X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(1) };
        _frame.Add(_list, _empty);

        _hints = new HintsLine { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1 };
        _hints.Show(hint);

        Add(_frame, _hints);
        Rebuild(0);
        _list.SetFocus();
    }

    /// <summary>The config the user opened, or null when the user quit the picker.</summary>
    public T? Opened { get; private set; }

    /// <summary>The path of the config the user opened, or null when the user quit the picker.</summary>
    public string? OpenedPath { get; private set; }

    /// <summary>
    /// Whether the user quit with Ctrl+Q, as opposed to Esc or nothing. Both leave <see cref="Opened"/> null; a caller that
    /// shows the picker over a running application can use this to quit the application too.
    /// </summary>
    public bool QuitRequested { get; private set; }

    // The rows' list and the empty-state line, for the tests.
    internal ListView Rows => _list;

    internal Label EmptyLine => _empty;

    internal HintsLine HintsRow => _hints;

    /// <summary>Esc keeps its default Quit binding; Ctrl+Q quits too.</summary>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Q.WithCtrl)
        {
            QuitRequested = true;
            _app.RequestStop(this);
            return true;
        }

        return base.OnKeyDown(key);
    }

    // The recent list's entries, or none without a store.
    private IReadOnlyList<string> Entries => _recent?.Files ?? Array.Empty<string>();

    // Digits select a row; Del removes it; o and Ctrl+O open a file. Any other key ends a number being typed.
    private bool OnListKey(Key key)
    {
        if (DigitOf(key) is char digit)
        {
            int? index = _typing.Type(digit, DateTimeOffset.UtcNow, Entries.Count);
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

        if (_alternate is { } alternate && key == alternate.Key)
        {
            OpenSelected(alternate.TryOpen);
            return true;
        }

        if (_openFile && IsOpenKey(key))
        {
            // The dialog runs after this key is handled, so it is not nested in the key dispatch that asked for it.
            _app.Invoke(() => OpenFile());
            return true;
        }

        return false;
    }

    // Opens the selected row with the loader.
    private void OpenSelected(Func<string, (T? Value, string? Error)> tryOpen)
    {
        if (_list.SelectedItem is not int row || row < 0 || row >= Entries.Count)
        {
            return;
        }

        OpenPath(Entries[row], tryOpen);
    }

    private void OpenFile()
    {
        string? path = _pickFile();
        if (path is null)
        {
            return;
        }

        OpenPath(path, _tryOpen);
    }

    // Loads the config at the path. A config that does not open shows its error and the picker stays. A load the user cancelled
    // (no value, no error) leaves the picker as it is, with no error. One that opens is moved to the front of the recent list
    // (saved) and the picker stops with it as the result.
    private void OpenPath(string path, Func<string, (T? Value, string? Error)> tryOpen)
    {
        (T? value, string? error) = tryOpen(path);
        if (value is null)
        {
            if (error is null)
            {
                return;
            }

            _showError(error);
            Rebuild(Math.Clamp(_list.SelectedItem ?? 0, 0, Math.Max(0, Entries.Count - 1)));
            return;
        }

        _recent?.Add(path);
        _recent?.TrySave();
        Opened = value;
        OpenedPath = path;
        _app.RequestStop(this);
    }

    private void Remove()
    {
        if (_list.SelectedItem is not int row || row < 0 || row >= Entries.Count)
        {
            return;
        }

        _recent?.Remove(Entries[row]);
        _recent?.TrySave();

        // Without the Open file action the picker has nothing to show once the list is empty, so it stops, as it does for
        // cmdr. With it, the picker stays and shows the empty state.
        if (Entries.Count == 0 && !_openFile)
        {
            _app.RequestStop(this);
            return;
        }

        Rebuild(Math.Min(row, Entries.Count - 1));
    }

    // Rebuilds the rows from the recent list and selects the row at the index. An empty list has no row to select.
    private void Rebuild(int selected)
    {
        IReadOnlyList<string> rows = RecentRows.Build(Entries, File.Exists, _home);
        _list.Source = new ListWrapper<string>(new ObservableCollection<string>(rows));
        _empty.Visible = rows.Count == 0;
        if (rows.Count > 0)
        {
            SelectRow(selected);
        }
    }

    private void SelectRow(int row)
    {
        _list.SelectedItem = row;
        _list.EnsureSelectedItemVisible();
    }

    // The file dialog for a config file, started in the current directory. Returns the chosen path, or null when cancelled.
    private static string? ShowOpenDialog(IApplication app, IReadOnlyList<IAllowedType> fileTypes)
    {
        using var dialog = new OpenDialog
        {
            AllowedTypes = [.. fileTypes],
            OpenMode = OpenMode.File,
            AllowsMultipleSelection = false,
            MustExist = true,
        };
        app.Run(dialog);

        string? path = dialog.FilePaths.FirstOrDefault();
        return string.IsNullOrWhiteSpace(path) ? null : path;
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

    // o, or Ctrl+O. A key with Alt is not.
    private static bool IsOpenKey(Key key)
    {
        if (key == Key.O.WithCtrl)
        {
            return true;
        }

        return !key.IsCtrl && !key.IsAlt && key.AsRune.Value == 'o';
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
