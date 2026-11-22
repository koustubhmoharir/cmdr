using Cfgedit;
using Cfgedit.Core;
using Cfgedit.Core.Model;
using Cfgedit.Tui;
using Shared.Core;

namespace Cfgedit.Tests;

/// <summary>
/// The command line. The host's actions are replaced by stubs, so no terminal is started: the picker and the window are recorded, and
/// the recent list is a temporary store.
/// </summary>
public sealed class ProgramTests : IDisposable
{
    private readonly string _store = Path.Combine(Path.GetTempPath(), $"cfgedit-store-{Guid.NewGuid():N}.json");
    private readonly List<string> _files = [];

    [Fact]
    public void Version_prints_the_informational_version()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        int code = Program.Run(["--version"], stdout, stderr);

        Assert.Equal(0, code);
        Assert.StartsWith("cfgedit ", stdout.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public void No_arguments_show_the_picker_with_the_recent_list_and_print_nothing()
    {
        int pickers = 0;
        RecentFiles? shown = null;
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        int code = Program.Run([], stdout, stderr, recent => { pickers++; shown = recent; }, (_, _) => throw new InvalidOperationException(), () => RecentFiles.Load(_store));

        Assert.Equal(0, code);
        Assert.Equal(1, pickers);
        Assert.NotNull(shown);
        Assert.Equal(_store, shown!.StorePath);
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public void Unknown_option_prints_usage_to_stderr_and_returns_2()
    {
        Outcome outcome = Run("--bogus");

        Assert.Equal(2, outcome.Code);
        Assert.Equal(string.Empty, outcome.Stdout);
        Assert.StartsWith("usage: cfgedit <file>", outcome.Stderr);
        Assert.Equal(0, outcome.Pickers);
        Assert.Empty(outcome.Ran);
    }

    [Fact]
    public void Empty_string_argument_prints_usage_to_stderr_and_returns_2()
    {
        Outcome outcome = Run("");

        Assert.Equal(2, outcome.Code);
        Assert.Equal(string.Empty, outcome.Stdout);
        Assert.StartsWith("usage: cfgedit <file>", outcome.Stderr);
    }

    [Fact]
    public void The_file_and_the_format_option_are_accepted_in_either_order()
    {
        string path = TempFile("settings.txt", "{\"a\": 1}");

        Outcome before = Run("--format", "json5", path);
        Outcome after = Run(path, "--format", "json5");

        Assert.Equal(0, before.Code);
        Assert.Equal(0, after.Code);
        Assert.Equal("json5", Assert.Single(before.Ran).File.Format.Name);
        Assert.Equal("json5", Assert.Single(after.Ran).File.Format.Name);
        Assert.Equal(path, Assert.Single(after.Ran).Path);
    }

    [Fact]
    public void A_valid_format_option_overrides_the_extension()
    {
        string path = TempFile("data.json", "<root/>");

        Outcome outcome = Run(path, "--format", "XML");

        Assert.Equal(0, outcome.Code);
        Assert.Equal("xml", Assert.Single(outcome.Ran).File.Format.Name);
    }

    [Fact]
    public void An_unknown_format_option_is_a_usage_error_and_opens_nothing()
    {
        string path = TempFile("data.json", "{}");

        Outcome outcome = Run(path, "--format", "yaml");

        Assert.Equal(2, outcome.Code);
        Assert.Equal("cfgedit: unknown format 'yaml'; use xml, json, jsonc, json5 or env" + Environment.NewLine, outcome.Stderr);
        Assert.Empty(outcome.Ran);
    }

    [Fact]
    public void The_env_format_option_opens_a_file_as_env_whatever_its_name()
    {
        string path = TempFile("settings.txt", "A=1\n");

        Outcome outcome = Run(path, "--format", "env");

        Assert.Equal(0, outcome.Code);
        Assert.Equal("env", Assert.Single(outcome.Ran).File.Format.Name);
    }

    [Fact]
    public void The_usage_line_lists_env_as_a_format()
    {
        Outcome outcome = Run("--bogus");

        Assert.Equal(2, outcome.Code);
        Assert.StartsWith("usage: cfgedit <file> [--format xml|json|jsonc|json5|env]" + Environment.NewLine, outcome.Stderr);
    }

    [Fact]
    public void A_format_option_without_a_value_is_a_usage_error()
    {
        string path = TempFile("data.json", "{}");

        Outcome outcome = Run(path, "--format");

        Assert.Equal(2, outcome.Code);
        Assert.StartsWith("usage: cfgedit <file>", outcome.Stderr);
        Assert.Empty(outcome.Ran);
    }

    [Fact]
    public void Two_files_are_a_usage_error()
    {
        string first = TempFile("a.json", "{}");
        string second = TempFile("b.json", "{}");

        Outcome outcome = Run(first, second);

        Assert.Equal(2, outcome.Code);
        Assert.StartsWith("usage: cfgedit <file>", outcome.Stderr);
        Assert.Empty(outcome.Ran);
    }

    [Theory]
    [InlineData("<settings><add/></settings>", "xml")]
    [InlineData("  \n\t<settings/>", "xml")]
    [InlineData("{\"a\": 1}", "json5")]
    [InlineData("  [1, 2]", "json5")]
    public void A_file_with_an_unknown_extension_is_sniffed_by_its_first_character(string content, string expected)
    {
        string path = TempFile("settings.dat", content);

        Outcome outcome = Run(path);

        Assert.Equal(0, outcome.Code);
        Assert.Equal(expected, Assert.Single(outcome.Ran).File.Format.Name);
    }

    [Fact]
    public void A_file_whose_format_cannot_be_told_is_reported_and_not_opened()
    {
        string path = TempFile("notes.dat", "hello");

        Outcome outcome = Run(path);

        Assert.Equal(1, outcome.Code);
        Assert.Equal($"cfgedit: cannot tell the format of {path}; use --format" + Environment.NewLine, outcome.Stderr);
        Assert.Equal(string.Empty, outcome.Stdout);
        Assert.Empty(outcome.Ran);
    }

    [Fact]
    public void A_missing_file_starts_the_new_file_flow_with_the_format_named_and_returns_0()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cfgedit-missing-{Guid.NewGuid():N}.json");

        Outcome outcome = Run(path, "--format", "json5");

        Assert.Equal(0, outcome.Code);
        Assert.Equal(string.Empty, outcome.Stderr);
        Assert.Equal(new[] { $"{path}|json5" }, outcome.Created);
        Assert.Empty(outcome.Ran);
    }

    [Fact]
    public void A_missing_file_without_a_format_option_passes_no_format_to_the_new_file_flow()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cfgedit-missing-{Guid.NewGuid():N}.json");

        Outcome outcome = Run(path);

        Assert.Equal(0, outcome.Code);
        Assert.Equal(new[] { $"{path}|" }, outcome.Created);
    }

