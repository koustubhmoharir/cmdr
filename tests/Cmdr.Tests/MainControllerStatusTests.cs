using System.Reflection;
using Cmdr.Tui;
using Terminal.Gui.App;

namespace Cmdr.Tests;

[Collection("Terminal GUI")]
public sealed class MainControllerStatusTests
{
    [Fact]
    public void Expired_message_restores_unchanged_hints_and_right_side_quit_hint()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using TempSession temp = TempSession.Sample();
        using var controller = new MainController(app, temp.Session);
        controller.Build();

        Type type = typeof(MainController);
        MethodInfo tick = type.GetMethod("OnFastTick", BindingFlags.Instance | BindingFlags.NonPublic)!;

        // Build caches hints before the rows and focus exist. One tick establishes the stable hints; the message scenario
        // starts from those, so a message that leaves the hints out of date cannot pass by the first tick redrawing them.
        tick.Invoke(controller, null);
        (string hintText, string hintRight, bool hintIsMessage) = ReadStatus(controller);
        tick.Invoke(controller, null);
        Assert.Equal((hintText, hintRight, hintIsMessage), ReadStatus(controller));
        Assert.False(hintIsMessage);
        Assert.Equal("  ^Q Quit", hintRight);

        MethodInfo showMessage = type.GetMethod("ShowMessage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        showMessage.Invoke(controller, new object[] { "Temporary status" });
        (string shown, _, bool shownIsMessage) = ReadStatus(controller);
        Assert.Equal("Temporary status", shown);
        Assert.True(shownIsMessage);

        FieldInfo until = type.GetField("_messageUntil", BindingFlags.Instance | BindingFlags.NonPublic)!;
        until.SetValue(controller, DateTimeOffset.UtcNow.AddSeconds(-1));
        tick.Invoke(controller, null);

        // The expired message gives way to exactly the hints that were shown before it.
        Assert.Equal((hintText, hintRight, hintIsMessage), ReadStatus(controller));
    }

    /// <summary>The status line's displayed text, its right-side text, and whether it shows a message.</summary>
    private static (string Text, string Right, bool IsMessage) ReadStatus(MainController controller)
    {
        FieldInfo statusField = typeof(MainController).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object status = statusField.GetValue(controller)!;
        Type statusType = status.GetType();
        string text = (string)statusType.GetField("_text", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(status)!;
        string right = (string)statusType.GetField("_right", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(status)!;
        bool isMessage = (bool)statusType.GetField("_message", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(status)!;
        return (text, right, isMessage);
    }
}
