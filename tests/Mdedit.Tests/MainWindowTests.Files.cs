using System.Text;
using Mdedit.Tui;
using Shared.Core;
using Terminal.Gui.App;

namespace Mdedit.Tests;

/// <summary>
/// The helpers of the window tests that touch files: a file opened from given bytes in a folder of its own, a new file that does not exist
/// yet, and a window run with a width, a first-save action and the picker of the test.
/// </summary>
public sealed partial class MainWindowTests
{
    // The row of the message line in a 40 by 12 window.
    private const int MessageRow = 10;

    // A file opened from bytes, in a new folder that the test removes afterwards. The file is opened as the host opens it.
    private OpenedFile OpenedBytes(string name, byte[] bytes)
    {
        string path = Path.Combine(NewFolder(), name);
        File.WriteAllBytes(path, bytes);
        return MdeditHost.Load(path);
    }

    // The new file at a path: unsaved and empty, as the host makes it. Its folder is the test's.
    private static OpenedFile NewFile(string path) =>
        new(path, TextFile.CreateNew(path, SourceText.Create(string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), hasBom: false)));

    // A folder of its own for a test; removed by Dispose.
    private string NewFolder()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"mdedit-window-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _folders.Add(dir);
        return dir;
    }

    // Runs the window at a size with the picker of the test and the first-save action. The steps get the application, for the dialogs.
    private static Session RunWith(
        OpenedFile file,
        int width,
        int height,
        Func<(PickOutcome Outcome, OpenedFile? File)>? pick,
        Action? onFirstSave,
        params Action<IApplication, MainWindow>[] steps)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(width, height);
        using var window = new MainWindow(
            file,
            pick ?? (() => (PickOutcome.Back, null)),
            (_, _) => () => { },
            MainWindow.LargeFileBytes,
            onFirstSave);
        List<TerminalScreen> screens = TerminalHarness.RunWindow(
            app,
            window,
            width,
            height,
            steps.Select(step => (Action)(() => step(app, window))).ToArray());
        return new Session(screens, window);
    }

    // The UTF-8 bytes of a text, without a byte order mark.
    private static byte[] Bytes(string text) => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
}