    [Fact]
    public void A_new_file_flow_that_fails_is_reported_on_stderr_and_returns_1()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cfgedit-missing-{Guid.NewGuid():N}.json");

        Outcome outcome = RunWith(new CfgeditException("the new file cannot be made") { Path = path }, path);

        Assert.Equal(1, outcome.Code);
        Assert.Equal($"cfgedit: {path}: the new file cannot be made" + Environment.NewLine, outcome.Stderr);
    }

    [Fact]
    public void A_file_that_is_a_directory_is_reported_on_stderr_and_returns_1()
    {
        string path = Path.GetTempPath();

        Outcome outcome = Run(path);

        Assert.Equal(1, outcome.Code);
        Assert.Equal($"cfgedit: {path}: is a directory" + Environment.NewLine, outcome.Stderr);
        Assert.Empty(outcome.Ran);
    }

    [Fact]
    public void A_file_that_does_not_parse_opens_the_window_in_text_mode_and_returns_0()
    {
        string path = TempFile("broken.json", "{\n  \"a\": ,\n}");

        Outcome outcome = Run(path, "--format", "json");

        Assert.Equal(0, outcome.Code);
        Assert.Equal(string.Empty, outcome.Stderr);
        OpenedFile opened = Assert.Single(outcome.Ran);
        Assert.Null(opened.Document);
        Assert.Equal($"{path}:2:8: expected a value but found ','", CfgeditHost.Describe(opened.File.ParseError!));
    }

    [Fact]
    public void Check_of_a_file_that_parses_prints_nothing_and_returns_0_without_the_window()
    {
        string path = TempFile("good.json", "{\"a\": 1}");

        Outcome outcome = Run("--check", path);

        Assert.Equal(0, outcome.Code);
        Assert.Equal(string.Empty, outcome.Stdout);
        Assert.Equal(string.Empty, outcome.Stderr);
        Assert.Empty(outcome.Ran);
        Assert.Equal(0, outcome.Pickers);
    }

    [Fact]
    public void Check_of_a_parse_error_prints_path_line_and_column_and_returns_1()
    {
        string path = TempFile("broken.json", "{\n  \"a\": ,\n}");

        Outcome outcome = Run(path, "--check", "--format", "json");

        Assert.Equal(1, outcome.Code);
        Assert.Equal(string.Empty, outcome.Stdout);
        Assert.Equal($"{path}:2:8: expected a value but found ','" + Environment.NewLine, outcome.Stderr);
        Assert.Empty(outcome.Ran);
        Assert.Empty(outcome.Created);
    }

    [Fact]
    public void Check_of_a_missing_file_prints_the_path_and_returns_1_without_the_new_file_flow()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cfgedit-missing-{Guid.NewGuid():N}.json");

        Outcome outcome = Run("--check", path);

        Assert.Equal(1, outcome.Code);
        Assert.Equal($"{path}: the file does not exist" + Environment.NewLine, outcome.Stderr);
        Assert.Empty(outcome.Created);
    }

    [Fact]
    public void Check_with_an_unknown_format_is_a_usage_error_returning_2()
    {
        string path = TempFile("good.json", "{}");

        Outcome outcome = Run("--check", path, "--format", "yaml");

        Assert.Equal(2, outcome.Code);
        Assert.Equal("cfgedit: unknown format 'yaml'; use xml, json, jsonc, json5 or env" + Environment.NewLine, outcome.Stderr);
    }

    [Fact]
    public void Check_without_a_file_or_with_two_checks_is_a_usage_error_returning_2()
    {
        string path = TempFile("good.json", "{}");

        Assert.Equal(2, Run("--check").Code);
        Assert.Equal(2, Run("--check", "--check", path).Code);
    }

    [Fact]
    public void A_file_that_opens_is_recorded_at_the_front_of_the_recent_list()
    {
        string first = TempFile("first.json", "{}");
        string second = TempFile("second.json", "{}");

        Run(first);
        Run(second);
        Run(first);

        Assert.Equal(new[] { Path.GetFullPath(first), Path.GetFullPath(second) }, RecentFiles.Load(_store).Files);
    }

    [Fact]
    public void A_file_that_does_not_open_is_not_recorded()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cfgedit-missing-{Guid.NewGuid():N}.json");

        Run(path);

        Assert.Empty(RecentFiles.Load(_store).Files);
    }

    // What the command line passed to the new-file flow, as "path|format name" (the format name is empty when none was given).
    private sealed record Outcome(int Code, string Stdout, string Stderr, List<OpenedFile> Ran, int Pickers, List<string> Created);

    // Runs the command line with the picker, the window and the new-file flow replaced: the window's files and the new-file calls are
    // collected, and the recent list is the temp store.
    private Outcome Run(params string[] args) => RunWith(null, args);

    // As Run, and when failNew is given, the new-file flow throws it.
    private Outcome RunWith(CfgeditException? failNew, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var ran = new List<OpenedFile>();
        var created = new List<string>();
        int pickers = 0;
        int code = Program.Run(
            args,
            stdout,
            stderr,
            _ => pickers++,
            (file, _) => ran.Add(file),
            () => RecentFiles.Load(_store),
            (path, format, _) =>
            {
                created.Add($"{path}|{format?.Name ?? string.Empty}");
                if (failNew is not null)
                {
                    throw failNew;
                }
            });
        return new Outcome(code, stdout.ToString(), stderr.ToString(), ran, pickers, created);
    }

    private string TempFile(string name, string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"cfgedit-{Guid.NewGuid():N}-{name}");
        File.WriteAllText(path, content);
        _files.Add(path);
        return path;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (string path in _files.Append(_store))
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
