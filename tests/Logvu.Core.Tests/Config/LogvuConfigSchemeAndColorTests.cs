using Logvu.Core.Config;
using Logvu.Core.Parsing;

namespace Logvu.Core.Tests.Config;

public sealed class LogColorTests
{
    [Theory]
    [InlineData("black", AnsiColor.Black)]
    [InlineData("white", AnsiColor.White)]
    [InlineData("brightCyan", AnsiColor.BrightCyan)]
    [InlineData("BRIGHTCYAN", AnsiColor.BrightCyan)]
    [InlineData("brightwhite", AnsiColor.BrightWhite)]
    public void Named_colors_parse_ignoring_case(string text, AnsiColor expected)
    {
        Assert.True(LogColor.TryParse(text, out LogColor color));

        Assert.Equal(expected, color.Named);
        Assert.Null(color.Rgb);
    }

    [Theory]
    [InlineData("#102030", 0x10, 0x20, 0x30)]
    [InlineData("#AbCdEf", 0xAB, 0xCD, 0xEF)]
    [InlineData("#000000", 0, 0, 0)]
    public void Hex_colors_parse(string text, byte r, byte g, byte b)
    {
        Assert.True(LogColor.TryParse(text, out LogColor color));

        Assert.Null(color.Named);
        Assert.Equal(new RgbColor(r, g, b), color.Rgb);
    }

    [Theory]
    [InlineData("purple")]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#fff")]
    [InlineData("#gggggg")]
    [InlineData("12345")]
    [InlineData("bright cyan")]
    public void Invalid_colors_do_not_parse(string text)
    {
        Assert.False(LogColor.TryParse(text, out _));
    }
}

public sealed class LogvuConfigSchemeTests
{
    [Fact]
    public void Built_in_style_names_are_the_documented_eight()
    {
        Assert.Equal(
            new[] { "error", "warning", "info", "debug", "accent", "emphasis", "dim", "success" },
            LogvuConfig.BuiltInStyles);
    }

    [Fact]
    public void Scheme_names_are_listed_after_the_built_in_schemes_in_configuration_order()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", """
            {
              "logsDirectory": "Logs",
              "filePrefix": "App",
              "fileNamePattern": "^App[.]log$",
              "schemes": [
                { "name": "zeta", "header": "(?<message>.*)" },
                { "name": "alpha", "header": "(?<level>\\w+) (?<message>.*)", "timeFormats": ["yyyy-MM-dd"] }
              ]
            }
            """);

        LogvuConfig config = LogvuConfig.Load(path);

        Assert.Equal(BuiltInSchemes.All.Count + 2, config.Schemes.Count);
        Assert.Equal("zeta", config.Schemes[BuiltInSchemes.All.Count].Name);
        Assert.Equal("alpha", config.Schemes[BuiltInSchemes.All.Count + 1].Name);
    }

    [Fact]
    public void Configured_scheme_header_pattern_is_not_anchored_by_the_caller_but_is_a_text_scheme()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", """
            {
              "logsDirectory": "Logs",
              "filePrefix": "App",
              "fileNamePattern": "^App[.]log$",
              "format": "my-app",
              "schemes": [ { "name": "my-app", "header": "(?<time>[0-9]{4}) (?<message>.*)", "timeFormats": ["yyyy"] } ]
            }
            """);

        ILogScheme? format = LogvuConfig.Load(path).Format;

        Assert.True(format!.IsHeader("2026 hello"));
        Assert.False(format.IsHeader("x 2026 hello"));
    }

    [Fact]
    public void Bad_color_at_a_style_path_is_reported_with_that_path()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", """
            {
              "logsDirectory": "Logs",
              "filePrefix": "App",
              "fileNamePattern": "^App[.]log$",
              "styles": { "deploy": { "bold": true, "background": "purple" } }
            }
            """);

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfig.Load(path));

        Assert.Contains("styles.deploy.background: invalid color 'purple'", ex.Message);
    }
}
