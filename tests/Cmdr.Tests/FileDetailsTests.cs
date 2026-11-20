using Cmdr.Core.Session;
using Cmdr.Tui;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Tests;

/// <summary>
/// The info pane of a file row: the file's path, base, bufferLines, logsDir, read-only state, baseEnv, envScript and secret as written,
/// then the environment its own level adds. Covers the opened file and a base file that the opened file imports.
/// </summary>
public sealed class FileDetailsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("cmdr-file-details-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: the temporary folder is left behind when it cannot be removed.
        }
    }

    /// <summary>
    /// The base file (bufferLines 7, logsDir "base-logs", env BASE_VAR, secret TOKEN) and the opened file that imports it (bufferLines 50,
    /// env MAIN_VAR, baseEnv BASE_VAR, envScript setup.sh with the arguments --mode x, secret API_KEY).
    /// </summary>
    private CmdrSession OpenChain()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "shared"));
        File.WriteAllText(
            Path.Combine(_dir, "shared", "base.json"),
            """
            { "bufferLines": 7, "logsDir": "base-logs", "env": { "BASE_VAR": "from-base" }, "secret": [ "TOKEN" ],
              "groups": [ { "name": "shared", "items": [ { "name": "tool", "program": "tool" } ] } ] }
            """);
        File.WriteAllText(
            Path.Combine(_dir, "cmdr.json"),
            """
            { "base": "shared/base.json", "bufferLines": 50, "env": { "MAIN_VAR": "main" }, "baseEnv": [ "BASE_VAR" ],
              "envScript": { "program": "setup.sh", "args": [ "--mode", "x" ] }, "secret": [ "API_KEY" ],
              "groups": [ { "name": "main", "items": [ { "name": "mine", "program": "dotnet" } ] } ] }
            """);
        return CmdrSession.Load(Path.Combine(_dir, "cmdr.json"), SessionMode.Run);
    }

    /// <summary>The plain text of the lines of a file row, as the info pane shows them.</summary>
    private static IReadOnlyList<string> Lines(CmdrSession session, ConfigSource file) =>
        DetailsText.FileLines(file, new Dictionary<string, string?>(), windows: false, pathSeparator: ':')
            .Select(line => line.Text)
            .ToList();

    [Fact]
    public void The_opened_file_shows_its_base_settings_and_its_own_environment()
    {
        using CmdrSession session = OpenChain();
        ConfigSource main = session.MainSource;

        Assert.Equal(
            new[]
            {
                "file: " + main.Path,
                "base: shared/base.json",
                "bufferLines: 50",
                "logsDir: " + main.LogsDir,
                "baseEnv: BASE_VAR",
                "envScript: setup.sh --mode x",
                "secret: API_KEY",
                "BASE_VAR=from-base",
                "MAIN_VAR=main",
            },
            Lines(session, main));
    }

    [Fact]
    public void A_base_file_is_marked_read_only_and_shows_no_base_when_it_imports_none()
    {
        using CmdrSession session = OpenChain();
        ConfigSource baseFile = session.Sources[0];

        Assert.Equal(
            new[]
            {
                "file: " + baseFile.Path,
                "bufferLines: 7",
                "logsDir: " + baseFile.LogsDir,
                "read-only (base file)",
                "secret: TOKEN",
                "BASE_VAR=from-base",
            },
            Lines(session, baseFile));
    }

    [Fact]
    public void The_title_and_the_directory_of_a_file_row_are_its_display_path_and_its_own_directory()
    {
        using CmdrSession session = OpenChain();
        ConfigSource baseFile = session.Sources[0];

        Assert.Equal("cmdr.json", DetailsText.Title(new FileRow(session.MainSource)));
        Assert.Equal(Path.Combine("shared", "base.json"), DetailsText.Title(new FileRow(baseFile)));
        Assert.Equal(baseFile.Dir, DetailsText.ConfigDirFor(new FileRow(baseFile), "fallback"));
    }
}
