using Logvu.Core.Config;

namespace Logvu.Core.Tests.Config;

public sealed class LogvuConfigInvalidTests
{
    /// <summary>A configuration with valid required members followed by <paramref name="extra"/>, which must start with a comma.</summary>
    private static string Doc(
        string logsDirectory = "\"Logs\"",
        string filePrefix = "\"SheetKraft\"",
        string fileNamePattern = "\"^SheetKraft(?:[0-9]+)?[.]log$\"",
        string extra = "") =>
        "{\n  \"logsDirectory\": " + logsDirectory + ",\n  \"filePrefix\": " + filePrefix + ",\n  \"fileNamePattern\": " + fileNamePattern + extra + "\n}";

    [Theory]
    [MemberData(nameof(InvalidDocuments))]
    public void Invalid_setting_is_reported_with_the_file_and_the_json_path(string json, string jsonPath, string problem)
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", json);

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfig.Load(path));

        Assert.StartsWith($"Configuration file '{Path.GetFullPath(path)}': {jsonPath}: ", ex.Message);
        Assert.Contains(problem, ex.Message);
    }

    public static TheoryData<string, string, string> InvalidDocuments()
    {
        return new TheoryData<string, string, string>
        {
            { Doc(filePrefix: "\"\""), "filePrefix", "must not be empty" },
            { Doc(filePrefix: "\"   \""), "filePrefix", "must not be empty" },
            { Doc(logsDirectory: "\"\""), "logsDirectory", "must not be empty" },
            { Doc(fileNamePattern: "\"^(SheetKraft\""), "fileNamePattern", "not a valid regular expression" },
            { Doc(fileNamePattern: "\"\""), "fileNamePattern", "must not be empty" },

            // Wrapping an unbalanced pattern as \A(?:...)\z would make it valid, so the pattern is checked alone first.
            { Doc(fileNamePattern: "\"a)(?:\""), "fileNamePattern", "not a valid regular expression" },

            { Doc(extra: ",\n  \"activeFile\": \"Other.log\""), "activeFile", "does not start with filePrefix 'SheetKraft'" },
            { Doc(extra: ",\n  \"activeFile\": \"SheetKraftWeb.log\""), "activeFile", "does not match fileNamePattern" },
            { Doc(extra: ",\n  \"activeFile\": \"sub/SheetKraft.log\""), "activeFile", "must be a file name, not a path" },
            { Doc(extra: ",\n  \"activeFile\": \"\""), "activeFile", "must not be empty" },

            { Doc(extra: ",\n  \"format\": \"nonexistent\""), "format", "unknown scheme 'nonexistent'" },
            { Doc(extra: ",\n  \"timeZone\": \"Mars/Olympus\""), "timeZone", "unknown time zone 'Mars/Olympus'" },
            { Doc(extra: ",\n  \"timeZone\": \"\""), "timeZone", "unknown time zone ''" },
            { Doc(extra: ",\n  \"recentCapacity\": 99"), "recentCapacity", "must be between 100 and 1000000, got 99" },
            { Doc(extra: ",\n  \"recentCapacity\": 1000001"), "recentCapacity", "must be between 100 and 1000000, got 1000001" },

            {
                Doc(extra: ",\n  \"schemes\": [ { \"name\": \"a\", \"header\": \"(?<message>.*)\" }, { \"name\": \"b\", \"header\": \"(?<time>x\" } ]"),
                "schemes[1].header",
                "not a valid regular expression"
            },
            {
                Doc(extra: ",\n  \"schemes\": [ { \"name\": \"a\", \"header\": \"(?<message>.*)\" }, { \"name\": \"b\", \"header\": \"(?<time>x)\" } ]"),
                "schemes[1].header",
                "must have a named group 'message'"
            },
            {
                Doc(extra: ",\n  \"schemes\": [ { \"name\": \"a\", \"header\": \"(?<message>.*)\" }, { \"name\": \"a\", \"header\": \"(?<message>.*)\" } ]"),
                "schemes[1].name",
                "duplicate scheme name 'a'"
            },
            {
                Doc(extra: ",\n  \"schemes\": [ { \"name\": \"A\", \"header\": \"(?<message>.*)\" }, { \"name\": \"a\", \"header\": \"(?<message>.*)\" } ]"),
                "schemes[1].name",
                "duplicate scheme name 'a'"
            },
            {
                Doc(extra: ",\n  \"schemes\": [ { \"name\": \"log4net-core\", \"header\": \"(?<message>.*)\" } ]"),
                "schemes[0].name",
                "'log4net-core' is the name of a built-in scheme"
            },
            {
                Doc(extra: ",\n  \"schemes\": [ { \"name\": \"auto\", \"header\": \"(?<message>.*)\" } ]"),
                "schemes[0].name",
                "'auto' is reserved"
            },
            {
                Doc(extra: ",\n  \"schemes\": [ { \"name\": \"a\", \"header\": \"(?<message>.*)\", \"timeFormats\": [\"  \"] } ]"),
                "schemes[0].timeFormats[0]",
                "must not be empty"
            },
            {
                Doc(extra: ",\n  \"schemes\": [ { \"name\": \"a\", \"header\": \"\" } ]"),
                "schemes[0].header",
                "must not be empty"
            },
            {
                Doc(extra: ",\n  \"schemes\": [ { \"header\": \"(?<message>.*)\" } ]"),
                "schemes[0].name",
                "is required"
            },

            {
                Doc(extra: ",\n  \"highlights\": [ { \"field\": \"message\", \"pattern\": \"[\", \"style\": \"error\" } ]"),
                "highlights[0].pattern",
                "not a valid regular expression"
            },
            {
                Doc(extra: ",\n  \"highlights\": [ { \"field\": \"message\", \"pattern\": \"x\", \"style\": \"foo\" } ]"),
                "highlights[0].style",
                "unknown style 'foo'"
            },
            {
                Doc(extra: ",\n  \"highlights\": [ { \"field\": \"message\", \"pattern\": \"x\" } ]"),
                "highlights[0].style",
                "is required"
            },
            {
                Doc(extra: ",\n  \"highlights\": [ { \"field\": \"message\", \"pattern\": \"x\", \"style\": \"error\" }, { \"field\": \"message\", \"pattern\": \"x\", \"style\": \"nope\" } ]"),
                "highlights[1].style",
                "unknown style 'nope'"
            },

            { Doc(extra: ",\n  \"styles\": { \"error\": { \"bold\": true } }"), "styles.error", "'error' is a built-in style name" },
            { Doc(extra: ",\n  \"styles\": { \"Warning\": { \"bold\": true } }"), "styles.Warning", "'Warning' is a built-in style name" },
            {
                Doc(extra: ",\n  \"styles\": { \"deploy\": { \"foreground\": \"purple\" } }"),
                "styles.deploy.foreground",
                "invalid color 'purple'"
            },
            {
                Doc(extra: ",\n  \"styles\": { \"deploy\": { \"background\": \"#12345\" } }"),
                "styles.deploy.background",
                "invalid color '#12345'"
            },
            {
                Doc(extra: ",\n  \"styles\": { \"deploy\": { \"foreground\": \"#gggggg\" } }"),
                "styles.deploy.foreground",
                "invalid color '#gggggg'"
            },
            {
                Doc(extra: ",\n  \"styles\": { \"deploy\": {}, \"DEPLOY\": {} }"),
                "styles.DEPLOY",
                "duplicate style name 'DEPLOY'"
            },
            { Doc(extra: ",\n  \"styles\": { \"deploy\": null }"), "styles.deploy", "must be an object, not null" },

            { Doc(extra: ",\n  \"frameworkPrefixes\": [\"System.\", \"\"]"), "frameworkPrefixes[1]", "must not be empty" },
            { Doc(extra: ",\n  \"frameworkPrefixes\": [null]"), "frameworkPrefixes[0]", "must not be null" },
        };
    }

    [Fact]
    public void Unknown_top_level_member_is_reported_as_malformed_json_at_its_path()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Doc(fileNamePattern: "\"^SheetKraft[.]log$\"").Replace("\"fileNamePattern\"", "\"fileNamePatern\""));

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfig.Load(path));

        Assert.StartsWith($"Malformed JSON in '{Path.GetFullPath(path)}' at line ", ex.Message);
        Assert.Contains("fileNamePatern", ex.Message);
    }

    [Fact]
    public void Unknown_member_in_a_scheme_is_an_error()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Doc(extra: ",\n  \"schemes\": [ { \"name\": \"a\", \"headr\": \"x\" } ]"));

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfig.Load(path));

        Assert.StartsWith($"Malformed JSON in '{Path.GetFullPath(path)}' at line ", ex.Message);
        Assert.Contains("headr", ex.Message);
    }

    [Theory]
    [InlineData("\"recentCapacity\": \"x\"", "$.recentCapacity")]
    [InlineData("\"schemes\": \"none\"", "$.schemes")]
    [InlineData("\"highlights\": [ { \"field\": 3 } ]", "$.highlights[0].field")]
    public void Wrong_type_is_reported_as_malformed_json_at_its_path(string member, string jsonPath)
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Doc(extra: ",\n  " + member));

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfig.Load(path));

        Assert.StartsWith($"Malformed JSON in '{Path.GetFullPath(path)}' at line ", ex.Message);
        Assert.Contains($"(path: {jsonPath})", ex.Message);
    }

    [Fact]
    public void Trailing_garbage_is_malformed_json_with_position()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", "{\n \"logsDirectory\": \"Logs\"\n} x");

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfig.Load(path));

        Assert.Matches(@"^Malformed JSON in '.*' at line \d+, position \d+: ", ex.Message);
    }
}
