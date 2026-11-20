using System.Drawing;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Cmdr.Tests;

/// <summary>
/// The layout of a dialog on a terminal of a given size: the dialog's screen frame, the top row of its button area, and the screen
/// rectangles of its content controls (fields, labels with text, check boxes, option selectors, dropdowns and the buttons that are not
/// dialog buttons). A content control is fit when it is inside the dialog and above the button area.
/// </summary>
internal sealed record DialogFit(Rectangle Dialog, int ButtonAreaTop, IReadOnlyList<(string Name, Rectangle Rect)> Content);

/// <summary>Lays a dialog out for one frame and reads where its content and buttons are.</summary>
internal static class DialogFitProbe
{
    /// <summary>Runs the dialog on a terminal of the given size for one frame, then closes it, and returns its layout.</summary>
    public static DialogFit Measure(IApplication app, Dialog<bool> dialog, int width = 80, int height = 24)
    {
        DialogFit? result = null;
        app.Driver!.SetScreenSize(width, height);
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            app.LayoutAndDraw();
            result = Read(dialog);
            app.RequestStop(dialog);
            return false;
        });
        app.Run(dialog);
        return result ?? throw new InvalidOperationException("The dialog was not laid out.");
    }

    /// <summary>Asserts that the dialog fits the terminal and that every content control is inside it and above its button area.</summary>
    public static void AssertFits(DialogFit fit, int width = 80, int height = 24)
    {
        Assert.True(fit.Dialog.Right <= width && fit.Dialog.Bottom <= height, $"dialog {fit.Dialog} does not fit {width}x{height}");
        Assert.True(fit.ButtonAreaTop < fit.Dialog.Bottom, $"no dialog button was found in {fit.Dialog}");
        foreach ((string name, Rectangle rect) in fit.Content)
        {
            Assert.True(fit.Dialog.Contains(rect), $"'{name}' at {rect} is outside the dialog {fit.Dialog}");
            Assert.True(rect.Bottom <= fit.ButtonAreaTop, $"'{name}' at {rect} is under the button area, which starts at row {fit.ButtonAreaTop}");
        }
    }

    private static DialogFit Read(Dialog<bool> dialog)
    {
        // The dialog's own buttons are the top of the button area; the controls below are the content. A dialog button is not
        // a subview of the content, but it is listed by the dialog, so the content walk does not see it.
        int buttonAreaTop = dialog.Buttons.Count() == 0
            ? dialog.FrameToScreen().Bottom
            : dialog.Buttons.Min(button => button.FrameToScreen().Y);

        var content = new List<(string, Rectangle)>();
        foreach (View view in Descendants(dialog))
        {
            if (!view.Visible || !IsContent(view, out string name))
            {
                continue;
            }

            Rectangle rect = view.FrameToScreen();
            if (rect.Width > 0 && rect.Height > 0)
            {
                content.Add((name, rect));
            }
        }

        return new DialogFit(dialog.FrameToScreen(), buttonAreaTop, content);
    }

    /// <summary>The content controls: the ones a user sees and edits. An empty label is not content.</summary>
    private static bool IsContent(View view, out string name)
    {
        switch (view)
        {
            case Label label when label.Text.Length > 0:
                name = label.Text;
                return true;
            // A DropDownList is a TextField in Terminal.Gui, so it is matched first.
            case DropDownList:
                name = "dropdown";
                return true;
            case TextField field:
                name = "field " + field.Text;
                return true;
            case TextView area:
                name = "area " + area.Text;
                return true;
            case CheckBox check:
                name = "check " + check.Text;
                return true;
            case Button button:
                name = "button " + button.Text;
                return true;
            default:
                name = view.GetType().Name;
                return view.GetType().Name.StartsWith("OptionSelector", StringComparison.Ordinal);
        }
    }

    /// <summary>Every view below the given one, depth-first.</summary>
    internal static IEnumerable<View> Descendants(View root)
    {
        foreach (View child in root.SubViews)
        {
            yield return child;
            foreach (View descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
