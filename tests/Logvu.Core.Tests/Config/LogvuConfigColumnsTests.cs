using Logvu.Core.Config;

namespace Logvu.Core.Tests.Config;

public sealed class LogvuConfigColumnsTests
{
    private const string Minimal = """
        {
          "logsDirectory": "Logs",
          "filePrefix": "SheetKraft",
          "fileNamePattern": "^SheetKraft(?:[0-9]+)?[.]log$"
        }
        """;

    /// <summary>Writes the minimal configuration with <c>columns</c> set to <paramref name="columns"/>, a JSON value, and loads it.</summary>
    private static LogvuConfig LoadWithColumns(ConfigTestDirectory dir, string columns)
    {
        string path = dir.Write("logvu.json", Minimal.Replace("}", $", \"columns\": {columns} }}"));
        return LogvuConfig.Load(path);
    }

    [Fact]
    public void Absent_columns_are_empty()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Minimal);

        Assert.Empty(LogvuConfig.Load(path).Columns);
    }

    [Fact]
    public void Empty_columns_array_is_empty()
    {
        using var dir = new ConfigTestDirectory();

        Assert.Empty(LoadWithColumns(dir, "[]").Columns);
    }

    [Fact]
    public void Null_columns_are_empty()
    {
        using var dir = new ConfigTestDirectory();

        Assert.Empty(LoadWithColumns(dir, "null").Columns);
    }

    [Fact]
    public void String_items_are_paths_with_default_titles_and_width()
    {
        using var dir = new ConfigTestDirectory();

        IReadOnlyList<ColumnSpec> columns = LoadWithColumns(dir, "[ \"status\", \"req.path\" ]").Columns;

        Assert.Equal(2, columns.Count);
        Assert.Equal(new ColumnSpec("status", "status", ColumnSpec.DefaultMaxWidth), columns[0]);
        Assert.Equal("req.path", columns[1].Path);
        Assert.Equal("path", columns[1].Title);
        Assert.Equal(ColumnSpec.DefaultMaxWidth, columns[1].MaxWidth);
        Assert.Equal(30, ColumnSpec.DefaultMaxWidth);
    }

    [Fact]
    public void Object_item_takes_path_title_and_width()
    {
        using var dir = new ConfigTestDirectory();

        IReadOnlyList<ColumnSpec> columns = LoadWithColumns(dir, "[ { \"path\": \"req.path\", \"title\": \"Path\", \"width\": 12 } ]").Columns;

        ColumnSpec column = Assert.Single(columns);
        Assert.Equal("req.path", column.Path);
        Assert.Equal("Path", column.Title);
        Assert.Equal(12, column.MaxWidth);
    }

    [Fact]
    public void Object_item_without_title_or_width_takes_the_defaults()
    {
        using var dir = new ConfigTestDirectory();

        ColumnSpec column = Assert.Single(LoadWithColumns(dir, "[ { \"path\": \"items[0]\" } ]").Columns);

        Assert.Equal("items", column.Title);
        Assert.Equal(ColumnSpec.DefaultMaxWidth, column.MaxWidth);
    }

    [Fact]
    public void Columns_keep_configuration_order_and_repeated_paths()
    {
        using var dir = new ConfigTestDirectory();

        IReadOnlyList<ColumnSpec> columns = LoadWithColumns(dir, "[ \"b\", \"a\", \"b\", { \"path\": \"a\", \"title\": \"A2\" } ]").Columns;

        Assert.Equal(new[] { "b", "a", "b", "a" }, columns.Select(c => c.Path).ToArray());
        Assert.Equal("A2", columns[3].Title);
    }

    [Theory]
    [InlineData("status", "status")]
    [InlineData("req.path", "path")]
    [InlineData("items[0]", "items")]
    [InlineData("err.attrs[1].code", "code")]
    [InlineData("a.b[2]", "b")]
    [InlineData("matrix[1][2]", "matrix")]
    [InlineData("[0]", "[0]")]
    [InlineData("items[0].[1]", "[1]")]
    public void Default_title_is_the_last_segment_without_indices(string path, string title)
    {
        using var dir = new ConfigTestDirectory();

        ColumnSpec column = Assert.Single(LoadWithColumns(dir, $"[ \"{path}\" ]").Columns);

        Assert.Equal(title, column.Title);
        Assert.Equal(title, ColumnSpec.DefaultTitleOf(path));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    [InlineData(30)]
    public void Width_in_range_is_accepted(int width)
    {
        using var dir = new ConfigTestDirectory();

        ColumnSpec column = Assert.Single(LoadWithColumns(dir, $"[ {{ \"path\": \"a\", \"width\": {width} }} ]").Columns);

        Assert.Equal(width, column.MaxWidth);
    }

    [Fact]
    public void Null_title_and_width_are_absent()
    {
        using var dir = new ConfigTestDirectory();

        ColumnSpec column = Assert.Single(LoadWithColumns(dir, "[ { \"path\": \"req.path\", \"title\": null, \"width\": null } ]").Columns);

        Assert.Equal("path", column.Title);
        Assert.Equal(ColumnSpec.DefaultMaxWidth, column.MaxWidth);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("null", false)]
    [InlineData(null, false)]
    public void Object_item_takes_keep_end_as_a_boolean_and_null_is_absent(string? keepEnd, bool expected)
    {
        using var dir = new ConfigTestDirectory();
        string member = keepEnd is null ? string.Empty : $", \"keepEnd\": {keepEnd}";

        ColumnSpec column = Assert.Single(LoadWithColumns(dir, $"[ {{ \"path\": \"logger\"{member} }} ]").Columns);

        Assert.Equal(expected, column.KeepEnd);
    }

    [Fact]
    public void Keep_end_is_read_with_the_other_members()
    {
        using var dir = new ConfigTestDirectory();

        ColumnSpec column = Assert.Single(LoadWithColumns(dir, "[ { \"path\": \"logger\", \"title\": \"Logger\", \"width\": 20, \"keepEnd\": true } ]").Columns);

        Assert.Equal(new ColumnSpec("logger", "Logger", 20, keepEnd: true), column);
    }

    [Fact]
    public void A_path_string_item_does_not_keep_its_end()
    {
        using var dir = new ConfigTestDirectory();

        ColumnSpec column = Assert.Single(LoadWithColumns(dir, "[ \"logger\" ]").Columns);

        Assert.False(column.KeepEnd);
    }

    [Fact]
    public void Columns_that_are_not_an_array_are_a_json_error_at_the_setting()
    {
        using var dir = new ConfigTestDirectory();

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LoadWithColumns(dir, "\"status\""));

        Assert.Contains("(path: $.columns)", ex.Message);
    }

    [Theory]
    [MemberData(nameof(InvalidColumns))]
    public void Invalid_column_is_an_error_at_its_json_path(string columns, string jsonPath, string problem)
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Minimal.Replace("}", $", \"columns\": {columns} }}"));

        LogvuConfigException ex = Assert.Throws<LogvuConfigException>(() => LogvuConfig.Load(path));

        Assert.StartsWith($"Configuration file '{Path.GetFullPath(path)}': {jsonPath}: ", ex.Message);
        Assert.Contains(problem, ex.Message);
    }

    public static TheoryData<string, string, string> InvalidColumns()
    {
        return new TheoryData<string, string, string>
        {
            { "[ null ]", "columns[0]", "must be a string or an object, found null" },
            { "[ \"ok\", 3 ]", "columns[1]", "must be a string or an object, found number" },
            { "[ true ]", "columns[0]", "must be a string or an object, found true" },
            { "[ [ \"a\" ] ]", "columns[0]", "must be a string or an object, found array" },

            { "[ {} ]", "columns[0].path", "is required" },
            { "[ { \"path\": null } ]", "columns[0].path", "is required" },
            { "[ { \"path\": \"\" } ]", "columns[0].path", "must not be empty" },
            { "[ { \"path\": \"   \" } ]", "columns[0].path", "must not be empty" },
            { "[ { \"path\": 5 } ]", "columns[0].path", "must be a string, found number" },

            { "[ \"\" ]", "columns[0]", "must not be empty" },
            { "[ \"a..b\" ]", "columns[0]", "is not a valid path" },
            { "[ \".a\" ]", "columns[0]", "is not a valid path" },
            { "[ \"a.\" ]", "columns[0]", "is not a valid path" },
            { "[ \"a[]\" ]", "columns[0]", "is not a valid path" },
            { "[ \"a[x]\" ]", "columns[0]", "is not a valid path" },
            { "[ \"a[-1]\" ]", "columns[0]", "is not a valid path" },
            { "[ \"a[0\" ]", "columns[0]", "is not a valid path" },
            { "[ \"a[0]b\" ]", "columns[0]", "is not a valid path" },
            { "[ \"a[0]]\" ]", "columns[0]", "is not a valid path" },
            { "[ \"ok\", { \"path\": \"a[\" } ]", "columns[1].path", "is not a valid path" },

            { "[ { \"path\": \"a\", \"title\": 4 } ]", "columns[0].title", "must be a string, found number" },
            { "[ { \"path\": \"a\", \"title\": \"\" } ]", "columns[0].title", "must not be empty" },
            { "[ { \"path\": \"a\", \"title\": \"  \" } ]", "columns[0].title", "must not be empty" },

            { "[ { \"path\": \"a\", \"keepEnd\": \"yes\" } ]", "columns[0].keepEnd", "must be true or false, found string" },
            { "[ { \"path\": \"a\", \"keepEnd\": 1 } ]", "columns[0].keepEnd", "must be true or false, found number" },
            { "[ \"ok\", { \"path\": \"a\", \"keepEnd\": [] } ]", "columns[1].keepEnd", "must be true or false, found array" },

            { "[ { \"path\": \"a\", \"width\": 0 } ]", "columns[0].width", "must be between 1 and 200, got 0" },
            { "[ { \"path\": \"a\", \"width\": 201 } ]", "columns[0].width", "must be between 1 and 200, got 201" },
            { "[ { \"path\": \"a\", \"width\": -5 } ]", "columns[0].width", "must be between 1 and 200, got -5" },
            { "[ { \"path\": \"a\", \"width\": 1.5 } ]", "columns[0].width", "must be an integer between 1 and 200" },
            { "[ { \"path\": \"a\", \"width\": \"30\" } ]", "columns[0].width", "must be an integer between 1 and 200" },

            { "[ { \"path\": \"a\", \"colour\": \"red\" } ]", "columns[0].colour", "unknown member 'colour'" },
            { "[ { \"path\": \"a\", \"keepend\": true } ]", "columns[0].keepend", "unknown member 'keepend'" },
            { "[ \"a\", { \"path\": \"b\", \"title\": \"T\", \"widht\": 3 } ]", "columns[1].widht", "unknown member 'widht'" },
        };
    }
}
