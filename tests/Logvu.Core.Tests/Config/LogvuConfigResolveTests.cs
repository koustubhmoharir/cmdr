using Logvu.Core.Config;

namespace Logvu.Core.Tests.Config;

/// <summary>Resolution of <c>logsDirectory</c> placeholders in a configuration without an <c>envScript</c>.</summary>
public sealed class LogvuConfigResolveTests
{
    /// <summary>A configuration whose <c>logsDirectory</c> is the given JSON text.</summary>
    private static string Doc(string logsDirectory) => $$"""
        {
          "logsDirectory": {{logsDirectory}},
          "filePrefix": "SheetKraft",
          "fileNamePattern": "^SheetKraft(?:[0-9]+)?[.]log$"
        }
        """;

    [Fact]
    public async Task Environment_variable_of_logvu_is_expanded()
    {
        using var dir = new ConfigTestDirectory();
        string name = "LOGVU_TEST_ROOT_" + Guid.NewGuid().ToString("N");
        string root = dir.Full("env-root");
        string path = dir.Write("logvu.json", Doc($"\"${{env:{name}}}/Logs\""));

        string? previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, root);

            LogvuConfig config = LogvuConfig.Load(path);

            Assert.Equal(Path.GetFullPath(Path.Combine(root, "Logs")), config.LogsDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }

    [Fact]
    public async Task Unset_variable_is_reported_by_name()
    {
        using var dir = new ConfigTestDirectory();
        string name = "LOGVU_TEST_UNSET_" + Guid.NewGuid().ToString("N");
        string path = dir.Write("logvu.json", Doc($"\"${{env:{name}}}/bin/Logs\""));

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfig.Load(path));

        Assert.Equal($"Configuration file '{Path.GetFullPath(path)}': logsDirectory: variable '{name}' is not set", ex.Message);
    }

    [Fact]
    public async Task Empty_variable_is_allowed()
    {
        using var dir = new ConfigTestDirectory();
        string name = "LOGVU_TEST_EMPTY_" + Guid.NewGuid().ToString("N");
        string path = dir.Write("logvu.json", Doc($"\"${{env:{name}}}Logs\""));

        string? previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, "");

            Assert.Equal(dir.Full("Logs"), LogvuConfig.Load(path).LogsDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }

    [Fact]
    public async Task Configuration_directory_placeholder_is_the_config_folder()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("Config/logvu.json", Doc("\"${configDir}/../Logs\""));

        Assert.Equal(dir.Full("Logs"), LogvuConfig.Load(path).LogsDirectory);
    }

    [Fact]
    public async Task Cwd_placeholder_is_the_config_folder()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("Config/logvu.json", Doc("\"${cwd}/Logs\""));

        Assert.Equal(dir.Full("Config/Logs"), LogvuConfig.Load(path).LogsDirectory);
    }

    [Fact]
    public async Task Escaped_placeholder_is_a_literal_directory_name()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Doc("\"$${env:HOME}\""));

        Assert.Equal(dir.Full("${env:HOME}"), LogvuConfig.Load(path).LogsDirectory);
    }

    [Fact]
    public async Task Plain_relative_directory_is_unchanged()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("Config/logvu.json", Doc("\"../Logs/Sub\""));

        Assert.Equal(dir.Full("Logs/Sub"), LogvuConfig.Load(path).LogsDirectory);
    }

    [Fact]
    public async Task Resolve_without_a_script_completes_with_the_same_result()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("Config/logvu.json", Doc("\"../Logs\""));

        LogvuConfig config = await LogvuConfigFile.Read(path).ResolveAsync(CancellationToken.None);

        Assert.Equal(dir.Full("Logs"), config.LogsDirectory);
        Assert.Equal(Path.GetFullPath(path), config.ConfigPath);
    }
}
