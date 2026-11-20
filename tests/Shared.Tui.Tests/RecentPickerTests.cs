using Shared.Core;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Terminal.Gui.Views;

namespace Shared.Tui.Tests;

/// <summary>
/// The recent-configs picker: opening, removing, number keys, and the Open file action. The dialogs are replaced by the
/// picker's internal seams, and keys are sent through the picker's focus chain as the terminal sends them.
/// </summary>
[Collection("Terminal GUI")]
public sealed class RecentPickerTests
{
    // The picker's hints as the logvu picker shows them: the labels of the keys it performs are clickable, the rest is text.
    private static readonly Hint PickerHint = KeyHints.Join(
        KeyHints.Bound(Key.Enter, "Enter Open"),
        KeyHints.Bound(Key.Delete, "Del Remove"),
        KeyHints.Concat(KeyHints.Bound('o', "o"), KeyHints.Plain("/"), KeyHints.Bound(Key.O.WithCtrl, "^O"), KeyHints.Plain(" Open file")),
        KeyHints.Plain("0-9 Select by number"),
        KeyHints.Concat(KeyHints.Bound(Key.Esc, "Esc"), KeyHints.Plain("/"), KeyHints.Bound(Key.Q.WithCtrl, "^Q"), KeyHints.Plain(" Quit")));

    [Fact]
    public void Enter_on_a_numbered_row_opens_that_config_and_moves_it_to_the_front_of_the_saved_list()
    {
        using var f = new Fixture("a.json", "b.json", "c.json");

        f.Picker.NewKeyDownEvent('2');
        f.Picker.NewKeyDownEvent(Key.Enter);

        Assert.Equal(f.Path("b.json"), f.Picker.OpenedPath);
        Assert.Equal(f.Path("b.json"), f.Picker.Opened!.Path);
        Assert.Equal(new[] { f.Path("b.json"), f.Path("a.json"), f.Path("c.json") }, f.Saved());
    }

    [Fact]
    public void A_config_that_does_not_open_shows_its_error_and_the_picker_stays_open()
    {
        using var f = new Fixture("a.json", "b.json");
        f.Invalid.Add(f.Path("a.json"));

        f.Picker.NewKeyDownEvent(Key.Enter);

        Assert.Equal(new[] { "bad a.json" }, f.Errors);
        Assert.Null(f.Picker.Opened);
        Assert.Null(f.Picker.OpenedPath);
        Assert.Equal(new[] { f.Path("a.json"), f.Path("b.json") }, f.Saved());

        // Still open: the rows answer to the keys after the error.
        f.Picker.NewKeyDownEvent('2');
        f.Picker.NewKeyDownEvent(Key.Enter);
        Assert.Equal(f.Path("b.json"), f.Picker.OpenedPath);
    }

    [Fact]
    public void Del_removes_the_selected_row_and_saves_the_list()
    {
        using var f = new Fixture("a.json", "b.json", "c.json");

        f.Picker.NewKeyDownEvent('2');
        f.Picker.NewKeyDownEvent(Key.Delete);

        Assert.Equal(new[] { f.Path("a.json"), f.Path("c.json") }, f.Saved());
        Assert.Equal(1, f.Picker.Rows.SelectedItem);
        Assert.Null(f.Picker.Opened);
    }

    [Fact]
    public void Removing_the_last_entry_without_open_file_stops_the_picker_and_saves_an_empty_list()
    {
        using var f = new Fixture("a.json");

        f.Picker.NewKeyDownEvent(Key.Delete);

        Assert.Empty(f.Saved());
        Assert.Null(f.Picker.Opened);
    }

    [Fact]
    public void Removing_the_last_entry_with_open_file_keeps_the_picker_and_shows_the_empty_line()
    {
        using var f = new Fixture(openFile: true, "a.json");

        f.Picker.NewKeyDownEvent(Key.Delete);

        Assert.Empty(f.Saved());
        Assert.True(f.Picker.EmptyLine.Visible);
        Assert.Null(f.Picker.Opened);
    }

    [Fact]
    public void Typed_digits_select_a_row_and_an_out_of_range_number_selects_nothing()
    {
        using var f = new Fixture("a.json", "b.json", "c.json");

        f.Picker.NewKeyDownEvent('3');
        Assert.Equal(2, f.Picker.Rows.SelectedItem);

        // 9 is out of range: no row is selected, and the selection stays.
        f.Picker.NewKeyDownEvent('9');
        Assert.Equal(2, f.Picker.Rows.SelectedItem);
    }

