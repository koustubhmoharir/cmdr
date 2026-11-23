using Shared.Core;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Shared.Tui.Tests;

/// <summary>
/// How the recent-configs picker looks on screen: the gutter and the bold selection, the colours of the name, the directory, the
/// repository name and the (not found) column, and no focus background on any row. Drawn with the ANSI driver and read back cell by cell.
/// </summary>
[Collection("Terminal GUI")]
public sealed class RecentPickerLookTests : IDisposable
{
    private const int Width = 100;
    private const int Height = 8;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "picker-look-" + Guid.NewGuid().ToString("N"));
    private readonly string _repo;
    private readonly string _store;
    private readonly string _present;
    private readonly string _plain;
    private readonly string _missing;

    public RecentPickerLookTests()
    {
        // The repository is "cmdr" (a .git folder), with docs/a.json and docs/missing.json (not written). plain/b.json is outside it.
        string repo = _repo = Path.Combine(_root, "cmdr");
        string docs = Path.Combine(repo, "docs");
        string plain = Path.Combine(_root, "plain");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        Directory.CreateDirectory(docs);
        Directory.CreateDirectory(plain);

        _present = Path.Combine(docs, "a.json");
        _plain = Path.Combine(plain, "b.json");
        _missing = Path.Combine(docs, "missing.json");
        File.WriteAllText(_present, "{}");
        File.WriteAllText(_plain, "{}");

        _store = Path.Combine(_root, "recent.json");
        RecentFiles seed = RecentFiles.Load(_store);
        seed.Add(_missing);
        seed.Add(_plain);
        seed.Add(_present);
        seed.TrySave();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void The_selected_row_is_gutted_and_bold_and_moving_the_selection_moves_the_mark()
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(Width, Height);
        var picker = new RecentPicker<LookConfig>(
            app,
            RecentFiles.Load(_store),
            TryOpen,
            "Recent configs",
            KeyHints.Plain("Enter Open"),
            openFile: false,
            showError: null,
            pickFile: null,
            repoRootOf: directory => RepoRoot.Outermost(directory, folder => folder == _repo));

        try
        {
            List<TerminalScreen> screens = TerminalHarness.RunWindow(
                app,
                picker,
                Width,
                Height,
                () => picker.NewKeyDownEvent(Key.CursorDown));

            TerminalScreen first = screens[0];

            // Row 1 is selected: the gutter is "> " (RowOf finds that text), and every part of the row is bold.
            first.RowOf("> 1  a.json");
            Assert.True(StyleOf(first.At("> 1  a.json")).HasFlag(TextStyle.Bold));
            Assert.True(StyleOf(first.At("1  a.json")).HasFlag(TextStyle.Bold));
            Assert.True(StyleOf(first.At("1  a.json")).HasFlag(TextStyle.Faint));
            Assert.True(StyleOf(first.At("a.json")).HasFlag(TextStyle.Bold));

            // Row 2 is not: two spaces in the gutter, no bold.
            Assert.False(StyleOf(first.At("  2  b.json")).HasFlag(TextStyle.Bold));
            Assert.False(StyleOf(first.At("b.json")).HasFlag(TextStyle.Bold));

            // Each part has its colour: the name is the normal foreground, the directory grey, the repository name blue, and the
            // missing column red.
            Assert.Equal(first.Normal.Foreground, first.At("a.json").Foreground);
            Assert.Equal(first.Normal.Foreground, first.At("b.json").Foreground);
            Assert.Equal(PickerColors.Directory, first.At(_root).Foreground);
            Assert.Equal(PickerColors.Repo, first.At("cmdr" + Path.DirectorySeparatorChar + "docs").Foreground);
            Assert.Equal(PickerColors.Directory, first.At(Path.DirectorySeparatorChar + "docs").Foreground);
            Assert.Equal(PickerColors.Missing, first.At("(not found)").Foreground);

            // The plain row (outside the repository) has no repository colour.
            Assert.Equal(PickerColors.Directory, first.At(Path.Combine(_root, "plain")).Foreground);

            // No row takes the focus background: the rows keep the list's normal background.
            Assert.NotEqual(first.Focus.Background, first.Normal.Background);
            int firstRow = first.RowOf("a.json");
            int lastRow = first.RowOf("missing.json");
            for (int row = firstRow; row <= lastRow; row++)
            {
                for (int column = 0; column < Width; column++)
                {
                    Assert.NotEqual(first.Focus.Background, first.Cells[row][column].Attribute.Background);
                }
            }

            // Down moves the mark to row 2 and row 1 loses it.
            TerminalScreen moved = screens[1];
            Assert.True(StyleOf(moved.At("> 2  b.json")).HasFlag(TextStyle.Bold));
            Assert.False(StyleOf(moved.At("1  a.json")).HasFlag(TextStyle.Bold));
            Assert.DoesNotContain("> 1", moved.Text(moved.RowOf("a.json")));
        }
        finally
        {
            picker.Dispose();
        }
    }

    private static TextStyle StyleOf(Attribute attribute) => attribute.Style;

    private static (LookConfig? Value, string? Error) TryOpen(string path) => (new LookConfig(path), null);

    private sealed record LookConfig(string Source);
}
