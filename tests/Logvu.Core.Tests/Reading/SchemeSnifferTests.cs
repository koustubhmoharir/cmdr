using System.Text;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;

namespace Logvu.Core.Tests.Reading;

public sealed class SchemeSnifferTests
{
    private static MemoryStream Fixture(string fileName) =>
        new(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName)), writable: false);

    private static MemoryStream Text(string text) => new(Encoding.UTF8.GetBytes(text), writable: false);

    [Theory]
    [InlineData("log4net-core.log", "log4net-core")]
    [InlineData("log4net-web.log", "log4net-web")]
    [InlineData("log4net-scheduler.log", "log4net-scheduler")]
    [InlineData("log4net-date-first.log", "log4net-date-first")]
    [InlineData("serilog.json", "serilog-json")]
    [InlineData("slog.json", "slog-json")]
    public void Each_fixture_sniffs_to_its_scheme(string fileName, string schemeName)
    {
        using (MemoryStream closed = Fixture(fileName))
        {
            Assert.Equal(schemeName, SchemeSniffer.Sniff(closed, BuiltInSchemes.All, live: false)?.Name);
        }

        using (MemoryStream live = Fixture(fileName))
        {
            Assert.Equal(schemeName, SchemeSniffer.Sniff(live, BuiltInSchemes.All, live: true)?.Name);
        }
    }

    [Fact]
    public void Json_file_whose_first_line_is_50_kb_sniffs_to_serilog_json()
    {
        string first =
            "{\"Timestamp\":\"2026-11-18T10:20:30.1234567+05:30\",\"Level\":\"Error\",\"MessageTemplate\":\"Failed\"," +
            "\"Exception\":\"" + new string('e', 50_000) + "\",\"Properties\":{\"SourceContext\":\"A.B\"}}";
        Assert.True(Encoding.UTF8.GetByteCount(first) > LineReader.TextCap);

        string content = first + "\n" + "{\"Timestamp\":\"2026-11-18T10:20:31+05:30\",\"Level\":\"Information\",\"MessageTemplate\":\"ok\"}\n";
        using var stream = Text(content);

        ILogScheme? scheme = SchemeSniffer.Sniff(stream, BuiltInSchemes.All, live: false);

        Assert.Equal("serilog-json", scheme?.Name);
    }

    [Fact]
    public void Live_file_whose_only_content_is_an_unterminated_line_sniffs_to_null()
    {
        using var stream = Text("INFO  2026-11-18 10:20:30,123 [1] A.B - partial");
        Assert.Null(SchemeSniffer.Sniff(stream, BuiltInSchemes.All, live: true));
    }

    [Fact]
    public void Closed_file_whose_only_content_is_an_unterminated_line_sniffs_to_its_scheme()
    {
        using var stream = Text("INFO  2026-11-18 10:20:30,123 [1] A.B - partial");
        Assert.Equal("log4net-core", SchemeSniffer.Sniff(stream, BuiltInSchemes.All, live: false)?.Name);
    }

    [Fact]
    public void Empty_stream_sniffs_to_null()
    {
        using var stream = Text("");
        Assert.Null(SchemeSniffer.Sniff(stream, BuiltInSchemes.All, live: false));
    }

    [Fact]
    public void Blank_lines_before_the_content_are_skipped()
    {
        using var stream = Text("\n   \n2026-11-18 10:20:30,123 [1] INFO A.B - hello\n");
        Assert.Equal("log4net-date-first", SchemeSniffer.Sniff(stream, BuiltInSchemes.All, live: false)?.Name);
    }
}
