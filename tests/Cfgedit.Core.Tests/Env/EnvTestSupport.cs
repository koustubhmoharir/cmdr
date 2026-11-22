using System.Text;
using Cfgedit.Core.Env;
using Cfgedit.Core.Text;

namespace Cfgedit.Core.Tests.Env;

/// <summary>Shared helpers for the .env tests. Expectations passed in are literal values, never computed by the parser.</summary>
internal static class EnvTestSupport
{
    public static SourceText Utf8(string text)
    {
        return SourceText.Create(text, Encoding.UTF8, false);
    }

    public static EnvDocument Parse(string text)
    {
        return EnvDocument.Parse(Utf8(text));
    }

    /// <summary>Joins lines with LF, so that a multi-line expectation reads as it is laid out.</summary>
    public static string Lines(params string[] lines)
    {
        return string.Join("\n", lines);
    }

    public static string FixturePath(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", "env", name);
    }

    /// <summary>Loads a fixture from the output folder and parses it through the byte path, as the editor does.</summary>
    public static EnvDocument Fixture(string name)
    {
        return EnvDocument.Parse(SourceText.Decode(File.ReadAllBytes(FixturePath(name)), name));
    }

    /// <summary>The text of the items' spans, printed in order. It must equal the source text.</summary>
    public static string Reassemble(EnvDocument doc)
    {
        var sb = new StringBuilder();
        foreach (EnvItem item in doc.Items)
        {
            sb.Append(doc.Source.Text, item.Start, item.End - item.Start);
        }

        return sb.ToString();
    }

    /// <summary>The kind of an item as a word: blank, comment or entry.</summary>
    public static string Kind(EnvItem item)
    {
        return item switch
        {
            EnvBlankLine => "blank",
            EnvCommentLine => "comment",
            EnvEntry => "entry",
            _ => throw new InvalidOperationException("unknown item"),
        };
    }

    public static EnvEntry Entry(EnvDocument doc, int index)
    {
        return Assert.IsType<EnvEntry>(doc.Items[index]);
    }

    public static string Slice(EnvDocument doc, EnvSpan span)
    {
        return doc.Source.Text[span.Start..span.End];
    }

    /// <summary>Asserts that an offset is at the given 1-based line and column.</summary>
    public static void AssertAt(EnvDocument doc, int offset, int line, int column)
    {
        Assert.Equal(new LinePosition(line, column), doc.Source.GetPosition(offset));
    }

    /// <summary>Asserts that the text fails to parse with the message, and at the line and column.</summary>
    public static void AssertParseError(string text, string message, int line, int column)
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => Parse(text));
        Assert.Equal(message, ex.Message);
        Assert.Equal(line, ex.Line);
        Assert.Equal(column, ex.Column);
    }

    /// <summary>Asserts a single entry parses from the text, and returns it with its document.</summary>
    public static EnvEntry SingleEntry(string text, out EnvDocument doc)
    {
        doc = Parse(text);
        Assert.Single(doc.Items);
        return Entry(doc, 0);
    }
}
