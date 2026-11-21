using System.Text;
using Cfgedit.Core.Xml;
using static Cfgedit.Core.Tests.Xml.XmlTestSupport;

namespace Cfgedit.Core.Tests.Xml;

/// <summary>Documents in generated encodings, parsed through <see cref="XmlDocumentSyntax.Parse(ReadOnlySpan{byte}, string?)"/>.</summary>
public sealed class XmlParseEncodingTests
{
    /// <summary>U+00E9, written as a number so that no source file holds the raw character.</summary>
    private static readonly string Eacute = char.ConvertFromUtf32(0xE9);

    /// <summary>U+20AC, the euro sign.</summary>
    private static readonly string Euro = char.ConvertFromUtf32(0x20AC);

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    /// <summary>The document, with the declared encoding. Its attribute holds U+00E9 and its text holds U+20AC.</summary>
    private static string Document(string encoding) =>
        "<?xml version=\"1.0\" encoding=\"" + encoding + "\"?><r a=\"" + Eacute + "\">" + Euro + "</r>";

    private static void AssertDocument(XmlDocumentSyntax doc, string encodingName, int codePage)
    {
        Assert.Equal(encodingName, doc.Source.Encoding.WebName);
        Assert.Equal(codePage, doc.Source.Encoding.CodePage);
        Assert.Equal(Eacute, AttributeValue(doc.Root, "a"));
        Assert.Equal(Euro, Assert.IsType<XmlTextSyntax>(Assert.Single(doc.Root.Children)).Value);
    }

    [Fact]
    public void Utf8_without_a_bom_parses_and_round_trips()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Document("utf-8"));

        XmlDocumentSyntax doc = XmlDocumentSyntax.Parse(bytes, "utf8.xml");

        Assert.False(doc.Source.HasBom);
        AssertDocument(doc, "utf-8", 65001);
        Assert.Equal(bytes, doc.Source.Encode());
    }

    [Fact]
    public void Utf8_with_a_bom_strips_the_bom_and_round_trips_it()
    {
        byte[] bytes = Concat([0xEF, 0xBB, 0xBF], Encoding.UTF8.GetBytes(Document("utf-8")));

        XmlDocumentSyntax doc = XmlDocumentSyntax.Parse(bytes, "utf8bom.xml");

        Assert.True(doc.Source.HasBom);
        Assert.StartsWith("<?xml", doc.Source.Text);
        AssertDocument(doc, "utf-8", 65001);
        Assert.Equal(bytes, doc.Source.Encode());
    }

    [Fact]
    public void Utf16_le_with_a_bom_parses_and_round_trips()
    {
        byte[] bytes = Concat([0xFF, 0xFE], Encoding.Unicode.GetBytes(Document("utf-16")));

        XmlDocumentSyntax doc = XmlDocumentSyntax.Parse(bytes, "utf16le.xml");

        Assert.True(doc.Source.HasBom);
        AssertDocument(doc, "utf-16", 1200);
        Assert.Equal(bytes, doc.Source.Encode());
    }

    [Fact]
    public void Utf16_be_with_a_bom_parses_and_round_trips()
    {
        byte[] bytes = Concat([0xFE, 0xFF], Encoding.BigEndianUnicode.GetBytes(Document("utf-16")));

        XmlDocumentSyntax doc = XmlDocumentSyntax.Parse(bytes, "utf16be.xml");

        Assert.True(doc.Source.HasBom);
        AssertDocument(doc, "utf-16BE", 1201);
        Assert.Equal(bytes, doc.Source.Encode());
    }

    [Fact]
    public void Utf16_le_without_a_bom_is_found_by_its_pattern_and_round_trips()
    {
        byte[] bytes = Encoding.Unicode.GetBytes(Document("utf-16"));
        Assert.Equal(new byte[] { 0x3C, 0x00, 0x3F, 0x00 }, bytes[..4]);

        XmlDocumentSyntax doc = XmlDocumentSyntax.Parse(bytes, "pattern-le.xml");

        Assert.False(doc.Source.HasBom);
        AssertDocument(doc, "utf-16", 1200);
        Assert.Equal(bytes, doc.Source.Encode());
    }

    [Fact]
    public void Utf16_be_without_a_bom_is_found_by_its_pattern_and_round_trips()
    {
        byte[] bytes = Encoding.BigEndianUnicode.GetBytes(Document("utf-16"));
        Assert.Equal(new byte[] { 0x00, 0x3C, 0x00, 0x3F }, bytes[..4]);

        XmlDocumentSyntax doc = XmlDocumentSyntax.Parse(bytes, "pattern-be.xml");

        Assert.False(doc.Source.HasBom);
        AssertDocument(doc, "utf-16BE", 1201);
        Assert.Equal(bytes, doc.Source.Encode());
    }

    [Fact]
    public void Windows_1252_bytes_0x80_and_0xE9_decode_and_round_trip()
    {
        // The bytes are written out: 0xE9 is e-acute and 0x80 is the euro sign in windows-1252.
        byte[] bytes = Concat(
            Ascii("<?xml version=\"1.0\" encoding=\"windows-1252\"?><r a=\""),
            [0xE9],
            Ascii("\">"),
            [0x80],
            Ascii("</r>"));

        XmlDocumentSyntax doc = XmlDocumentSyntax.Parse(bytes, "cp1252.xml");

        Assert.False(doc.Source.HasBom);
        AssertDocument(doc, "windows-1252", 1252);
        Assert.Equal(bytes, doc.Source.Encode());
    }

    [Fact]
    public void A_utf8_bom_with_a_windows_1252_declaration_is_a_contradiction_with_the_path()
    {
        byte[] bytes = Concat([0xEF, 0xBB, 0xBF], Encoding.UTF8.GetBytes(Document("windows-1252")));

        CfgeditException error = Assert.Throws<CfgeditException>(() => XmlDocumentSyntax.Parse(bytes, "bad.xml"));

        Assert.Equal("the UTF-8 byte order mark contradicts the declared encoding 'windows-1252'", error.Message);
        Assert.Equal("bad.xml", error.Path);
    }

    [Fact]
    public void An_undefined_windows_1252_byte_is_an_error_with_the_path()
    {
        byte[] bytes = Concat(Ascii("<?xml version=\"1.0\" encoding=\"windows-1252\"?><r>"), [0x81], Ascii("</r>"));

        CfgeditException error = Assert.Throws<CfgeditException>(() => XmlDocumentSyntax.Parse(bytes, "undef.xml"));

        Assert.Equal("undef.xml", error.Path);
        Assert.Contains("windows-1252", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_document_in_bytes_carries_the_path_and_the_reader_position()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("<a></b>");

        CfgeditException error = Assert.Throws<CfgeditException>(() => XmlDocumentSyntax.Parse(bytes, "broken.xml"));

        Assert.Equal("The 'a' start tag on line 1 position 2 does not match the end tag of 'b'.", error.Message);
        Assert.Equal("broken.xml", error.Path);
        Assert.Equal(1, error.Line);
        Assert.Equal(6, error.Column);
    }
}
