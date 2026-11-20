using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Shared.Core;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Terminal.Gui.Views;

namespace Cmdr.Tests;

/// <summary>
/// The picker's bottom row as click targets: a left press on a label performs its key through the application's keyboard, and the
/// picker reacts as it does to the key. The clicks are injected through the input pipeline, inside the picker's own run loop.
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
            Assert.Equal("Enter Open  F2 Edit  Del Remove  0-9 Select by number  Esc/^Q Quit", Status(picker).Shown.Text);
            Assert.Equal(string.Empty, Status(picker).ShownRight.Text);
            app.RequestStop(picker);
        });
    }

    [Fact]
    public void Clicking_Enter_Open_opens_the_selected_row_and_moves_it_to_the_front_of_the_list()
    {
        using var f = new Fixture();

        f.Run((app, picker) =>
        {
            Assert.Equal(0, List(picker).SelectedItem);

            Click(app, picker, "Enter Open");
        });

        Assert.NotNull(f.Picker.Session);
        Assert.Equal(SessionMode.Run, f.Picker.Session!.Mode);
        Assert.Equal(f.Alpha, f.Saved()[0]);
        f.Picker.Session!.Dispose();
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

        Assert.NotNull(f.Picker.Session);
        Assert.Equal(SessionMode.Edit, f.Picker.Session!.Mode);
        Assert.Equal(f.Alpha, f.Saved()[0]);
        f.Picker.Session!.Dispose();
    }

    [Fact]
    public void Clicking_Del_Remove_removes_the_selected_row_from_the_list()
    {
        using var f = new Fixture();

        f.Run((app, picker) =>
        {
            Assert.Equal(0, List(picker).SelectedItem);

            Click(app, picker, "Del Remove");
            app.RequestStop(picker);
        });

        Assert.Null(f.Picker.Session);
        Assert.DoesNotContain(f.Alpha, f.Saved());
        Assert.Contains(f.Beta, f.Saved());
    }

    [Fact]
    public void Clicking_Esc_quits_with_no_session()
    {
        using var f = new Fixture();

        f.Run((app, picker) => Click(app, picker, "Esc"));

        Assert.Null(f.Picker.Session);
    }

    [Fact]
    public void Clicking_Ctrl_Q_quits_with_no_session()
    {
        using var f = new Fixture();

        f.Run((app, picker) => Click(app, picker, "^Q"));

        Assert.Null(f.Picker.Session);
    }

    [Fact]
    public void Clicking_the_number_hint_does_nothing_and_the_picker_keeps_running()
    {
        using var f = new Fixture();
        IReadOnlyList<string> before = f.Saved();
        bool stillRunning = false;

        f.Run((app, picker) =>
        {
            Click(app, picker, "0-9 Select by number");
            app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
            {
                stillRunning = true;
                app.RequestStop(picker);
                return false;
            });
        });

        Assert.True(stillRunning);
        Assert.Null(f.Picker.Session);
        Assert.Equal(before, f.Saved());
        Assert.Equal(0, List(f.Picker).SelectedItem);
    }

    private static StatusLine Status(RecentPicker picker) => Field<StatusLine>(picker, "_status");

    private static ListView List(RecentPicker picker) => Field<ListView>(picker, "_list");

    /// <summary>
    /// Clicks the first character of the label in the bar, through the input pipeline: a press and a release at the bottom row.
    /// </summary>
    private static void Click(IApplication app, RecentPicker picker, string label)
    {
        app.LayoutAndDraw();
        int column = ColumnOf(picker, label);
        app.InjectSequence(InputInjectionExtensions.LeftButtonClick(new Point(column, Height - 1)));
        app.LayoutAndDraw();
    }

    /// <summary>The display column of the label's first character in the bar.</summary>
    private static int ColumnOf(RecentPicker picker, string label)
    {
        StatusLine status = Status(picker);
        int index = status.Shown.Text.IndexOf(label, StringComparison.Ordinal);
        Assert.True(index >= 0, $"'{label}' is not in the bar '{status.Shown.Text}'");
        return TextWidth.Columns(status.Shown.Text[..index]);
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

            RecentFiles seed = RecentFiles.Load(Store);
            seed.Add(Beta);
            seed.Add(Alpha);
            seed.TrySave();

            _app = Application.Create();
            _app.Init("ansi");
            _app.Driver!.SetScreenSize(Width, Height);
            Picker = new RecentPicker(_app, RecentFiles.Load(Store));
        }

        public string Alpha { get; }

        public string Beta { get; }

        public string Store { get; }

        public RecentPicker Picker { get; }

        /// <summary>The paths in the saved store, most recent first.</summary>
        public IReadOnlyList<string> Saved() => RecentFiles.Load(Store).Files;

        /// <summary>Runs the picker, and the check on its loop once the picker is up. Failures of the check are rethrown.</summary>
        public void Run(Action<IApplication, RecentPicker> check)
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
