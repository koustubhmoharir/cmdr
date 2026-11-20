using System.Reflection;
using System.Text;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Tests;

/// <summary>
/// How the list draws its file rows and the rows below them: the glyph and display path of a file row, the faint " (base)" marker of a
/// base file, the groups one level deeper than before, and no source label on a base group's header.
/// </summary>
[Collection("Terminal GUI")]
public sealed class FileRowDrawTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int Width = 60;
    private const int Height = 12;

    [Fact]
    public void A_file_row_shows_its_path_with_the_base_marker_and_the_groups_and_commands_sit_one_level_deeper()
    {
        using TempChain chain = TempChain.Open();

        string[] rows = Frames(chain.Session, Width, Height, controller => { })[0];

        // The base file, its group shared (with the command tool and its detail line), the group extra, the opened file, its group main.
        Assert.Equal("▾ shared/base.json (base)", rows[0]);
        Assert.Equal("  ▾ shared  [parallel]", rows[1]);
        Assert.Equal("tool", rows[2].Substring(6, 4));
        Assert.Equal("not started", rows[3].Substring(6));
        Assert.Equal("  ▾ extra  [parallel]", rows[4]);
        Assert.Equal("▾ cmdr.json", rows[5]);
        Assert.Equal("  ▾ main  [parallel]", rows[6]);
        Assert.Equal("mine", rows[7].Substring(6, 4));
    }

    [Fact]
    public void A_base_group_header_no_longer_names_its_file()
    {
        using TempChain chain = TempChain.Open();

        string[] rows = Frames(chain.Session, Width, Height, controller => { })[0];

        Assert.DoesNotContain("base.json", rows[1], StringComparison.Ordinal);
        Assert.DoesNotContain("base.json", rows[4], StringComparison.Ordinal);
    }

    [Fact]
    public void A_collapsed_file_row_shows_the_collapsed_glyph_and_hides_its_rows()
    {
        using TempChain chain = TempChain.Open();

        string[][] frames = Frames(
            chain.Session,
            Width,
            Height,
            controller => { },
            controller =>
            {
                typeof(MainController).GetMethod("FocusKey", Members)!.Invoke(controller, new object[] { chain.Session.Sources[0] });
                controller.OnListKey(Key.CursorLeft);
            });

        // Collapsed, the base file hides its group shared, the command tool and the group extra; the opened file follows it.
        Assert.Equal("▾ shared/base.json (base)", frames[0][0]);
        Assert.Equal("▸ shared/base.json (base)", frames[1][0]);
        Assert.Equal("▾ cmdr.json", frames[1][1]);
        Assert.Equal("  ▾ main  [parallel]", frames[1][2]);
    }

    /// <summary>
    /// Draws the command list of the controller over the session in a window of the given size, one frame per step. Each step runs on
    /// the UI loop before its frame. Returns the text of each row of each frame, with the trailing spaces cut.
    /// </summary>
    private static string[][] Frames(CmdrSession session, int width, int height, params Action<MainController>[] steps)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(width, height);

        var frames = new List<string[]>();
        using var controller = new MainController(app, session);
        controller.Build();
        CommandListSource source = (CommandListSource)typeof(MainController).GetField("_source", Members)!.GetValue(controller)!;
        var list = new CommandListView(controller, source) { X = 0, Y = 0, Width = width, Height = height };
        var window = new Window { X = 0, Y = 0, Width = width, Height = height, BorderStyle = LineStyle.None };
        window.Add(list);

        int frame = 0;
        app.AddTimeout(TimeSpan.FromMilliseconds(50), () =>
        {
            steps[frame](controller);

            // The controller redraws its own list, not this one, so this list is invalidated here.
            list.SetNeedsDraw();
            app.LayoutAndDraw();
            var driver = app.Driver!;
            var rows = new string[height];
            for (int r = 0; r < height; r++)
            {
                var text = new StringBuilder();
                for (int c = 0; c < width; c++)
                {
                    text.Append(driver.Contents![r, c].Grapheme);
                }

                rows[r] = text.ToString().TrimEnd();
            }

            frames.Add(rows);
            frame++;
            if (frame == steps.Length)
            {
                app.RequestStop(window);
                return false;
            }

            return true;
        });
        app.Run(window);
        return frames.ToArray();
    }
}
