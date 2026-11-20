using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Shared.Core;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Terminal.Gui.Views;

namespace Cmdr.Tests;

/// <summary>
/// The cmdr picker as cmdr builds it (<see cref="TuiHost.CreatePicker"/>): its bottom row's labels are click targets, and a click
/// opens the selected row in the mode its label names. The clicks are injected through the input pipeline, inside the picker's own run loop.
/// The rest of the picker's behaviour (digits, Del, Esc, Ctrl+Q, the errors) is tested with the shared picker.
/// </summary>
[Collection("Terminal GUI")]
public sealed class RecentPickerHintsTests
{
    private const int Width = 120;
    private const int Height = 10;
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void The_bar_text_is_the_picker_hint_text()
    {
        using var f = new Fixture();

        f.Run((app, picker) =>
        {
            Assert.Equal("Enter Open  F2 Edit  Del Remove  0-9 Select by number  Esc/^Q Quit", Hints(picker).Shown.Text);
            Assert.Equal(string.Empty, Hints(picker).ShownRight.Text);
            app.RequestStop(picker);
        });
    }

    [Fact]
    public void Clicking_Enter_Open_opens_the_selected_row_in_run_mode_and_moves_it_to_the_front_of_the_list()
    {
        using var f = new Fixture();

        f.Run((app, picker) =>
        {
            Assert.Equal(0, List(picker).SelectedItem);

            Click(app, picker, "Enter Open");
        });

        Assert.NotNull(f.Picker.Opened);
        Assert.Equal(SessionMode.Run, f.Picker.Opened!.Mode);
        Assert.Equal(f.Alpha, f.Saved()[0]);
        f.Picker.Opened!.Dispose();
    }

    [Fact]
    public void Clicking_F2_Edit_opens_the_selected_row_in_edit_mode_and_moves_it_to_the_front_of_the_list()
    {
        using var f = new Fixture();

        f.Run((app, picker) =>
        {
            Assert.Equal(0, List(picker).SelectedItem);

            Click(app, picker, "F2 Edit");
        });

        Assert.NotNull(f.Picker.Opened);
        Assert.Equal(SessionMode.Edit, f.Picker.Opened!.Mode);
        Assert.Equal(f.Alpha, f.Saved()[0]);
        f.Picker.Opened!.Dispose();
    }

    [Fact]
    public void F2_on_a_file_that_does_not_exist_opens_it_in_edit_mode_without_asking()
    {
        // A question would show a dialog and wait for a key, so the picker would not stop before the timeout.
        using var f = new Fixture();

        f.Run((app, picker) =>
        {
            app.Keyboard.RaiseKeyDownEvent((Key)'3');
            Click(app, picker, "F2 Edit");
        });

        Assert.NotNull(f.Picker.Opened);
        Assert.Equal(SessionMode.Edit, f.Picker.Opened!.Mode);
        Assert.Equal(f.Missing, f.Saved()[0]);
        f.Picker.Opened!.Dispose();
    }

    private static HintsLine Hints(RecentPicker<Session> picker) => Field<HintsLine>(picker, "_hints");

    private static ListView List(RecentPicker<Session> picker) => Field<ListView>(picker, "_list");

    /// <summary>
    /// Clicks the first character of the label in the bar, through the input pipeline: a press and a release at the bottom row.
    /// </summary>
    private static void Click(IApplication app, RecentPicker<Session> picker, string label)
    {
        app.LayoutAndDraw();
        int column = ColumnOf(picker, label);
        app.InjectSequence(InputInjectionExtensions.LeftButtonClick(new Point(column, Height - 1)));
        app.LayoutAndDraw();
    }

    /// <summary>The display column of the label's first character in the bar.</summary>
    private static int ColumnOf(RecentPicker<Session> picker, string label)
    {
        HintsLine hints = Hints(picker);
        int index = hints.Shown.Text.IndexOf(label, StringComparison.Ordinal);
        Assert.True(index >= 0, $"'{label}' is not in the bar '{hints.Shown.Text}'");
        return TextWidth.Columns(hints.Shown.Text[..index]);
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, Members)!.GetValue(target)!;

    /// <summary>
    /// A picker over two real config files, the first of them most recent, with a store in a temporary folder. The picker runs in
    /// the application's loop; a fallback timer stops it when a click does not stop it.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        private readonly string _directory;
        private readonly IApplication _app;

        public Fixture()
        {
            _directory = Directory.CreateTempSubdirectory("cmdr-picker-hints-").FullName;
            Alpha = Path.GetFullPath(WriteConfig("alpha.json"));
            Beta = Path.GetFullPath(WriteConfig("beta.json"));
            Store = Path.Combine(_directory, "recent.json");
            Missing = Path.GetFullPath(Path.Combine(_directory, "missing.json"));

            // The missing file is last in the list: the first two rows are the configs that exist.
            RecentFiles seed = RecentFiles.Load(Store);
            seed.Add(Missing);
            seed.Add(Beta);
            seed.Add(Alpha);
            seed.TrySave();

            _app = Application.Create();
            _app.Init("ansi");
            _app.Driver!.SetScreenSize(Width, Height);
            Picker = TuiHost.CreatePicker(_app, RecentFiles.Load(Store));
        }

        public string Alpha { get; }

        public string Beta { get; }

        public string Store { get; }

        public string Missing { get; }

        public RecentPicker<Session> Picker { get; }

        /// <summary>The paths in the saved store, most recent first.</summary>
        public IReadOnlyList<string> Saved() => RecentFiles.Load(Store).Files;

        /// <summary>Runs the picker, and the check on its loop once the picker is up. Failures of the check are rethrown.</summary>
        public void Run(Action<IApplication, RecentPicker<Session>> check)
        {
            ExceptionDispatchInfo? failure = null;
            bool timedOut = false;
            _app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
            {
                try
                {
                    check(_app, Picker);
                }
                catch (Exception ex)
                {
                    failure = ExceptionDispatchInfo.Capture(ex);
                    _app.RequestStop(Picker);
                }

                return false;
            });
            _app.AddTimeout(TimeSpan.FromSeconds(10), () =>
            {
                timedOut = true;
                _app.RequestStop(Picker);
                return false;
            });

            _app.Run(Picker);

            failure?.Throw();
            Assert.False(timedOut, "The picker did not stop.");
        }

        public void Dispose()
        {
            Picker.Dispose();
            _app.Dispose();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // Best effort: the temporary folder is left behind when it cannot be removed.
            }
        }

        private string WriteConfig(string name)
        {
            string path = Path.Combine(_directory, name);
            ConfigStore.Save(
                new CmdrConfig
                {
                    Groups = [new GroupSpec { Name = "work", Items = { new CommandSpec { Name = "build", Program = "sh" } } }],
                },
                path);
            return path;
        }
    }
}