    [Fact]
    public void Empty_list_with_open_file_shows_the_empty_line_with_the_open_hint()
    {
        using var f = new Fixture(openFile: true);

        Assert.True(f.Picker.EmptyLine.Visible);
        Assert.Equal("No recent configs. Press o to open one.", f.Picker.EmptyLine.Text);
        Assert.Null(f.Picker.Rows.SelectedItem);
    }

    [Fact]
    public void Non_empty_list_hides_the_empty_line()
    {
        using var f = new Fixture(openFile: true, "a.json");

        Assert.False(f.Picker.EmptyLine.Visible);
    }

    [Fact]
    public void Empty_list_without_open_file_shows_the_empty_line_and_nothing_crashes()
    {
        using var f = new Fixture(openFile: false);

        Assert.True(f.Picker.EmptyLine.Visible);
        Assert.Equal("No recent configs.", f.Picker.EmptyLine.Text);

        // Enter, Del and digits on the empty list do nothing.
        f.Picker.NewKeyDownEvent(Key.Enter);
        f.Picker.NewKeyDownEvent(Key.Delete);
        f.Picker.NewKeyDownEvent('1');

        Assert.Null(f.Picker.Opened);
        Assert.Equal(0, f.Dialogs);
    }

    [Fact]
    public void Open_file_opens_the_chosen_config_and_records_it_at_the_front()
    {
        using var f = new Fixture(openFile: true, "a.json");
        f.NextDialogAnswer = f.Path("chosen.json");

        Assert.True(Press(f, 'o'));

        Assert.Equal(1, f.Dialogs);
        Assert.Equal(f.Path("chosen.json"), f.Picker.OpenedPath);
        Assert.Equal(new[] { f.Path("chosen.json"), f.Path("a.json") }, f.Saved());
    }

    [Fact]
    public void Ctrl_O_opens_the_file_dialog_too()
    {
        using var f = new Fixture(openFile: true);
        f.NextDialogAnswer = f.Path("chosen.json");

        Assert.True(Press(f, Key.O.WithCtrl));

        Assert.Equal(1, f.Dialogs);
        Assert.Equal(f.Path("chosen.json"), f.Picker.OpenedPath);
    }

    [Fact]
    public void A_cancelled_file_dialog_changes_nothing()
    {
        using var f = new Fixture(openFile: true, "a.json");
        f.NextDialogAnswer = null;

        Assert.False(Press(f, 'o'));

        Assert.Equal(1, f.Dialogs);
        Assert.Null(f.Picker.Opened);
        Assert.Empty(f.Errors);
        Assert.Equal(new[] { f.Path("a.json") }, f.Saved());
    }

    [Fact]
    public void A_chosen_file_that_does_not_open_shows_its_error_and_is_not_recorded()
    {
        using var f = new Fixture(openFile: true, "a.json");
        f.NextDialogAnswer = f.Path("bad.json");
        f.Invalid.Add(f.Path("bad.json"));

        Assert.False(Press(f, 'o'));

        Assert.Equal(new[] { "bad bad.json" }, f.Errors);
        Assert.Null(f.Picker.Opened);
        Assert.Equal(new[] { f.Path("a.json") }, f.Saved());
    }

    [Fact]
    public void A_chosen_config_stops_the_picker_run_loop_after_the_dialog_returns()
    {
        using var f = new Fixture(openFile: true, "a.json");
        f.NextDialogAnswer = f.Path("chosen.json");

        // Press returns true only when the picker stopped itself, so the run loop returned because of the pick.
        bool stoppedItself = Press(f, 'o');

        Assert.True(stoppedItself);
        Assert.False(f.Picker.IsRunning);
        Assert.Equal(f.Path("chosen.json"), f.Picker.OpenedPath);
    }

