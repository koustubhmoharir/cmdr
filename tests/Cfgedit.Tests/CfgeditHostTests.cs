using Cfgedit.Core;
using Cfgedit.Core.Model;
using Cfgedit.Tui;

namespace Cfgedit.Tests;

/// <summary>
/// The host without a terminal: the loader the picker uses (the file, or the message to show when it does not load), the load errors as
/// the command line prints them, and the loop that runs documents in the order they were chosen.
/// </summary>
public sealed class CfgeditHostTests : IDisposable
{
    private readonly List<string> _files = [];

    [Fact]
    public void Try_load_gives_the_file_and_its_document_when_the_format_comes_from_the_extension()
    {
        string path = TempFile("settings.json", "{\"a\": 1}");

        (OpenedFile? value, string? error) = CfgeditHost.TryLoad(path);

        Assert.Null(error);
        Assert.NotNull(value);
        Assert.Equal(path, value!.Path);
        Assert.Equal("jsonc", value.Document.Format.Name);
    }

    [Fact]
    public void Try_load_sniffs_a_file_with_an_unknown_extension()
    {
        string path = TempFile("settings.dat", "  <settings/>");

        (OpenedFile? value, string? error) = CfgeditHost.TryLoad(path);

        Assert.Null(error);
        Assert.Equal("xml", value!.Document.Format.Name);
    }

    [Fact]
    public void Try_load_reports_a_file_whose_format_cannot_be_told_and_gives_no_file()
    {
        string path = TempFile("notes.dat", "plain words");

        (OpenedFile? value, string? error) = CfgeditHost.TryLoad(path);

        Assert.Null(value);
        Assert.Equal($"cannot tell the format of {path}; use --format", error);
    }

    [Fact]
    public void Try_load_reports_a_missing_file_with_its_path()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cfgedit-missing-{Guid.NewGuid():N}.xml");

        (OpenedFile? value, string? error) = CfgeditHost.TryLoad(path);

        Assert.Null(value);
        Assert.Equal($"{path}: the file does not exist", error);
    }

    [Fact]
    public void Try_load_reports_a_parse_error_with_its_position()
    {
        string path = TempFile("broken.json", "{\n  \"a\": ,\n}");

        (OpenedFile? value, string? error) = CfgeditHost.TryLoad(path);

        Assert.Null(value);
        Assert.Equal($"{path}:2:8: expected a value but found ','", error);
    }

    [Fact]
    public void Load_with_a_format_uses_it_over_the_extension()
    {
        string path = TempFile("data.json", "<root/>");

        OpenedFile opened = CfgeditHost.Load(path, Formats.FromName("xml"));

        Assert.Equal("xml", opened.Document.Format.Name);
    }

    [Fact]
    public void Load_of_a_bad_file_throws_with_the_path_on_the_error()
    {
        string path = TempFile("data.xml", "<a><b></a>");

        CfgeditException ex = Assert.Throws<CfgeditException>(() => CfgeditHost.Load(path, null));

        Assert.Equal(path, ex.Path);
        Assert.NotNull(ex.Line);
    }

    [Theory]
    [InlineData("msg", null, null, "msg")]
    [InlineData("expected a value", "f.json", null, "f.json: expected a value")]
    [InlineData("expected a value", "f.json", 2, "f.json:2: expected a value")]
    public void Describe_gives_the_message_with_the_path_and_position_when_there_are_some(string message, string? path, int? line, string expected)
    {
        var ex = new CfgeditException(message) { Path = path, Line = line };

        Assert.Equal(expected, CfgeditHost.Describe(ex));
    }

    [Fact]
    public void Describe_gives_the_path_line_and_column_for_a_position_with_a_column()
    {
        var ex = new CfgeditException("bad") { Path = "f.json", Line = 3, Column = 7 };

        Assert.Equal("f.json:3:7: bad", CfgeditHost.Describe(ex));
    }

    [Fact]
    public void Documents_run_in_the_order_they_were_chosen_and_the_loop_ends_with_the_first_run_that_chooses_nothing()
    {
        var first = new OpenedFile("a.json", TreeFixtures.Parse("{}", "json"));
        var second = new OpenedFile("b.json", TreeFixtures.Parse("{}", "json"));
        var third = new OpenedFile("c.json", TreeFixtures.Parse("{}", "json"));
        var ran = new List<OpenedFile>();

        CfgeditHost.RunDocuments(first, file =>
        {
            ran.Add(file);
            return file == first ? second : file == second ? third : null;
        });

        Assert.Equal([first, second, third], ran);
    }

    [Fact]
    public void Choosing_the_document_that_is_open_runs_it_again()
    {
        var only = new OpenedFile("a.json", TreeFixtures.Parse("{}", "json"));
        var ran = new List<OpenedFile>();

        CfgeditHost.RunDocuments(only, file =>
        {
            ran.Add(file);
            return ran.Count == 1 ? only : null;
        });

        Assert.Equal([only, only], ran);
    }

    private string TempFile(string name, string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"cfgedit-host-{Guid.NewGuid():N}-{name}");
        File.WriteAllText(path, content);
        _files.Add(path);
        return path;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (string path in _files)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
