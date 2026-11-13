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
        MethodInfo showMessage = type.GetMethod("ShowMessage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        showMessage.Invoke(controller, new object[] { "Temporary status" });
        FieldInfo until = type.GetField("_messageUntil", BindingFlags.Instance | BindingFlags.NonPublic)!;
        until.SetValue(controller, DateTimeOffset.UtcNow.AddSeconds(-1));

        MethodInfo tick = type.GetMethod("OnFastTick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        tick.Invoke(controller, null);

        FieldInfo statusField = type.GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object status = statusField.GetValue(controller)!;
        Type statusType = status.GetType();
        string displayed = (string)statusType.GetField("_text", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(status)!;
        string right = (string)statusType.GetField("_right", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(status)!;
        bool isMessage = (bool)statusType.GetField("_message", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(status)!;

        Assert.NotEqual("Temporary status", displayed);
        Assert.Equal("  ^Q Quit", right);
        Assert.False(isMessage);
    }
}