    [Fact]
    public void The_real_open_dialog_accepting_a_typed_path_stops_the_picker()
    {
        using var f = new Fixture(openFile: true, "a.json");
        string file = f.Path("typed.json");
        File.WriteAllText(file, "{}");
        f.UseRealDialog();

        bool stoppedItself = RunPickerLoop(f, iteration =>
        {
            if (iteration == 1)
            {
                f.Picker.NewKeyDownEvent('o');
            }

            // The dialog is the top runnable once it is shown: type the path into it and press Enter.
            if (f.App.TopRunnable is OpenDialog dialog && dialog.Path != file)
            {
                dialog.Path = file;
                dialog.NewKeyDownEvent(Key.Enter);
            }
        });

        Assert.True(stoppedItself);
        Assert.Equal(file, f.Picker.OpenedPath);
        Assert.Equal(new[] { file, f.Path("a.json") }, f.Saved());
    }

    [Fact]
    public void The_alternate_key_opens_the_selected_row_with_its_loader_and_records_it_at_the_front()
    {
        using var f = new Fixture("a.json", "b.json", "c.json");
        var alternateLoads = new List<string>();
        f.UseAlternate(new PickerOpen<FakeConfig>(Key.F2, path =>
        {
            alternateLoads.Add(path);
            return (new FakeConfig(path), null);
        }));

        f.Picker.NewKeyDownEvent('2');
        Assert.True(Press(f, Key.F2));

        Assert.Equal(new[] { f.Path("b.json") }, alternateLoads);
        Assert.Empty(f.Loads);
        Assert.Equal(f.Path("b.json"), f.Picker.OpenedPath);
        Assert.Equal(new[] { f.Path("b.json"), f.Path("a.json"), f.Path("c.json") }, f.Saved());
    }

    [Fact]
    public void A_config_the_alternate_loader_does_not_open_shows_its_error_and_the_picker_stays_open()
    {
        using var f = new Fixture("a.json", "b.json");
        f.UseAlternate(new PickerOpen<FakeConfig>(Key.F2, path => (null, $"edit failed {System.IO.Path.GetFileName(path)}")));

        f.Picker.NewKeyDownEvent(Key.F2);

        Assert.Equal(new[] { "edit failed a.json" }, f.Errors);
        Assert.Null(f.Picker.Opened);
        Assert.Equal(new[] { f.Path("a.json"), f.Path("b.json") }, f.Saved());
    }

    [Fact]
    public void A_load_the_user_cancelled_shows_no_error_changes_nothing_and_the_picker_stays()
    {
        using var f = new Fixture("a.json", "b.json");
        f.Cancelled.Add(f.Path("b.json"));
        f.Picker.NewKeyDownEvent('2');

        // Press returns false when the picker did not stop itself: it is still running when the loop times out.
        Assert.False(Press(f, Key.Enter));

        Assert.Empty(f.Errors);
        Assert.Null(f.Picker.Opened);
        Assert.Null(f.Picker.OpenedPath);
        Assert.Equal(new[] { f.Path("a.json"), f.Path("b.json") }, f.Saved());
        Assert.Equal(1, f.Picker.Rows.SelectedItem);
    }

    [Fact]
    public void A_cancelled_alternate_load_shows_no_error_and_changes_nothing()
    {
        using var f = new Fixture("a.json", "b.json");
        f.UseAlternate(new PickerOpen<FakeConfig>(Key.F2, _ => (null, null)));

        Assert.False(Press(f, Key.F2));

        Assert.Empty(f.Errors);
        Assert.Null(f.Picker.Opened);
        Assert.Equal(new[] { f.Path("a.json"), f.Path("b.json") }, f.Saved());
    }

    [Fact]
    public void The_open_keys_do_nothing_without_the_open_file_action()
    {
        using var f = new Fixture(openFile: false, "a.json");

        f.Picker.NewKeyDownEvent('o');
        f.Picker.NewKeyDownEvent(Key.O.WithCtrl);

        Assert.Equal(0, f.Dialogs);
        Assert.Null(f.Picker.Opened);
    }

    [Fact]
    public void Without_a_store_an_opened_config_is_the_result_and_nothing_is_saved()
    {
        using var f = new Fixture(openFile: true, store: false);
        f.NextDialogAnswer = f.Path("chosen.json");

        Assert.True(Press(f, 'o'));

        Assert.Equal(f.Path("chosen.json"), f.Picker.OpenedPath);
        Assert.False(File.Exists(f.StorePath));
    }

