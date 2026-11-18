using Logvu.Core.Config;
using Logvu.Core.Parsing;

namespace Logvu.Core.Tests.Config;

public sealed class LogvuConfigLoadTests
{
    private const string Minimal = """
        {
          "logsDirectory": "Logs",
          "filePrefix": "SheetKraft",
          "fileNamePattern": "^SheetKraft(?:[0-9]+)?[.]log$"
        }
        """;

    [Fact]
    public void Minimal_config_gets_defaults()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Minimal);

        LogvuConfig config = LogvuConfig.Load(path);

        Assert.Equal(Path.GetFullPath(path), config.ConfigPath);
        Assert.Equal(dir.Full("Logs"), config.LogsDirectory);
        Assert.Equal("SheetKraft", config.FilePrefix);
        Assert.Equal(@"\A(?:^SheetKraft(?:[0-9]+)?[.]log$)\z", config.FileNamePattern.ToString());
        Assert.Null(config.ActiveFile);
        Assert.Null(config.Format);
        Assert.Equal(TimeZoneInfo.Local, config.TimeZone);
        Assert.Equal(20000, config.RecentCapacity);
        Assert.Empty(config.Highlights);
        Assert.Empty(config.Styles);
        Assert.Equal(LogvuConfig.DefaultFrameworkPrefixes, config.FrameworkPrefixes);
        Assert.Equal(BuiltInSchemes.All, config.Schemes);
    }

    [Fact]
    public void Default_framework_prefixes_are_the_documented_list()
    {
        Assert.Equal(
            new[] { "System.", "Microsoft.", "Newtonsoft.", "log4net.", "Serilog.", "runtime.", "net/http.", "github.com/labstack/" },
            LogvuConfig.DefaultFrameworkPrefixes);
    }

    [Fact]
    public void Design_example_resolves_logs_directory_against_the_config_directory_not_the_working_directory()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("Config/logvu.json", """
            {
              "logsDirectory": "../Logs",
              "filePrefix": "SheetKraft",
              "fileNamePattern": "^SheetKraft(?:[0-9]+)?[.]log$",
              "activeFile": "SheetKraft.log",
              "format": "log4net-core"
            }
            """);

        LogvuConfig config = LogvuConfig.Load(path);

        Assert.Equal(dir.Full("Logs"), config.LogsDirectory);
        Assert.Equal("SheetKraft.log", config.ActiveFile);
        Assert.Same(BuiltInSchemes.Log4NetCore, config.Format);
    }

    [Fact]
    public void Absolute_logs_directory_is_kept()
    {
        using var dir = new ConfigTestDirectory();
        string logs = dir.Full("Elsewhere");
        string path = dir.Write("logvu.json", $$"""
            {
              "logsDirectory": {{System.Text.Json.JsonSerializer.Serialize(logs)}},
              "filePrefix": "SheetKraft",
              "fileNamePattern": "^SheetKraft[.]log$"
            }
            """);

        Assert.Equal(Path.GetFullPath(logs), LogvuConfig.Load(path).LogsDirectory);
    }

    [Fact]
    public void Full_example_config_reads_every_setting()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", """
            {
              "logsDirectory": "Logs",
              "filePrefix": "SheetKraft",
              "fileNamePattern": "^SheetKraft(?:[0-9]+)?[.]log$",
              "activeFile": "SheetKraft.log",
              "format": "my-app",
              "timeZone": "utc",
              "recentCapacity": 500,
              "schemes": [ { "name": "my-app", "header": "(?<time>[0-9-]+ [0-9:]+) (?<level>\\w+) (?<message>.*)", "timeFormats": ["yyyy-MM-dd HH:mm:ss"] } ],
              "highlights": [ { "field": "message", "pattern": "timeout|deadlock", "style": "warning" }, { "field": "Order.Id", "pattern": "^7$", "style": "deploy" } ],
              "styles": { "deploy": { "foreground": "brightCyan", "bold": true, "underline": true }, "tint": { "background": "#102030", "italic": true } },
              "frameworkPrefixes": []
            }
            """);

        LogvuConfig config = LogvuConfig.Load(path);

        Assert.Equal("my-app", config.Format?.Name);
        Assert.IsType<TextScheme>(config.Format);
        Assert.Equal(BuiltInSchemes.All.Count + 1, config.Schemes.Count);
        Assert.Same(config.Format, config.Schemes[^1]);
        Assert.Equal(TimeZoneInfo.Utc, config.TimeZone);
        Assert.Equal(500, config.RecentCapacity);

        Assert.Equal(2, config.Highlights.Count);
        Assert.Equal("message", config.Highlights[0].Field);
        Assert.Equal("timeout|deadlock", config.Highlights[0].Pattern.ToString());
        Assert.Equal("warning", config.Highlights[0].Style);
        Assert.Equal("Order.Id", config.Highlights[1].Field);
        Assert.Equal("deploy", config.Highlights[1].Style);

        Assert.Equal(2, config.Styles.Count);
        Assert.Equal(new StyleSpec(LogColor.FromNamed(AnsiColor.BrightCyan), null, Bold: true, Italic: false, Underline: true), config.Styles["deploy"]);
        Assert.Equal(new StyleSpec(null, LogColor.FromRgb(new RgbColor(0x10, 0x20, 0x30)), Bold: false, Italic: true, Underline: false), config.Styles["tint"]);

        Assert.Empty(config.FrameworkPrefixes);
    }

    [Fact]
    public void Explicit_framework_prefixes_replace_the_defaults()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Minimal.Replace("}", ", \"frameworkPrefixes\": [\"Acme.\"] }"));

        Assert.Equal(new[] { "Acme." }, LogvuConfig.Load(path).FrameworkPrefixes);
    }

    [Fact]
    public void Format_auto_means_detection()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Minimal.Replace("}", ", \"format\": \"auto\" }"));

        Assert.Null(LogvuConfig.Load(path).Format);
    }

    [Theory]
    [InlineData("log4net-scheduler")]
    [InlineData("Serilog-JSON")]
    public void Format_names_a_built_in_scheme_ignoring_case(string name)
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Minimal.Replace("}", $", \"format\": \"{name}\" }}"));

        Assert.Same(BuiltInSchemes.Find(name), LogvuConfig.Load(path).Format);
    }

    [Fact]
    public void Configured_scheme_used_as_format_resolves_to_that_scheme()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", """
            {
              "logsDirectory": "Logs",
              "filePrefix": "App",
              "fileNamePattern": "^App[.]log$",
              "format": "my-app",
              "schemes": [ { "name": "my-app", "header": "(?<time>x) (?<message>.*)" } ]
            }
            """);

        LogvuConfig config = LogvuConfig.Load(path);

        Assert.NotNull(config.Format);
        Assert.Equal("my-app", config.Format.Name);
        Assert.Same(config.Format, config.Schemes.Single(s => s.Name == "my-app"));
    }

    [Fact]
    public void Configured_scheme_without_time_formats_is_valid()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Minimal.Replace("}", ", \"schemes\": [ { \"name\": \"plain\", \"header\": \"(?<message>.*)\" } ] }"));

        Assert.Equal("plain", LogvuConfig.Load(path).Schemes[^1].Name);
    }

    [Fact]
    public void Byte_order_mark_comments_and_trailing_commas_are_accepted()
    {
        using var dir = new ConfigTestDirectory();
        string text = """
            // A comment before the object.
            {
              /* block comment */
              "logsDirectory": "Logs",
              "filePrefix": "SheetKraft",
              "fileNamePattern": "^SheetKraft[.]log$",
              "recentCapacity": 100,
            }
            """;
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes(text)];
        string path = dir.WriteBytes("logvu.json", bytes);

        LogvuConfig config = LogvuConfig.Load(path);

        Assert.Equal("SheetKraft", config.FilePrefix);
        Assert.Equal(100, config.RecentCapacity);
    }

    [Fact]
    public void Json_null_is_an_error()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", "null");

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfig.Load(path));

        Assert.Contains($"Configuration file '{Path.GetFullPath(path)}': $: ", ex.Message);
    }

    [Fact]
    public void Missing_file_is_an_error_naming_the_file()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Full("absent.json");

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfig.Load(path));

        Assert.Contains(Path.GetFullPath(path), ex.Message);
    }

    [Fact]
    public void Malformed_json_reports_line_position_and_path()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", "{\n  \"logsDirectory\": \"Logs\",\n  \"filePrefix\": SheetKraft\n}");

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfig.Load(path));

        Assert.StartsWith($"Malformed JSON in '{Path.GetFullPath(path)}' at line 3, position ", ex.Message);
        Assert.Contains("(path: $.filePrefix)", ex.Message);
    }

    [Fact]
    public void Unknown_member_is_an_error_at_its_path()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Minimal.Replace("\"fileNamePattern\"", "\"fileNamePatern\""));

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfig.Load(path));

        Assert.StartsWith($"Malformed JSON in '{Path.GetFullPath(path)}' at line ", ex.Message);
        Assert.Contains("fileNamePatern", ex.Message);
    }

    [Fact]
    public void Wrong_type_is_an_error_at_its_path()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Minimal.Replace("}", ", \"recentCapacity\": \"x\" }"));

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfig.Load(path));

        Assert.StartsWith($"Malformed JSON in '{Path.GetFullPath(path)}' at line ", ex.Message);
        Assert.Contains("(path: $.recentCapacity)", ex.Message);
    }

    [Theory]
    [InlineData("logsDirectory")]
    [InlineData("filePrefix")]
    [InlineData("fileNamePattern")]
    public void Missing_required_field_is_an_error_at_that_field(string field)
    {
        using var dir = new ConfigTestDirectory();
        var members = new Dictionary<string, string>
        {
            ["logsDirectory"] = "\"Logs\"",
            ["filePrefix"] = "\"SheetKraft\"",
            ["fileNamePattern"] = "\"^SheetKraft[.]log$\"",
        };
        members.Remove(field);
        string json = "{ " + string.Join(", ", members.Select(m => $"\"{m.Key}\": {m.Value}")) + " }";
        string path = dir.Write("logvu.json", json);

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfig.Load(path));

        Assert.Contains($"Configuration file '{Path.GetFullPath(path)}': {field}: is required", ex.Message);
    }

    [Fact]
    public void Second_byte_order_mark_is_malformed_json()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.WriteBytes("logvu.json", [0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF]);

        Assert.Throws<LogvuConfigException>(() => LogvuConfig.Load(path));
    }
}
