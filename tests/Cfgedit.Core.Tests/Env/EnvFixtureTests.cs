using System.Text;
using Cfgedit.Core.Env;
using Cfgedit.Core.Text;
using static Cfgedit.Core.Tests.Env.EnvTestSupport;

namespace Cfgedit.Core.Tests.Env;

public sealed class EnvFixtureTests
{
    [Fact]
    public void Aspnet_items_are_in_source_order_with_their_kinds()
    {
        EnvDocument doc = Fixture("aspnet.env");

        Assert.Equal(
            [
                "comment", "entry", "entry", "blank", "comment", "entry", "entry", "entry", "blank", "entry", "entry",
                "entry", "entry", "entry", "entry", "entry", "entry", "entry", "entry", "entry", "comment",
            ],
            doc.Items.Select(Kind).ToArray());
    }

    [Fact]
    public void Aspnet_entries_have_their_keys_values_and_quote_styles()
    {
        EnvDocument doc = Fixture("aspnet.env");

        Assert.Equal(
            [
                ("ASPNETCORE_ENVIRONMENT", "Development", EnvQuote.None),
                ("ASPNETCORE_URLS", "http://+:5000;https://+:5001", EnvQuote.Double),
                ("Logging__LogLevel__Default", "Information", EnvQuote.None),
                ("Logging__LogLevel__Microsoft.AspNetCore", "Warning", EnvQuote.None),
                ("Logging__LogLevel__Microsoft.EntityFrameworkCore", "Error", EnvQuote.Single),
                ("ConnectionStrings__Main", "Server=db;Database=app;User Id=app;Password=p@ss#1", EnvQuote.Double),
                ("Greeting", "hello", EnvQuote.None),
                ("Empty", "", EnvQuote.None),
                ("QuotedEmpty", "", EnvQuote.Double),
                ("Banner", "line one\nline two", EnvQuote.Double),
                ("Single", "it is $HOME", EnvQuote.Single),
                ("Escaped", "tab\there \"quoted\" back\\slash \\q", EnvQuote.Double),
                ("Expand", "${HOME}/data", EnvQuote.Double),
                ("Plain", "/var/data/app", EnvQuote.None),
                ("Indented", "spaced value", EnvQuote.None),
                ("Plain", "/second/copy", EnvQuote.None),
            ],
            Enumerable.Range(0, doc.Items.Count)
                .Where(i => doc.Items[i] is EnvEntry)
                .Select(i => Entry(doc, i))
                .Select(e => (e.Key, e.Value, e.Quote))
                .ToArray());
    }

    [Fact]
    public void Export_keyword_is_recorded_and_not_part_of_the_key()
    {
        EnvDocument doc = Fixture("aspnet.env");

        EnvEntry urls = Entry(doc, 2);
        Assert.NotNull(urls.Export);
        Assert.Equal("export", Slice(doc, urls.Export.Value));
        Assert.Equal("ASPNETCORE_URLS", Slice(doc, urls.KeySpan));
        Assert.Null(Entry(doc, 1).Export);
    }

    [Fact]
    public void Offsets_locate_the_key_equals_and_value_tokens()
    {
        EnvDocument doc = Fixture("aspnet.env");

        // Line 7 is "Logging__LogLevel__Microsoft.AspNetCore = Warning   # trailing comment".
        EnvEntry levels = Entry(doc, 6);
        AssertAt(doc, levels.KeySpan.Start, 7, 1);
        Assert.Equal("Logging__LogLevel__Microsoft.AspNetCore", Slice(doc, levels.KeySpan));
        AssertAt(doc, levels.EqualsStart, 7, 41);
        Assert.Equal("=", Slice(doc, new EnvSpan(levels.EqualsStart, levels.EqualsStart + 1)));
        Assert.Equal("Warning", Slice(doc, levels.ValueSpan));
        AssertAt(doc, levels.ValueSpan.Start, 7, 43);

        // Line 14 is the first line of Banner, which runs to line 15.
        EnvEntry banner = Entry(doc, 13);
        Assert.Equal("\"line one\nline two\"", Slice(doc, banner.ValueSpan));
        AssertAt(doc, banner.ValueSpan.Start, 14, 8);
        AssertAt(doc, banner.End, 16, 1);

        // Line 12 is "Empty=": the value is empty and sits at the line break.
        EnvEntry empty = Entry(doc, 11);
        Assert.Equal("Empty", empty.Key);
        Assert.Equal(0, empty.ValueSpan.Length);
        AssertAt(doc, empty.ValueSpan.Start, 12, 7);
    }

    [Fact]
    public void Trailing_comments_are_recorded_after_unquoted_and_double_quoted_values()
    {
        EnvDocument doc = Fixture("aspnet.env");

        EnvComment? levels = Entry(doc, 6).Comment;
        Assert.NotNull(levels);
        Assert.Equal(" trailing comment", levels.Value.Content);
        Assert.Equal("# trailing comment", Slice(doc, new EnvSpan(levels.Value.Start, levels.Value.End)));

        Assert.Equal(" comment after unquoted", Entry(doc, 10).Comment?.Content);
        Assert.Equal(" expansion is kept as written", Entry(doc, 16).Comment?.Content);
        Assert.Null(Entry(doc, 1).Comment);
    }

    [Fact]
    public void Comment_lines_keep_their_content_without_the_hash()
    {
        EnvDocument doc = Fixture("aspnet.env");

        var first = Assert.IsType<EnvCommentLine>(doc.Items[0]);
        Assert.Equal(" Synthetic ASP.NET-style environment file. Not a real configuration.", first.Comment.Content);
        AssertAt(doc, first.Comment.Start, 1, 1);

        var last = Assert.IsType<EnvCommentLine>(doc.Items[20]);
        Assert.Equal(" end of file", last.Comment.Content);
        Assert.Equal(doc.Source.Text.Length, last.End);
    }

    [Fact]
    public void Aspnet_round_trips_byte_for_byte()
    {
        byte[] bytes = File.ReadAllBytes(FixturePath("aspnet.env"));
        SourceText source = SourceText.Decode(bytes, "aspnet.env");
        EnvDocument doc = EnvDocument.Parse(source);

        Assert.Equal(source.Text, Reassemble(doc));
        Assert.Equal(bytes, source.Encode());
    }

    [Fact]
    public void Crlf_fixture_round_trips_and_decodes_line_breaks_in_quotes_as_lf()
    {
        // The fixture is stored with CRLF line breaks (there is no .gitattributes, so git keeps them).
        byte[] bytes = File.ReadAllBytes(FixturePath("crlf.env"));
        string crlf = Encoding.UTF8.GetString(bytes);
        Assert.Contains("\r\n", crlf, StringComparison.Ordinal);
        SourceText source = SourceText.Decode(bytes);
        EnvDocument doc = EnvDocument.Parse(source);

        Assert.Equal("\r\n", source.Newline);
        Assert.Equal(crlf, Reassemble(doc));
        Assert.Equal(
            ["comment", "entry", "entry", "entry", "entry"],
            doc.Items.Select(Kind).ToArray());

        EnvEntry port = Entry(doc, 2);
        Assert.Equal("5432", port.Value);
        Assert.Equal(" trailing comment", port.Comment?.Content);

        EnvEntry banner = Entry(doc, 3);
        Assert.Equal("one\ntwo", banner.Value);
        Assert.Equal("\"one\r\ntwo\"", Slice(doc, banner.ValueSpan));
        Assert.Equal("end", Entry(doc, 4).Value);
        Assert.Equal(crlf, source.Text);
    }
}