    [Fact]
    public void Without_a_store_the_list_is_empty_and_the_picker_still_shows()
    {
        using var f = new Fixture(openFile: true, store: false);

        Assert.True(f.Picker.EmptyLine.Visible);
        Assert.Null(f.Picker.Opened);
    }

    [Fact]
    public void Ctrl_Q_quits_with_nothing_opened_and_sets_QuitRequested()
    {
        using var f = new Fixture("a.json", "b.json");

        Assert.True(Press(f, Key.Q.WithCtrl));

        Assert.True(f.Picker.QuitRequested);
        Assert.Null(f.Picker.Opened);
        Assert.Null(f.Picker.OpenedPath);
        Assert.Equal(new[] { f.Path("a.json"), f.Path("b.json") }, f.Saved());
    }

    [Fact]
    public void Esc_quits_with_nothing_opened_and_leaves_QuitRequested_false()
    {
        using var f = new Fixture("a.json");

        // Esc is bound to the application's Quit, so it is raised on the application's keyboard as the terminal sends it.
        bool stoppedItself = RunPickerLoop(f, iteration =>
        {
            if (iteration == 1)
            {
                f.App.Keyboard.RaiseKeyDownEvent(Key.Esc);
            }
        });

        Assert.True(stoppedItself);

        Assert.False(f.Picker.QuitRequested);
        Assert.Null(f.Picker.Opened);
    }

    [Fact]
    public void The_hint_row_shows_the_hint_text_and_the_bottom_row_draws_it()
    {
        using var f = new Fixture("a.json");

        Assert.Equal(PickerHint.Text, f.Picker.HintsRow.Shown.Text);

        string? row = null;
        RunPickerLoop(f, iteration =>
        {
            if (iteration == 2)
            {
                row = ReadRow(f.App, Fixture.ScreenHeight - 1);
                f.App.RequestStop(f.Picker);
            }
        });

        Assert.Equal(PickerHint.Text, row);
    }

    [Fact]
    public void Clicking_Enter_Open_opens_the_selected_row()
    {
        using var f = new Fixture("a.json", "b.json", "c.json");
        f.Picker.NewKeyDownEvent('2');
        int column = ColumnOf(f, "Enter Open");

        bool stoppedItself = RunPickerLoop(f, iteration =>
        {
            if (iteration == 1)
            {
                ClickAt(f, column);
            }
        });

        Assert.True(stoppedItself);
        Assert.Equal(f.Path("b.json"), f.Picker.OpenedPath);
        Assert.Equal(new[] { f.Path("b.json"), f.Path("a.json"), f.Path("c.json") }, f.Saved());
    }

    [Fact]
    public void Clicking_Del_Remove_removes_the_selected_row()
    {
        using var f = new Fixture("a.json", "b.json", "c.json");
        f.Picker.NewKeyDownEvent('2');
        int column = ColumnOf(f, "Del Remove");
        bool running = false;

        RunPickerLoop(f, iteration =>
        {
            if (iteration == 1)
            {
                ClickAt(f, column);
            }

            if (iteration == 2)
            {
                running = f.Picker.IsRunning;
                f.App.RequestStop(f.Picker);
            }
        });

        Assert.True(running);
        Assert.Equal(new[] { f.Path("a.json"), f.Path("c.json") }, f.Saved());
        Assert.Null(f.Picker.Opened);
    }

    [Fact]
    public void Clicking_Esc_quits_with_nothing_opened_and_leaves_QuitRequested_false()
    {
        using var f = new Fixture("a.json");
        int column = ColumnOf(f, "Esc");

        bool stoppedItself = RunPickerLoop(f, iteration =>
        {
            if (iteration == 1)
            {
                ClickAt(f, column);
            }
        });

        Assert.True(stoppedItself);
        Assert.False(f.Picker.QuitRequested);
        Assert.Null(f.Picker.Opened);
        Assert.Equal(new[] { f.Path("a.json") }, f.Saved());
    }

    [Fact]
    public void Clicking_Ctrl_Q_quits_with_nothing_opened_and_sets_QuitRequested()
    {
        using var f = new Fixture("a.json");
        int column = ColumnOf(f, "^Q");

        bool stoppedItself = RunPickerLoop(f, iteration =>
        {
            if (iteration == 1)
            {
                ClickAt(f, column);
            }
        });

        Assert.True(stoppedItself);
        Assert.True(f.Picker.QuitRequested);
        Assert.Null(f.Picker.Opened);
    }

