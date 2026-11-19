using System.Reflection;
using Logvu.Core.Filter;
using Logvu.Core.Parsing;
using Logvu.Tui;
using Shared.Core;
using Shared.Core;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Logvu.Tests;

/// <summary>
/// The filter editor: the pre-fill from the active filter, the validation of the fields, Clear, the buttons (Apply last and the default), and
/// the layout at 80 by 24, where every field must be above the button area.
/// </summary>
[Collection("Terminal GUI")]
public sealed class FilterDialogTests
{
    // UTC+2 with no daylight saving; 23:30 UTC is already the 19th in the zone.
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test+02", TimeSpan.FromHours(2), "Test+02", "Test+02");
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 23, 30, 0, TimeSpan.Zero);

    [Fact]
    public void The_editor_is_pre_filled_from_the_active_filter()
    {
        using IApplication app = CreateApp();
        EventFilter active = new(new FilterSpec(
            MinLevel: LogLevel.Warn,
            FileContains: "app",
            From: new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.FromHours(2)),
            Text: new SearchQuery("timeout", true, false, true),
            Fields: [new FieldPredicate("user", FieldOperator.Equals, "alice"), new FieldPredicate("logger", FieldOperator.Contains, "Orders")]));
        using var dialog = new FilterDialog(app, active, Zone, () => Now);

        FilterInput shown = dialog.Input();

        Assert.Equal(FilterForm.InputOf(active, Zone), shown);
        Assert.Equal(LogLevel.Warn, shown.MinLevel);
        Assert.Equal("app", shown.File);
        Assert.Equal("2026-11-18 10:00:00", shown.From);
        Assert.Equal("", shown.To);
        Assert.True(shown.CaseSensitive);
        Assert.True(shown.Regex);
        Assert.False(shown.WholeWord);
        Assert.Equal("user equals alice\nlogger contains Orders", shown.Predicates);
    }

    [Fact]
    public void An_empty_editor_applies_as_no_filter()
    {
        using IApplication app = CreateApp();
        using var dialog = new FilterDialog(app, null, Zone, () => Now);

        Assert.True(dialog.TryRead(out FilterEdit? edit, out string? error));

        Assert.Null(error);
        Assert.NotNull(edit);
        Assert.Null(edit!.Filter);
    }

    [Fact]
    public void A_valid_editor_gives_the_filter_it_shows()
    {
        using IApplication app = CreateApp();
        using var dialog = new FilterDialog(app, null, Zone, () => Now);
        Control<TextField>(dialog, "_file").Text = "app";
        Control<TextField>(dialog, "_from").Text = "10:00";
        Control<TextField>(dialog, "_text").Text = "time.*out";
        Control<CheckBox>(dialog, "_regex").Value = CheckState.Checked;
        Control<TextView>(dialog, "_predicates").Text = "code equals 42";

        Assert.True(dialog.TryRead(out FilterEdit? edit, out string? error));

        Assert.Null(error);
        Assert.NotNull(edit!.Filter);
        Assert.Equal("app", edit.Filter!.FileContains);
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.FromHours(2)), edit.Filter.From);
        Assert.Equal(new SearchQuery("time.*out", false, false, true), edit.Filter.Text);
        Assert.Equal([new FieldPredicate("code", FieldOperator.Equals, "42")], edit.Filter.Fields);
    }

    [Fact]
    public void An_invalid_time_is_an_error_that_names_its_field()
    {
        using IApplication app = CreateApp();
        using var dialog = new FilterDialog(app, null, Zone, () => Now);
        Control<TextField>(dialog, "_to").Text = "2026-11-18";

        Assert.False(dialog.TryRead(out FilterEdit? edit, out string? error));

        Assert.Null(edit);
        Assert.StartsWith("Time to: ", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_time_to_before_the_time_from_is_an_error_that_names_the_later_field()
    {
        using IApplication app = CreateApp();
        using var dialog = new FilterDialog(app, null, Zone, () => Now);
        Control<TextField>(dialog, "_from").Text = "2026-11-18 11:00";
        Control<TextField>(dialog, "_to").Text = "2026-11-18 10:00";

        Assert.False(dialog.TryRead(out _, out string? error));

        Assert.StartsWith("Time to: ", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_regular_expression_is_an_error_that_names_the_text_field()
    {
        using IApplication app = CreateApp();
        using var dialog = new FilterDialog(app, null, Zone, () => Now);
        Control<TextField>(dialog, "_text").Text = "(";
        Control<CheckBox>(dialog, "_regex").Value = CheckState.Checked;

        Assert.False(dialog.TryRead(out _, out string? error));

        Assert.StartsWith("Text: ", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_predicate_line_is_an_error_that_names_the_line()
    {
        using IApplication app = CreateApp();
        using var dialog = new FilterDialog(app, null, Zone, () => Now);
        Control<TextView>(dialog, "_predicates").Text = "code equals 42\nnot a predicate";

        Assert.False(dialog.TryRead(out _, out string? error));

        Assert.StartsWith("Field predicates: Line 2", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Clear_empties_every_field_and_the_editor_applies_as_no_filter()
    {
        using IApplication app = CreateApp();
        EventFilter active = new(new FilterSpec(MinLevel: LogLevel.Error, FileContains: "Orders", Fields: [new FieldPredicate("a", FieldOperator.Equals, "b")]));
        using var dialog = new FilterDialog(app, active, Zone, () => Now);

        dialog.Clear();

        Assert.Equal(new FilterInput(null, "", "", "", "", false, false, false, ""), dialog.Input());
        Assert.True(dialog.TryRead(out FilterEdit? edit, out _));
        Assert.Null(edit!.Filter);
    }

    [Fact]
    public void Apply_is_the_last_button_and_the_default_and_Clear_and_Cancel_are_not()
    {
        using IApplication app = CreateApp();
        using var dialog = new FilterDialog(app, null, Zone, () => Now);

        Button[] buttons = dialog.Buttons;

        Assert.Equal(new[] { "_Clear", "_Cancel", "_Apply" }, buttons.Select(button => button.Text));
        Assert.True(buttons[^1].IsDefault);
        Assert.False(buttons[0].IsDefault);
        Assert.False(buttons[1].IsDefault);
    }

    [Fact]
    public void Apply_with_valid_fields_ends_the_editor_with_the_filter()
    {
        using IApplication app = CreateApp();
        using var dialog = new FilterDialog(app, null, Zone, () => Now);
        Control<TextField>(dialog, "_file").Text = "app";
        FilterEdit? result = RunAndPress(app, dialog, 2);

        Assert.NotNull(result);
        Assert.Equal("app", result!.Filter!.FileContains);
    }

    [Fact]
    public void Clear_keeps_the_editor_open_until_it_is_applied()
    {
        using IApplication app = CreateApp();
        EventFilter active = new(new FilterSpec(FileContains: "Orders"));
        using var dialog = new FilterDialog(app, active, Zone, () => Now);
        bool stillOpenAfterClear = false;
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            dialog.Buttons[0].NewKeyDownEvent(Key.Enter);
            stillOpenAfterClear = app.TopRunnableView == dialog;
            dialog.Buttons[2].NewKeyDownEvent(Key.Enter);
            return false;
        });

        app.Run(dialog);

        Assert.True(stillOpenAfterClear);
        Assert.NotNull(dialog.Result);
        Assert.Null(dialog.Result!.Filter);
    }

    [Fact]
    public void Cancel_ends_the_editor_without_a_result()
    {
        using IApplication app = CreateApp();
        using var dialog = new FilterDialog(app, null, Zone, () => Now);
        Control<TextField>(dialog, "_file").Text = "app";

        FilterEdit? result = RunAndPress(app, dialog, 1);

        Assert.Null(result);
    }

    [Fact]
    public void Every_field_and_button_is_inside_the_editor_at_80_by_24()
    {
        using IApplication app = CreateApp();
        using var dialog = new FilterDialog(app, null, Zone, () => Now);
        View[] fields =
        [
            Control<OptionSelector>(dialog, "_level"),
            Control<TextField>(dialog, "_file"),
            Control<TextField>(dialog, "_text"),
            Control<CheckBox>(dialog, "_caseSensitive"),
            Control<CheckBox>(dialog, "_regex"),
            Control<CheckBox>(dialog, "_wholeWord"),
            Control<TextField>(dialog, "_from"),
            Control<TextField>(dialog, "_to"),
            Control<TextView>(dialog, "_predicates"),
        ];

        System.Drawing.Rectangle frame = default;
        var fieldRects = new System.Drawing.Rectangle[fields.Length];
        var buttonRects = new System.Drawing.Rectangle[dialog.Buttons.Length];
        string screen = string.Empty;
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            app.LayoutAndDraw();
            frame = dialog.Frame;
            for (int i = 0; i < fields.Length; i++)
            {
                fieldRects[i] = fields[i].FrameToScreen();
            }

            for (int i = 0; i < buttonRects.Length; i++)
            {
                buttonRects[i] = dialog.Buttons[i].FrameToScreen();
            }

            screen = ScreenText(app);
            app.RequestStop(dialog);
            return false;
        });
        app.Run(dialog);

        Assert.True(frame.X >= 0 && frame.Y >= 0 && frame.Right <= 80 && frame.Bottom <= 24, $"dialog at {frame}");
        int buttonTop = buttonRects.Min(rect => rect.Y);
        foreach (System.Drawing.Rectangle rect in fieldRects)
        {
            Assert.True(frame.Contains(rect), $"field {rect} outside dialog {frame}");
            Assert.True(rect.Bottom <= buttonTop, $"field {rect} runs into the button row {buttonTop}");
        }

        foreach (System.Drawing.Rectangle rect in buttonRects)
        {
            Assert.True(frame.Contains(rect), $"button {rect} outside dialog {frame}");
        }

        Assert.Contains("Field predicates", screen, StringComparison.Ordinal);
        Assert.Contains("Fields: path equals|prefix|suffix|contains value", screen, StringComparison.Ordinal);
        Assert.Contains("Time to", screen, StringComparison.Ordinal);
    }

    [Fact]
    public void Tab_visits_each_field_top_to_bottom_then_the_buttons_and_the_level_starts_focused()
    {
        using IApplication app = CreateApp();
        using var dialog = new FilterDialog(app, null, Zone, () => Now);
        View[] order =
        [
            Control<OptionSelector>(dialog, "_level"),
            Control<TextField>(dialog, "_file"),
            Control<TextField>(dialog, "_text"),
            Control<CheckBox>(dialog, "_caseSensitive"),
            Control<CheckBox>(dialog, "_regex"),
            Control<CheckBox>(dialog, "_wholeWord"),
            Control<TextField>(dialog, "_from"),
            Control<TextField>(dialog, "_to"),
            Control<TextView>(dialog, "_predicates"),
            dialog.Buttons[0],
            dialog.Buttons[1],
            dialog.Buttons[2],
        ];
        var focused = new List<View?>();
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            app.LayoutAndDraw();
            focused.Add(Deepest(dialog));
            for (int step = 1; step < order.Length; step++)
            {
                app.Keyboard.RaiseKeyDownEvent(Key.Tab);
                app.LayoutAndDraw();
                focused.Add(Deepest(dialog));
            }

            app.RequestStop(dialog);
            return false;
        });
        app.Run(dialog);

        Assert.Equal(order.Length, focused.Count);
        for (int i = 0; i < order.Length; i++)
        {
            Assert.True(IsWithin(focused[i], order[i]), $"step {i}: focus is on {focused[i]}, expected {order[i]}");
        }
    }

    [Fact]
    public void Typing_letters_on_the_level_does_not_change_it_and_typing_after_Tab_goes_to_the_file_name()
    {
        using IApplication app = CreateApp();
        using var dialog = new FilterDialog(app, null, Zone, () => Now);
        var selector = Control<OptionSelector>(dialog, "_level");
        var file = Control<TextField>(dialog, "_file");
        int? levelAfterTyping = null;
        string? fileText = null;
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            app.LayoutAndDraw();
            foreach (char ch in "Orders")
            {
                app.Keyboard.RaiseKeyDownEvent((Key)ch);
            }

            levelAfterTyping = selector.Value;
            app.Keyboard.RaiseKeyDownEvent(Key.Tab);
            foreach (char ch in "Orders")
            {
                app.Keyboard.RaiseKeyDownEvent((Key)ch);
            }

            fileText = file.Text;
            app.RequestStop(dialog);
            return false;
        });
        app.Run(dialog);

        Assert.Equal(0, levelAfterTyping);
        Assert.Equal("Orders", fileText);
    }

    [Fact]
    public void A_pre_filled_level_is_kept_when_the_editor_runs_and_the_selector_gets_the_focus()
    {
        using IApplication app = CreateApp();
        EventFilter active = new(new FilterSpec(MinLevel: LogLevel.Warn));
        using var dialog = new FilterDialog(app, active, Zone, () => Now);
        LogLevel? shown = null;
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            app.LayoutAndDraw();
            app.Keyboard.RaiseKeyDownEvent(Key.Tab);
            app.Keyboard.RaiseKeyDownEvent(Key.Tab);
            app.Keyboard.RaiseKeyDownEvent(Key.Tab);
            app.Keyboard.RaiseKeyDownEvent(Key.Tab);
            app.Keyboard.RaiseKeyDownEvent(Key.Tab);
            app.Keyboard.RaiseKeyDownEvent(Key.Tab);
            app.Keyboard.RaiseKeyDownEvent(Key.Tab);
            app.Keyboard.RaiseKeyDownEvent(Key.Tab);
            app.Keyboard.RaiseKeyDownEvent(Key.Tab);
            app.Keyboard.RaiseKeyDownEvent(Key.Tab);
            app.Keyboard.RaiseKeyDownEvent(Key.Tab);
            app.Keyboard.RaiseKeyDownEvent(Key.Tab);
            app.Keyboard.RaiseKeyDownEvent(Key.Tab);
            app.LayoutAndDraw();
            shown = dialog.Input().MinLevel;
            app.RequestStop(dialog);
            return false;
        });
        app.Run(dialog);

        Assert.Equal(LogLevel.Warn, shown);
    }

    [Fact]
    public void The_arrow_keys_choose_the_level()
    {
        using IApplication app = CreateApp();
        using var dialog = new FilterDialog(app, null, Zone, () => Now);
        LogLevel? afterRight = null;
        LogLevel? afterLeft = null;
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            app.LayoutAndDraw();
            app.Keyboard.RaiseKeyDownEvent(Key.CursorRight);
            afterRight = dialog.Input().MinLevel;
            app.Keyboard.RaiseKeyDownEvent(Key.CursorRight);
            app.Keyboard.RaiseKeyDownEvent(Key.CursorRight);
            app.Keyboard.RaiseKeyDownEvent(Key.CursorLeft);
            afterLeft = dialog.Input().MinLevel;
            app.RequestStop(dialog);
            return false;
        });
        app.Run(dialog);

        Assert.Equal(LogLevel.Trace, afterRight);
        Assert.Equal(LogLevel.Debug, afterLeft);
    }

    /// <summary>Whether <paramref name="view"/> is <paramref name="ancestor"/> or one of its subviews, at any depth.</summary>
    private static bool IsWithin(View? view, View ancestor)
    {
        for (View? current = view; current is not null; current = current.SuperView)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The view with the focus, following the focused chain of the views down from <paramref name="root"/>.</summary>
    private static View? Deepest(View root)
    {
        View? current = root;
        while (current?.Focused is { } next)
        {
            current = next;
        }

        return current;
    }

    /// <summary>Runs the dialog, presses the button at <paramref name="index"/> once it has shown, and returns the result.</summary>
    private static FilterEdit? RunAndPress(IApplication app, FilterDialog dialog, int index)
    {
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            dialog.Buttons[index].NewKeyDownEvent(Key.Enter);
            return false;
        });
        app.Run(dialog);
        return dialog.Result;
    }

    private static IApplication CreateApp()
    {
        IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(80, 24);
        return app;
    }

    private static T Control<T>(FilterDialog dialog, string name) where T : class =>
        (T)typeof(FilterDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;

    private static string ScreenText(IApplication app)
    {
        var driver = app.Driver!;
        var text = new System.Text.StringBuilder();
        for (int row = 0; row < 24; row++)
        {
            for (int column = 0; column < 80; column++)
            {
                text.Append(driver.Contents![row, column].Grapheme);
            }

            text.Append('\n');
        }

        return text.ToString();
    }
}
