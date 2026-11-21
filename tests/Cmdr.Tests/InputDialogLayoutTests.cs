using System.Reflection;
using Cmdr.Tui;
using Shared.Processes;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;

namespace Cmdr.Tests;

[Collection("Terminal GUI")]
public sealed class InputDialogLayoutTests
{
    [Fact]
    public void Focusing_late_input_scrolls_it_into_the_form_viewport_and_long_names_leave_room_for_a_field()
    {
        using var app = Application.Create();
        app.Init("ansi");
        var requests = Enumerable.Range(0, 30)
            .Select(i => new InputRequest(i == 29 ? new string('x', 100) : $"input{i}", false))
            .ToArray();
        ConstructorInfo constructor = typeof(InputDialog).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        using var dialog = (InputDialog)constructor.Invoke(new object[] { app, "test", requests });

        FieldInfo fieldsField = typeof(InputDialog).GetField("_fields", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var fields = (Terminal.Gui.Views.TextField[])fieldsField.GetValue(dialog)!;
        FieldInfo formField = typeof(InputDialog).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var form = (View)formField.GetValue(dialog)!;
        bool focused = false;
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            fields[^1].SetFocus();
            focused = fields[^1].HasFocus;
            dialog.Width = 40;
            app.LayoutAndDraw();
            app.RequestStop(dialog);
            return false;
        });
        app.Run(dialog);

        Assert.True(focused);
        Assert.True(form.Viewport.Y > 0);
        Assert.True(fields[^1].Frame.Width > 0);
        Assert.True(fields[^1].Frame.X > 0);
        Assert.True(fields[^1].Frame.Right <= form.Viewport.Width);
    }

    [Fact]
    public void Shrinking_the_terminal_shrinks_the_dialog_and_keeps_the_focused_field_on_screen()
    {
        using var app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(80, 24);
        var requests = Enumerable.Range(0, 30)
            .Select(i => new InputRequest(i == 29 ? new string('x', 100) : $"input{i}", false))
            .ToArray();
        ConstructorInfo constructor = typeof(InputDialog).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        using var dialog = (InputDialog)constructor.Invoke(new object[] { app, "test", requests });
        FieldInfo fieldsField = typeof(InputDialog).GetField("_fields", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var fields = (Terminal.Gui.Views.TextField[])fieldsField.GetValue(dialog)!;

        System.Drawing.Rectangle frame = default;
        System.Drawing.Rectangle field = default;
        int formHeight = -1;
        int dialogViewportHeight = -1;
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            fields[^1].SetFocus();
            app.Driver!.SetScreenSize(40, 12);
            app.LayoutAndDraw();
            frame = dialog.Frame;
            field = fields[^1].FrameToScreen();
            formHeight = FormOf(dialog).Frame.Height;
            dialogViewportHeight = dialog.Viewport.Height;
            app.RequestStop(dialog);
            return false;
        });
        app.Run(dialog);

        Assert.True(frame.X >= 0 && frame.Y >= 0, $"dialog at {frame}");
        Assert.True(frame.Right <= 40 && frame.Bottom <= 12, $"dialog at {frame}");
        Assert.True(field.X >= 0 && field.Right <= 40 && field.Y >= 0 && field.Bottom <= 12, $"focused field at {field}");
        Assert.Equal(dialogViewportHeight, formHeight);
    }

    [Fact]
    public void One_input_gets_a_visible_field_on_a_normal_screen()
    {
        using var app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(120, 30);
        using InputDialog dialog = CreateDialog(app, new[] { "db-name" });
        var fields = FieldsOf(dialog);
        var form = FormOf(dialog);

        System.Drawing.Rectangle frame = default;
        System.Drawing.Rectangle field = default;
        int formFrameHeight = -1;
        int formViewportHeight = -1;
        string screen = string.Empty;
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            app.LayoutAndDraw();
            frame = dialog.Frame;
            field = fields[0].FrameToScreen();
            formFrameHeight = form.Frame.Height;
            formViewportHeight = form.Viewport.Height;
            screen = ScreenText(app);
            app.RequestStop(dialog);
            return false;
        });
        app.Run(dialog);

        Assert.Equal(1, formFrameHeight);
        Assert.Equal(1, formViewportHeight);
        Assert.True(frame.Contains(field), $"field {field} outside dialog {frame}");
        Assert.True(field.Width > 0, $"field has no width: {field}");
        Assert.Contains("db-name", screen);
    }

    [Fact]
    public void Three_inputs_get_distinct_visible_rows_on_a_normal_screen()
    {
        using var app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(120, 30);
        using InputDialog dialog = CreateDialog(app, new[] { "alpha", "beta", "gamma" });
        var fields = FieldsOf(dialog);

        System.Drawing.Rectangle frame = default;
        var fieldRects = new System.Drawing.Rectangle[fields.Length];
        string screen = string.Empty;
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            app.LayoutAndDraw();
            frame = dialog.Frame;
            for (int i = 0; i < fields.Length; i++)
            {
                fieldRects[i] = fields[i].FrameToScreen();
            }

            screen = ScreenText(app);
            app.RequestStop(dialog);
            return false;
        });
        app.Run(dialog);

        foreach (System.Drawing.Rectangle rect in fieldRects)
        {
            Assert.True(frame.Contains(rect), $"field {rect} outside dialog {frame}");
        }

        Assert.Equal(fieldRects.Length, fieldRects.Select(rect => rect.Y).Distinct().Count());
        Assert.Contains("alpha", screen);
        Assert.Contains("beta", screen);
        Assert.Contains("gamma", screen);
    }

    private static InputDialog CreateDialog(IApplication app, string[] names)
    {
        var requests = names.Select(name => new InputRequest(name, false)).ToArray();
        ConstructorInfo constructor = typeof(InputDialog).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        return (InputDialog)constructor.Invoke(new object[] { app, "test", requests });
    }

    private static Terminal.Gui.Views.TextField[] FieldsOf(InputDialog dialog) =>
        (Terminal.Gui.Views.TextField[])typeof(InputDialog).GetField("_fields", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!;

    private static View FormOf(InputDialog dialog) =>
        (View)typeof(InputDialog).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;

    /// <summary>The rendered screen, one string per row, read from the driver's cells.</summary>
    private static string ScreenText(IApplication app)
    {
        var driver = app.Driver!;
        var rows = new string[driver.Rows];
        for (int r = 0; r < driver.Rows; r++)
        {
            var chars = new char[driver.Cols];
            for (int c = 0; c < driver.Cols; c++)
            {
                string grapheme = driver.Contents![r, c].Grapheme;
                chars[c] = string.IsNullOrEmpty(grapheme) ? ' ' : grapheme[0];
            }

            rows[r] = new string(chars);
        }

        return string.Join("\n", rows);
    }
}

[CollectionDefinition("Terminal GUI", DisableParallelization = true)]
public sealed class TerminalGuiTestCollection;