    [Fact]
    public void Clicking_o_opens_the_file_dialog_and_opens_the_chosen_config()
    {
        using var f = new Fixture(openFile: true, "a.json");
        f.NextDialogAnswer = f.Path("chosen.json");
        int column = ColumnOf(f, "o/^O");

        bool stoppedItself = RunPickerLoop(f, iteration =>
        {
            if (iteration == 1)
            {
                ClickAt(f, column);
            }
        });

        Assert.True(stoppedItself);
        Assert.Equal(1, f.Dialogs);
        Assert.Equal(f.Path("chosen.json"), f.Picker.OpenedPath);
        Assert.Equal(new[] { f.Path("chosen.json"), f.Path("a.json") }, f.Saved());
    }

    [Fact]
    public void Clicking_Ctrl_O_opens_the_file_dialog_too()
    {
        using var f = new Fixture(openFile: true);
        f.NextDialogAnswer = f.Path("chosen.json");
        int column = ColumnOf(f, "^O");

        bool stoppedItself = RunPickerLoop(f, iteration =>
        {
            if (iteration == 1)
            {
                ClickAt(f, column);
            }
        });

        Assert.True(stoppedItself);
        Assert.Equal(1, f.Dialogs);
        Assert.Equal(f.Path("chosen.json"), f.Picker.OpenedPath);
    }

    [Fact]
    public void Clicking_0_9_Select_by_number_does_nothing()
    {
        using var f = new Fixture("a.json", "b.json");
        f.Picker.NewKeyDownEvent('2');
        int column = ColumnOf(f, "0-9 Select");
        bool running = false;

        RunPickerLoop(f, iteration =>
        {
            if (iteration == 1)
            {
                ClickAt(f, column);
            }

            if (iteration == 2)
            {
                running = f.Picker.IsRunning;
                f.App.RequestStop(f.Picker);
            }
        });

        Assert.True(running);
        Assert.Equal(1, f.Picker.Rows.SelectedItem);
        Assert.Equal(0, f.Dialogs);
        Assert.Null(f.Picker.Opened);
        Assert.False(f.Picker.QuitRequested);
        Assert.Equal(new[] { f.Path("a.json"), f.Path("b.json") }, f.Saved());
    }

    // Sends a key on the picker's first loop iteration and runs the loop. True when the picker stopped by itself.
    private static bool Press(Fixture f, Key key) => RunPickerLoop(f, iteration =>
    {
        if (iteration == 1)
        {
            f.Picker.NewKeyDownEvent(key);
        }
    });

    // The column of the first character of the label in the hint row. The row is drawn from column 0, one column per ASCII character.
    private static int ColumnOf(Fixture f, string label)
    {
        int column = f.Picker.HintsRow.Shown.Text.IndexOf(label, StringComparison.Ordinal);
        Assert.True(column >= 0, $"'{label}' is not in '{f.Picker.HintsRow.Shown.Text}'");
        return column;
    }

    // A left press and release on the hint row, through the application's input pipeline.
    private static void ClickAt(Fixture f, int column) =>
        f.App.InjectSequence(InputInjectionExtensions.LeftButtonClick(new(column, Fixture.ScreenHeight - 1)));

    // The text of a row of the screen as drawn, with trailing spaces trimmed.
    private static string ReadRow(IApplication app, int row)
    {
        var driver = app.Driver!;
        var text = new System.Text.StringBuilder();
        for (int column = 0; column < Fixture.ScreenWidth; column++)
        {
            text.Append(driver.Contents![row, column].Grapheme);
        }

        return text.ToString().TrimEnd();
    }

    // Runs the picker in the application's own run loop, so that its stop request and the dialogs it shows behave as they do
    // in the application. The action runs on every iteration. The loop is stopped after a bound of iterations; the result
    // is false then.
    private static bool RunPickerLoop(Fixture f, Action<int> onIteration)
    {
        const int MaxIterations = 40;
        int iteration = 0;
        bool stoppedItself = true;
        EventHandler<EventArgs<IApplication?>> handler = (_, _) =>
        {
            iteration++;
            onIteration(iteration);
            if (iteration < MaxIterations)
            {
                return;
            }

            stoppedItself = false;
            f.App.RequestStop();
            f.App.RequestStop(f.Picker);
        };

        f.App.Iteration += handler;
        f.App.Run(f.Picker);
        f.App.Iteration -= handler;
        return stoppedItself;
    }

