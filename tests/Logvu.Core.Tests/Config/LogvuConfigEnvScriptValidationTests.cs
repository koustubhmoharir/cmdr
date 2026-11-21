using Logvu.Core.Config;

namespace Logvu.Core.Tests.Config;

/// <summary>Validation of <c>envScript</c> and of the placeholders in <c>logsDirectory</c>. Nothing here runs a process.</summary>
public sealed class LogvuConfigEnvScriptValidationTests
{
    private const string Required = """
          "filePrefix": "SheetKraft",
          "fileNamePattern": "^SheetKraft(?:[0-9]+)?[.]log$"
        """;

    /// <summary>A configuration with the required members, an optional <c>envScript</c> object and a <c>logsDirectory</c> as JSON text.</summary>
    private static string Doc(string logsDirectory = "\"Logs\"", string? envScript = null) =>
        "{\n"
        + (envScript is null ? "" : "  \"envScript\": " + envScript + ",\n")
        + "  \"logsDirectory\": " + logsDirectory + ",\n"
        + Required
        + "\n}";

    [Theory]
    [MemberData(nameof(InvalidEnvScripts))]
    public void Invalid_envScript_is_reported_with_the_file_and_the_json_path(string envScript, string jsonPath, string problem)
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Doc(envScript: envScript));

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfigFile.Read(path));

        Assert.StartsWith($"Configuration file '{Path.GetFullPath(path)}': {jsonPath}: ", ex.Message);
        Assert.Contains(problem, ex.Message);
    }

    public static TheoryData<string, string, string> InvalidEnvScripts()
    {
        return new TheoryData<string, string, string>
        {
            { """{ "args": ["x"] }""", "envScript.program", "is required" },
            { """{ "program": "" }""", "envScript.program", "must not be empty" },
            { """{ "program": "   " }""", "envScript.program", "must not be empty" },
            { """{ "program": "sh\u0000" }""", "envScript.program", "must not contain a NUL character" },
            { """{ "program": "sh", "args": [ "a", null ] }""", "envScript.args[1]", "must be a string, not null" },
            { """{ "program": "sh", "args": [ "a\u0000b" ] }""", "envScript.args[0]", "must not contain a NUL character" },
            { """{ "program": "sh", "args": [ "${input:x}" ] }""", "envScript.args[0]", "input placeholders are not supported here: '${input:x}'" },
            { """{ "program": "sh", "args": [ "${input:x?}" ] }""", "envScript.args[0]", "input placeholders are not supported here: '${input:x}'" },
            { """{ "program": "sh", "args": [ "${env:}" ] }""", "envScript.args[0]", "malformed placeholder '${env:}'" },
            { """{ "program": "sh", "args": [ "${PATH}" ] }""", "envScript.args[0]", "unknown placeholder '${PATH}'" },
            { """{ "program": "sh", "cwd": "a\u0000b" }""", "envScript.cwd", "must not contain a NUL character" },
            { """{ "program": "sh", "timeoutSeconds": 0 }""", "envScript.timeoutSeconds", "must be greater than 0, got 0" },
            { """{ "program": "sh", "timeoutSeconds": -5 }""", "envScript.timeoutSeconds", "must be greater than 0, got -5" },
        };
    }

    [Fact]
    public void Unknown_member_of_envScript_is_rejected_as_malformed()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Doc(envScript: """{ "program": "sh", "rerun": "eachStart" }"""));

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfigFile.Read(path));

        Assert.Contains("Malformed JSON", ex.Message);
        Assert.Contains("rerun", ex.Message);
    }

    [Theory]
    [InlineData("\"${input:x}\"")]
    [InlineData("\"${input:x?}\"")]
    public void Input_placeholder_in_logsDirectory_is_rejected(string logsDirectory)
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Doc(logsDirectory: logsDirectory));

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfigFile.Read(path));

        Assert.StartsWith($"Configuration file '{Path.GetFullPath(path)}': logsDirectory: ", ex.Message);
        Assert.Contains("input placeholders are not supported here: '${input:x}'", ex.Message);
    }

    [Theory]
    [InlineData("\"${env:}/Logs\"", "malformed placeholder '${env:}'")]
    [InlineData("\"${configDir/Logs\"", "unterminated placeholder")]
    [InlineData("\"${PATH}/Logs\"", "unknown placeholder '${PATH}'")]
    public void Malformed_placeholder_in_logsDirectory_is_rejected(string logsDirectory, string problem)
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Doc(logsDirectory: logsDirectory));

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfigFile.Read(path));

        Assert.StartsWith($"Configuration file '{Path.GetFullPath(path)}': logsDirectory: ", ex.Message);
        Assert.Contains(problem, ex.Message);
    }

    [Fact]
    public void Invalid_envScript_is_reported_before_an_invalid_logsDirectory()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Doc(
            logsDirectory: "\"${input:x}\"",
            envScript: """{ "program": "" }"""));

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfigFile.Read(path));

        Assert.StartsWith($"Configuration file '{Path.GetFullPath(path)}': envScript.program: ", ex.Message);
    }

    [Fact]
    public void Read_runs_nothing_and_reports_a_script()
    {
        using var dir = new ConfigTestDirectory();
        string marker = dir.Full("ran.txt");
        string path = dir.Write("logvu.json", Doc(envScript: $$"""
            { "program": "sh", "args": ["-c", "echo ran > '{{marker}}'; echo '{}'"] }
            """));

        LogvuConfigFile file = LogvuConfigFile.Read(path);

        Assert.True(file.HasEnvScript);
        Assert.Equal(Path.GetFullPath(path), file.ConfigPath);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void HasEnvScript_is_false_without_the_setting_or_with_null()
    {
        using var dir = new ConfigTestDirectory();

        Assert.False(LogvuConfigFile.Read(dir.Write("plain.json", Doc())).HasEnvScript);
        Assert.False(LogvuConfigFile.Read(dir.Write("null.json", Doc(envScript: "null"))).HasEnvScript);
    }

    [Fact]
    public void Minimal_envScript_is_valid_with_defaults()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Doc(envScript: """{ "program": "sh" }"""));

        Assert.True(LogvuConfigFile.Read(path).HasEnvScript);
    }
}
