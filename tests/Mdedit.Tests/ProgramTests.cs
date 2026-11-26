using System.Text;
using Mdedit;
using Mdedit.Tui;
using Shared.Core;

namespace Mdedit.Tests;

/// <summary>
/// The command line. The host's actions are replaced by fakes, so no terminal is started: the picker and the window are recorded, and the
/// recent list is a temporary store.
/// </summary>
public sealed class ProgramTests : IDisposable
{
    private const string Usage =
        "usage: mdedit <file>\n" +
        "       mdedit               pick a recently used file\n" +
        "       mdedit --version\n";

    private readonly string _store = Path.Combine(Path.GetTempPath(), $"mdedit-store-{Guid.NewGuid():N}.json");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"mdedit-program-{Guid.NewGuid():N}");

    [Fact]
    public void Version_prints_the_informational_version()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        int code = Program.Run(["--version"], stdout, stderr, _ => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException(), () => null);

        Assert.Equal(0, code);
        Assert.StartsWith("mdedit ", stdout.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public void No_arguments_show_the_picker_with_the_recent_list_and_print_nothing()
    {
        int pickers = 0;
        RecentFiles? shown = null;
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        int code = Program.Run([], stdout, stderr, recent => { pickers++; shown = recent; }, (_, _) => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException(), () => RecentFiles.Load(_store));

        Assert.Equal(0, code);
        Assert.Equal(1, pickers);
        Assert.NotNull(shown);
        Assert.Equal(_store, shown!.StorePath);
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("README.md", "extra.md")]
    [InlineData("--version", "README.md")]
    public void Usage_errors_print_usage_to_stderr_and_return_2_without_a_window(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        int code = Program.Run(args, stdout, stderr, _ => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException(), () => null);

        Assert.Equal(2, code);
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Equal(Usage, stderr.ToString().Replace("\r\n", "\n"));
    }

    [Fact]
    public void A_missing_path_runs_the_new_file_flow_with_the_path_and_the_recent_loader_and_no_window()
    {
        string missing = Path.Combine(_dir, "missing.md");
        string? created = null;
        Func<RecentFiles?>? loader = null;
        int windows = 0;

        int code = Program.Run(
            [missing],
            new StringWriter(),
            new StringWriter(),
            _ => throw new InvalidOperationException(),
            (_, _) => windows++,
            (path, recent) =>
            {
                created = path;
                loader = recent;
            },
            () => null);

        Assert.Equal(0, code);
        Assert.Equal(missing, created);
        Assert.NotNull(loader);
        Assert.Equal(0, windows);
    }

    [Fact]
    public void A_folder_path_is_not_a_new_file_and_prints_that_it_is_a_directory()
    {
        Directory.CreateDirectory(_dir);
        var stderr = new StringWriter();

        int code = Program.Run([_dir], new StringWriter(), stderr, _ => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException(), () => null);

        Assert.Equal(1, code);
        Assert.Equal($"mdedit: {Path.GetFullPath(_dir)}: is a directory\n", stderr.ToString().Replace("\r\n", "\n"));
    }

    [Fact]
    public void A_file_that_is_not_valid_utf8_prints_its_position_and_returns_1()
    {
        string path = TempFile("broken.md", [(byte)'a', (byte)'\n', (byte)'b', 0xFF, (byte)'\n']);
        var stderr = new StringWriter();

        int code = Program.Run([path], new StringWriter(), stderr, _ => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException(), () => null);

        Assert.Equal(1, code);
        Assert.Equal($"mdedit: {path}:2:1: not valid UTF-8 at byte 3\n", stderr.ToString().Replace("\r\n", "\n"));
    }

    [Fact]
    public void A_file_opens_the_window_with_the_loaded_file_and_records_it_in_the_recent_list()
    {
        string path = TempFile("notes.md", "# Notes\n");
        var opened = new List<OpenedFile>();
        RecentFiles? recorded = null;

        int code = Program.Run(
            [path],
            new StringWriter(),
            new StringWriter(),
            _ => throw new InvalidOperationException(),
            (file, recent) =>
            {
                opened.Add(file);
                recorded = recent;
            },
            (_, _) => throw new InvalidOperationException(),
            () => RecentFiles.Load(_store));

        Assert.Equal(0, code);
        OpenedFile file = Assert.Single(opened);
        Assert.Equal(path, file.Path);
        Assert.Equal("# Notes\n", file.File.Text.Text);
        Assert.NotNull(recorded);
        Assert.Equal(Path.GetFullPath(path), Assert.Single(recorded!.Files));
    }

    [Fact]
    public void A_file_with_no_recent_store_runs_the_window_with_no_list()
    {
        string path = TempFile("notes.md", "text");
        RecentFiles? recorded = RecentFiles.Load(_store);
        int windows = 0;

        int code = Program.Run([path], new StringWriter(), new StringWriter(), _ => throw new InvalidOperationException(), (_, recent) => { windows++; recorded = recent; }, (_, _) => throw new InvalidOperationException(), () => null);

        Assert.Equal(0, code);
        Assert.Equal(1, windows);
        Assert.Null(recorded);
    }

    private string TempFile(string name, string content) => TempFile(name, Encoding.UTF8.GetBytes(content));

    private string TempFile(string name, byte[] bytes)
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }

        if (File.Exists(_store))
        {
            File.Delete(_store);
        }
    }
}