    /// <summary>A picker over a temporary store, with fake loading and fake dialogs.</summary>
    private sealed class Fixture : IDisposable
    {
        // The screen the picker is drawn on: the hint row is its last row.
        public const int ScreenWidth = 120;
        public const int ScreenHeight = 10;

        private readonly IApplication _app;
        private readonly string _directory;
        private readonly bool _store;

        public Fixture(params string[] entries) : this(openFile: false, entries) { }

        public Fixture(bool openFile, params string[] entries) : this(openFile, store: true, entries) { }

        public Fixture(bool openFile, bool store, params string[] entries)
        {
            _store = store;
            _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "shared-tui-picker-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            if (store)
            {
                RecentFiles seed = RecentFiles.Load(StorePath);
                foreach (string entry in entries.Reverse())
                {
                    seed.Add(Path(entry));
                }

                seed.TrySave();
            }

            _app = Application.Create();
            _app.Init("ansi");
            _app.Driver!.SetScreenSize(ScreenWidth, ScreenHeight);
            Picker = new RecentPicker<FakeConfig>(
                _app,
                store ? RecentFiles.Load(StorePath) : null,
                TryOpen,
                "Recent configs",
                PickerHint,
                openFile,
                showError: message => Errors.Add(message),
                pickFile: () =>
                {
                    Dialogs++;
                    return NextDialogAnswer;
                });
        }

        public RecentPicker<FakeConfig> Picker { get; private set; }

        public IApplication App => _app;

        /// <summary>Replaces the picker with one that has the alternate open (and no Open file action).</summary>
    public void UseAlternate(PickerOpen<FakeConfig> alternate)
    {
        Picker.Dispose();
        Picker = new RecentPicker<FakeConfig>(
            _app,
            RecentFiles.Load(StorePath),
            TryOpen,
            "Recent configs",
            PickerHint,
            openFile: false,
            showError: message => Errors.Add(message),
            pickFile: () => null,
            alternate);
    }

    /// <summary>Replaces the picker with one that shows the real file dialog.</summary>
        public void UseRealDialog()
        {
            Picker.Dispose();
            Picker = new RecentPicker<FakeConfig>(_app, RecentFiles.Load(StorePath), TryOpen, "Recent configs", PickerHint, openFile: true);
        }

        public string StorePath => System.IO.Path.Combine(_directory, "recent.json");

        /// <summary>The error messages shown, in order.</summary>
        public List<string> Errors { get; } = new();

        /// <summary>The paths (as given) that fail to open.</summary>
        public HashSet<string> Invalid { get; } = new();

        /// <summary>The paths (as given) whose load the user cancels: the loader returns neither a value nor an error.</summary>
        public HashSet<string> Cancelled { get; } = new();

        /// <summary>The paths the picker's own loader was asked to load, in order.</summary>
        public List<string> Loads { get; } = new();

        /// <summary>The number of times the file dialog was shown.</summary>
        public int Dialogs { get; set; }

        /// <summary>What the next file dialog returns: a path, or null for cancelled.</summary>
        public string? NextDialogAnswer { get; set; }

        /// <summary>The full path of a file name in the temporary folder.</summary>
        public string Path(string name) => System.IO.Path.GetFullPath(System.IO.Path.Combine(_directory, name));

        /// <summary>The paths in the saved store, most recent first. Empty without a store.</summary>
        public IReadOnlyList<string> Saved() => _store ? RecentFiles.Load(StorePath).Files : Array.Empty<string>();

        public void Dispose()
        {
            Picker.Dispose();
            _app.Dispose();
            Directory.Delete(_directory, recursive: true);
        }

        private (FakeConfig? Value, string? Error) TryOpen(string path)
        {
            Loads.Add(path);
            if (Cancelled.Contains(path))
            {
                return (null, null);
            }

            if (Invalid.Contains(path))
            {
                return (null, $"bad {System.IO.Path.GetFileName(path)}");
            }

            return (new FakeConfig(path), null);
        }
    }

    private sealed record FakeConfig(string Path);
}
