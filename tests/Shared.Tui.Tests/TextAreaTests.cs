using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Shared.Tui.Tests;

/// <summary>
/// The text area as typed, pasted and drawn: the keys go through the application's keyboard to the focused view, the pastes go through
/// the application's paste path, and the screen is read back after each step.
/// </summary>
[Collection("Terminal GUI")]
public sealed class TextAreaTests
{
    [Fact]
    public void Defaults_are_no_auto_indent_no_tab_spaces_tabs_moving_focus_and_line_feed_breaks()
    {
        var area = new TextArea();

        Assert.Null(area.IndentationStrategy);
        Assert.False(area.ConvertTabsToSpaces);
        Assert.False(area.TabInserts);
        Assert.Equal("\n", area.LineBreak);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\n\n")]
    [InlineData("\n\r")]
    [InlineData("\t")]
    public void A_line_break_other_than_LF_CRLF_or_CR_is_rejected(string value)
    {
        var area = new TextArea();

        Assert.Throws<ArgumentException>(() => area.LineBreak = value);
        Assert.Equal("\n", area.LineBreak);
    }

    [Fact]
    public void A_null_line_break_is_rejected()
    {
        var area = new TextArea();

        Assert.Throws<ArgumentNullException>(() => area.LineBreak = null!);
    }

    [Fact]
    public void Enter_after_an_indented_line_does_not_indent_the_new_line()
    {
        using var f = new Fixture();

        f.Run(
            () =>
            {
                f.Area.SetFocus();
                f.Area.Text = "    indented";
                f.Area.CaretOffset = "    indented".Length;
            },
            () =>
            {
                f.Press(Key.Enter);
                Assert.Equal("    indented\n", f.Area.Text);
            });
    }

    [Fact]
    public void Enter_inserts_the_line_break_as_CRLF_exactly()
    {
        using var f = new Fixture();

        f.Run(
            () =>
            {
                f.Area.SetFocus();
                f.Area.Text = "ab";
                f.Area.CaretOffset = 2;
                f.Area.LineBreak = "\r\n";
            },
            () =>
            {
                f.Press(Key.Enter);
                Assert.Equal("ab\r\n", f.Area.Text);
            });
    }

    [Fact]
    public void Enter_over_a_selection_replaces_it_and_one_undo_restores_it()
    {
        using var f = new Fixture();

        f.Run(
            () =>
            {
                f.Area.SetFocus();
                f.Area.Text = "hello world";
                f.Area.CaretOffset = 6;
                SelectForward(f, 5);
                Assert.Equal("world", f.Area.SelectedText);
            },
            () =>
            {
                f.Press(Key.Enter);
                Assert.Equal("hello \n", f.Area.Text);
            },
            () =>
            {
                f.Press(Key.Z.WithCtrl);
                Assert.Equal("hello world", f.Area.Text);
            });
    }

    [Fact]
    public void Ctrl_V_writes_each_line_break_of_the_clipboard_as_the_line_break()
    {
        using var f = new Fixture();
        f.Clipboard.SetClipboardData("a\r\nb\rc\nd");

        f.Run(
            () =>
            {
                f.Area.SetFocus();
                f.Area.Text = "";
                f.Area.CaretOffset = 0;
            },
            () =>
            {
                f.Press(Key.V.WithCtrl);
                Assert.Equal("a\nb\nc\nd", f.Area.Text);
            },
            () =>
            {
                f.Area.Text = "";
                f.Area.CaretOffset = 0;
                f.Area.LineBreak = "\r\n";
                f.Press(Key.V.WithCtrl);
                Assert.Equal("a\r\nb\r\nc\r\nd", f.Area.Text);
            });
    }

    [Fact]
    public void A_terminal_paste_inserts_its_payload_and_not_the_clipboard()
    {
        using var f = new Fixture();
        f.Clipboard.SetClipboardData("CLIPBOARD");

        f.Run(
            () =>
            {
                f.Area.SetFocus();
                f.Area.Text = "";
                f.Area.CaretOffset = 0;
            },
            () =>
            {
                f.App.RaisePasteEvent("p1\r\np2\rp3\np4");
                Assert.Equal("p1\np2\np3\np4", f.Area.Text);
            },
            () =>
            {
                f.Area.Text = "";
                f.Area.CaretOffset = 0;
                f.Area.LineBreak = "\r\n";
                f.App.RaisePasteEvent("q1\nq2");
                Assert.Equal("q1\r\nq2", f.Area.Text);
            });
    }

    [Fact]
    public void A_paste_over_a_selection_replaces_it_and_one_undo_restores_it()
    {
        using var f = new Fixture();
        f.Clipboard.SetClipboardData("X\nY");

        f.Run(
            () =>
            {
                f.Area.SetFocus();
                f.Area.Text = "hello world";
                f.Area.CaretOffset = 6;
                SelectForward(f, 5);
                Assert.Equal("world", f.Area.SelectedText);
            },
            () =>
            {
                f.Press(Key.V.WithCtrl);
                Assert.Equal("hello X\nY", f.Area.Text);
            },
            () =>
            {
                f.Press(Key.Z.WithCtrl);
                Assert.Equal("hello world", f.Area.Text);
            });
    }

    [Fact]
    public void A_terminal_paste_over_a_selection_replaces_it()
    {
        using var f = new Fixture();

        f.Run(
            () =>
            {
                f.Area.SetFocus();
                f.Area.Text = "hello world";
                f.Area.CaretOffset = 6;
                SelectForward(f, 5);
            },
            () =>
            {
                f.App.RaisePasteEvent("X\r\nY");
                Assert.Equal("hello X\nY", f.Area.Text);
            });
    }

    [Fact]
    public void Single_line_pastes_and_typed_text_are_drawn_right_after_the_step()
    {
        using var f = new Fixture();
        f.Clipboard.SetClipboardData("abc");

        List<TerminalScreen> screens = f.Run(
            () =>
            {
                f.Area.SetFocus();
                f.Area.Text = "";
                f.Area.CaretOffset = 0;
            },
            () => f.Press(Key.V.WithCtrl),
            () => f.App.RaisePasteEvent("xyz"),
            () => f.Press((Key)'q'));

        Assert.Equal(0, screens[2].RowOf("abc"));
        Assert.Equal("abc", screens[2].Text(0));
        Assert.Equal("abcxyz", screens[3].Text(0));
        Assert.Equal("abcxyzq", screens[4].Text(0));
    }

    [Fact]
    public void A_tab_is_drawn_to_the_next_multiple_of_four_columns()
    {
        using var f = new Fixture();

        List<TerminalScreen> screens = f.Run(() => f.Area.Text = "a\tb\nabcd\te");

        Assert.Equal("a   b", screens[1].Text(0));
        Assert.Equal("abcd    e", screens[1].Text(1));
    }

    [Fact]
    public void Enter_and_pastes_change_nothing_when_read_only()
    {
        using var f = new Fixture();
        f.Clipboard.SetClipboardData("x");

        f.Run(
            () =>
            {
                f.Area.SetFocus();
                f.Area.Text = "abc";
                f.Area.CaretOffset = 3;
                f.Area.ReadOnly = true;
            },
            () =>
            {
                f.Press(Key.Enter);
                f.Press(Key.V.WithCtrl);
                f.App.RaisePasteEvent("y");
                Assert.Equal("abc", f.Area.Text);
                Assert.True(f.Area.HasFocus);
            });
    }

    [Fact]
    public void Find_Replace_and_Collapse_keys_are_unbound_and_not_handled()
    {
        using var f = new Fixture();
        Key[] keys = [Key.F.WithCtrl, Key.F3, Key.F3.WithShift, Key.H.WithCtrl, Key.M.WithCtrl];
        int finds = 0;
        int replaces = 0;
        f.Area.FindRequested += (_, _) => finds++;
        f.Area.ReplaceRequested += (_, _) => replaces++;

        f.Run(
            () =>
            {
                f.Area.SetFocus();
                f.Area.Text = "hello";
            },
            () =>
            {
                foreach (Key key in keys)
                {
                    Assert.Empty(f.Area.KeyBindings.GetCommands(key));
                    Assert.False(f.Area.NewKeyDownEvent(key), key.ToString());
                }

                Assert.Equal(0, finds);
                Assert.Equal(0, replaces);
                Assert.Equal("hello", f.Area.Text);
            });
    }

    [Fact]
    public void Tab_moves_the_focus_when_TabInserts_is_false()
    {
        using var f = new Fixture();

        f.Run(
            () => f.Area.SetFocus(),
            () =>
            {
                f.Press(Key.Tab);
                Assert.True(f.Other.HasFocus);
                Assert.Equal("", f.Area.Text);
            },
            () =>
            {
                f.Press(Key.Tab.WithShift);
                Assert.True(f.Area.HasFocus);
            });
    }

    [Fact]
    public void Tab_moves_the_focus_when_TabInserts_is_false_and_the_area_is_read_only()
    {
        using var f = new Fixture();

        f.Run(
            () =>
            {
                f.Area.SetFocus();
                f.Area.ReadOnly = true;
            },
            () =>
            {
                f.Press(Key.Tab);
                Assert.True(f.Other.HasFocus);
            });
    }

    [Fact]
    public void Tab_inserts_a_tab_when_TabInserts_is_true_and_setting_it_back_restores_the_focus_move()
    {
        using var f = new Fixture();

        f.Run(
            () =>
            {
                f.Area.SetFocus();
                f.Area.TabInserts = true;
            },
            () =>
            {
                f.Press(Key.Tab);
                Assert.Equal("\t", f.Area.Text);
                Assert.True(f.Area.HasFocus);
            },
            () =>
            {
                f.Area.TabInserts = false;
                f.Press(Key.Tab);
                Assert.True(f.Other.HasFocus);
                Assert.Equal("\t", f.Area.Text);
            },
            () =>
            {
                f.Press(Key.Tab.WithShift);
                Assert.True(f.Area.HasFocus);
                f.Area.TabInserts = true;
                f.Press(Key.Tab);
                Assert.Equal("\t\t", f.Area.Text);
            });
    }

    /// <summary>Extends the selection forward by a number of characters, with the editor's own Shift+Right.</summary>
    private static void SelectForward(Fixture f, int characters)
    {
        for (int i = 0; i < characters; i++)
        {
            f.Press(Key.CursorRight.WithShift);
        }
    }

    /// <summary>
    /// A window with the text area and a button after it, in the ANSI driver, with a fake clipboard. The application is disposed with the
    /// fixture.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        private const int Width = 60;
        private const int Height = 6;

        private readonly IApplication _app;
        private readonly Window _window;

        public Fixture()
        {
            Clipboard = new FakeClipboard();
            _app = Application.Create();
            _app.Init("ansi");
            _app.Driver!.SetScreenSize(Width, Height);
            _app.Driver.Clipboard = Clipboard;

            Area = new TextArea { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(1) };
            Other = new Button { X = 0, Y = Height - 1, Text = "Other" };
            _window = new Window { X = 0, Y = 0, Width = Width, Height = Height, BorderStyle = LineStyle.None };
            _window.Add(Area, Other);
        }

        /// <summary>The application the window runs in. Pastes go through it.</summary>
        public IApplication App => _app;

        /// <summary>The clipboard the application reads for Ctrl+V.</summary>
        public FakeClipboard Clipboard { get; }

        /// <summary>The text area under test, the first focusable view.</summary>
        public TextArea Area { get; }

        /// <summary>The button after the text area, which takes the focus when Tab moves it.</summary>
        public Button Other { get; }

        /// <summary>Sends a key to the application's keyboard, which routes it to the focused view and the application's bindings.</summary>
        public void Press(Key key) => _app.Keyboard.RaiseKeyDownEvent(key);

        /// <summary>Runs the window with the steps, and returns the screens captured by the harness.</summary>
        public List<TerminalScreen> Run(params Action[] steps) => TerminalHarness.RunWindow(_app, _window, Width, Height, steps);

        public void Dispose() => _app.Dispose();
    }

    /// <summary>An in-memory clipboard, so the tests set the clipboard text they paste.</summary>
    private sealed class FakeClipboard : ClipboardBase
    {
        private string _text = string.Empty;

        public override bool IsSupported => true;

        protected override string GetClipboardDataImpl() => _text;

        protected override void SetClipboardDataImpl(string text) => _text = text;
    }
}
