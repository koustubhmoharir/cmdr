using Logvu.Core.Config;

namespace Logvu.Core.Tests.Config;

public sealed class LogvuConfigMessageFieldsTests
{
    private const string Minimal = """
        {
          "logsDirectory": "Logs",
          "filePrefix": "SheetKraft",
          "fileNamePattern": "^SheetKraft(?:[0-9]+)?[.]log$"
        }
        """;

    /// <summary>Writes the minimal configuration with <c>messageFields</c> set to <paramref name="messageFields"/>, a JSON value, and loads it.</summary>
    private static LogvuConfig LoadWithMessageFields(ConfigTestDirectory dir, string messageFields)
    {
        string path = dir.Write("logvu.json", Minimal.Replace("}", $", \"messageFields\": {messageFields} }}"));
        return LogvuConfig.Load(path);
    }

    [Fact]
    public void Absent_message_fields_are_empty()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Minimal);

        Assert.Empty(LogvuConfig.Load(path).MessageFields);
    }

    [Fact]
    public void Empty_message_fields_array_is_empty()
    {
        using var dir = new ConfigTestDirectory();

        Assert.Empty(LoadWithMessageFields(dir, "[]").MessageFields);
    }

    [Fact]
    public void Null_message_fields_are_empty()
    {
        using var dir = new ConfigTestDirectory();

        Assert.Empty(LoadWithMessageFields(dir, "null").MessageFields);
    }

    [Fact]
    public void Valid_paths_are_parsed_in_configuration_order()
    {
        using var dir = new ConfigTestDirectory();

        IReadOnlyList<string> fields = LoadWithMessageFields(dir, "[ \"status\", \"req.path\", \"err.attrs[0].code\", \"status\" ]").MessageFields;

        Assert.Equal(new[] { "status", "req.path", "err.attrs[0].code", "status" }, fields);
    }

    [Fact]
    public void Message_fields_are_separate_from_columns()
    {
        using var dir = new ConfigTestDirectory();

        LogvuConfig config = LoadWithMessageFields(dir, "[ \"status\" ]");

        Assert.Empty(config.Columns);
        Assert.Equal(new[] { "status" }, config.MessageFields);
    }

    [Fact]
    public void Message_fields_that_are_not_an_array_are_a_json_error_at_the_setting()
    {
        using var dir = new ConfigTestDirectory();

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LoadWithMessageFields(dir, "\"status\""));

        Assert.Contains("(path: $.messageFields)", ex.Message);
    }

    [Theory]
    [MemberData(nameof(InvalidMessageFields))]
    public void Invalid_message_field_is_an_error_naming_the_setting_at_its_index(string messageFields, string jsonPath, string problem)
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Minimal.Replace("}", $", \"messageFields\": {messageFields} }}"));

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfig.Load(path));

        Assert.StartsWith($"Configuration file '{Path.GetFullPath(path)}': {jsonPath}: ", ex.Message);
        Assert.Contains(problem, ex.Message);
        Assert.Contains("messageFields", ex.Message);
    }

    public static TheoryData<string, string, string> InvalidMessageFields()
    {
        return new TheoryData<string, string, string>
        {
            { "[ \"\" ]", "messageFields[0]", "must not be empty" },
            { "[ \"   \" ]", "messageFields[0]", "must not be empty" },
            { "[ \"ok\", \"\" ]", "messageFields[1]", "must not be empty" },
            { "[ null ]", "messageFields[0]", "must be a string, found null" },
            { "[ 3 ]", "messageFields[0]", "must be a string, found number" },
            { "[ \"ok\", true ]", "messageFields[1]", "must be a string, found true" },
            { "[ {} ]", "messageFields[0]", "must be a string, found object" },
            { "[ [ \"a\" ] ]", "messageFields[0]", "must be a string, found array" },
            { "[ \"a..b\" ]", "messageFields[0]", "is not a valid path" },
            { "[ \".a\" ]", "messageFields[0]", "is not a valid path" },
            { "[ \"a[\" ]", "messageFields[0]", "is not a valid path" },
            { "[ \"a[x]\" ]", "messageFields[0]", "is not a valid path" },
            { "[ \"status\", \"req.path[-1]\" ]", "messageFields[1]", "is not a valid path" },
        };
    }
}
