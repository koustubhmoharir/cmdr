using Logvu.Core.Config;
using Logvu.Core.Parsing;

namespace Logvu.Core.Tests.Config;

public sealed class LogvuConfigMinLevelTests
{
    private const string Minimal = """
        {
          "logsDirectory": "Logs",
          "filePrefix": "SheetKraft",
          "fileNamePattern": "^SheetKraft(?:[0-9]+)?[.]log$"
        }
        """;

    [Fact]
    public void Absent_min_level_is_info()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Minimal);

        Assert.Equal(LogLevel.Info, LogvuConfig.Load(path).MinLevel);
        Assert.Equal(LogLevel.Info, LogvuConfig.DefaultMinLevel);
    }

    [Fact]
    public void Json_null_min_level_is_info()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Minimal.Replace("}", ", \"minLevel\": null }"));

        Assert.Equal(LogLevel.Info, LogvuConfig.Load(path).MinLevel);
    }

    [Theory]
    [InlineData("any", null)]
    [InlineData("ANY", null)]
    [InlineData("trace", LogLevel.Trace)]
    [InlineData("debug", LogLevel.Debug)]
    [InlineData("Debug", LogLevel.Debug)]
    [InlineData("info", LogLevel.Info)]
    [InlineData("INFO", LogLevel.Info)]
    [InlineData("warn", LogLevel.Warn)]
    [InlineData("warning", LogLevel.Warn)]
    [InlineData("Warning", LogLevel.Warn)]
    [InlineData("error", LogLevel.Error)]
    [InlineData("fatal", LogLevel.Fatal)]
    public void Each_accepted_value_is_read_ignoring_case(string text, LogLevel? expected)
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Minimal.Replace("}", $", \"minLevel\": \"{text}\" }}"));

        Assert.Equal(expected, LogvuConfig.Load(path).MinLevel);
    }

    [Theory]
    [InlineData("verbose")]
    [InlineData("information")]
    [InlineData("")]
    [InlineData("none")]
    public void Unknown_value_is_an_error_naming_the_setting_and_the_accepted_values(string text)
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Minimal.Replace("}", $", \"minLevel\": \"{text}\" }}"));

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfig.Load(path));

        Assert.StartsWith($"Configuration file '{Path.GetFullPath(path)}': minLevel: ", ex.Message);
        Assert.Contains($"unknown level '{text}'", ex.Message);
        Assert.Contains("any, trace, debug, info, warn (or warning), error, fatal", ex.Message);
    }

    [Fact]
    public void Wrong_type_min_level_is_an_error_at_its_path()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Minimal.Replace("}", ", \"minLevel\": 3 }"));

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfig.Load(path));

        Assert.Contains("(path: $.minLevel)", ex.Message);
    }
}
