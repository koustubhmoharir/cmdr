using System.Reflection;
using Cmdr.Core.Config;
using Cmdr.Tui;
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
            .Select(i => new InputPlaceholders.InputRequest(i == 29 ? new string('x', 100) : $"input{i}", false))
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
            .Select(i => new InputPlaceholders.InputRequest(i == 29 ? new string('x', 100) : $"input{i}", false))
            .ToArray();
        ConstructorInfo constructor = typeof(InputDialog).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        using var dialog = (InputDialog)constructor.Invoke(new object[] { app, "test", requests });
        FieldInfo fieldsField = typeof(InputDialog).GetField("_fields", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var fields = (Terminal.Gui.Views.TextField[])fieldsField.GetValue(dialog)!;

        System.Drawing.Rectangle frame = default;
        System.Drawing.Rectangle field = default;
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            fields[^1].SetFocus();
            app.Driver!.SetScreenSize(40, 12);
            app.LayoutAndDraw();
            frame = dialog.Frame;
            field = fields[^1].FrameToScreen();
            app.RequestStop(dialog);
            return false;
        });
        app.Run(dialog);

        Assert.True(frame.X >= 0 && frame.Y >= 0, $"dialog at {frame}");
        Assert.True(frame.Right <= 40 && frame.Bottom <= 12, $"dialog at {frame}");
        Assert.True(field.X >= 0 && field.Right <= 40 && field.Y >= 0 && field.Bottom <= 12, $"focused field at {field}");
    }
}

[CollectionDefinition("Terminal GUI", DisableParallelization = true)]
public sealed class TerminalGuiTestCollection;
