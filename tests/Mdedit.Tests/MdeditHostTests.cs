using Mdedit.Tui;

namespace Mdedit.Tests;

/// <summary>
/// The host without a terminal: the loader the picker uses (the file, or the message to show when it does not load), the load errors as
/// the command line prints them, and the loop that runs documents in the order they were chosen.
/// </summary>
public sealed class MdeditHostTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"mdedit-host-{Guid.NewGuid():N}");

    [Fact]
    public void Try_load_gives_the_file_with_its_text()
    {
        string path = TempFile("notes.md", "# Notes\n");

        (OpenedFile? value, string? error) = MdeditHost.TryLoad(path);

        Assert.Null(error);
        Assert.NotNull(value);
        Assert.Equal(path, value!.Path);
        Assert.Equal("# Notes\n", value.File.Text.Text);
    }

    [Fact]
    public void Try_load_of_a_missing_file_gives_the_message_with_its_full_path()
    {
        string path = Path.Combine(_dir, "missing.md");

        (OpenedFile? value, string? error) = MdeditHost.TryLoad(path);

        Assert.Null(value);
        Assert.Equal($"{Path.GetFullPath(path)}: the file does not exist", error);
    }

    [Fact]
    public void Try_load_of_a_folder_gives_the_message_with_its_full_path()
    {
        Directory.CreateDirectory(_dir);

        (OpenedFile? value, string? error) = MdeditHost.TryLoad(_dir);

        Assert.Null(value);
        Assert.Equal($"{Path.GetFullPath(_dir)}: is a directory", error);
    }

    [Fact]
    public void Try_load_of_a_file_that_is_not_valid_utf8_gives_the_position_of_the_last_good_character()
    {
        string path = TempFile("broken.md", [(byte)'a', (byte)'\n', (byte)'b', 0xFF]);

        (OpenedFile? value, string? error) = MdeditHost.TryLoad(path);

        Assert.Null(value);
        Assert.Equal($"{Path.GetFullPath(path)}:2:1: not valid UTF-8 at byte 3", error);
    }

    [Theory]
    [InlineData("msg", null, null, "msg")]
    [InlineData("the file does not exist", "f.md", null, "f.md: the file does not exist")]
    [InlineData("not valid UTF-8", "f.md", 2, "f.md:2: not valid UTF-8")]
    public void Describe_gives_the_message_with_the_path_and_position_when_there_are_some(string message, string? path, int? line, string expected)
    {
        var ex = new Shared.Core.TextFileException(message) { Path = path, Line = line };

        Assert.Equal(expected, MdeditHost.Describe(ex));
    }

    [Fact]
    public void Describe_gives_the_path_line_and_column_for_a_position_with_a_column()
    {
        var ex = new Shared.Core.TextFileException("bad") { Path = "f.md", Line = 3, Column = 7 };

        Assert.Equal("f.md:3:7: bad", MdeditHost.Describe(ex));
    }

    [Fact]
    public void Documents_run_in_the_order_they_were_chosen_and_the_loop_ends_with_the_first_run_that_chooses_nothing()
    {
        var first = Opened("a.md");
        var second = Opened("b.md");
        var third = Opened("c.md");
        var ran = new List<string>();

        MdeditHost.RunDocuments(first, file =>
        {
            ran.Add(file.Path);
            return file == first ? second : file == second ? third : null;
        });

        Assert.Equal([first.Path, second.Path, third.Path], ran);
    }

    private OpenedFile Opened(string name)
    {
        string path = TempFile(name, "text");
        return MdeditHost.Load(path);
    }

    private string TempFile(string name, string content) => TempFile(name, System.Text.Encoding.UTF8.GetBytes(content));

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
    }
}
