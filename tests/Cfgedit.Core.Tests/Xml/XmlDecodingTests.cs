using System.Text;
using Cfgedit.Core.Xml;
using Shared.Core;

namespace Cfgedit.Core.Tests.Xml;

public sealed class XmlDecodingTests
{
    private const string Windows1252Declaration = "<?xml version=\"1.0\" encoding=\"windows-1252\"?>";

    private static byte[] Concat(params byte[][] parts)
    {
        return parts.SelectMany(p => p).ToArray();
    }

    private static byte[] Utf8(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    private static byte[] Utf16Le(string text)
    {
        return Encoding.Unicode.GetBytes(text);
    }

    private static byte[] Utf16Be(string text)
    {
        return Encoding.BigEndianUnicode.GetBytes(text);
    }

    // ----- Byte order marks -----

    [Fact]
    public void Utf8_bom_is_removed_and_recorded_and_the_bytes_round_trip()
    {
        byte[] bytes = Concat([0xEF, 0xBB, 0xBF], Utf8("<a/>"));

        SourceText text = XmlDecoding.Decode(bytes, null);

        Assert.Equal("<a/>", text.Text);
        Assert.True(text.HasBom);
        Assert.Equal("utf-8", text.Encoding.WebName);
        Assert.Equal(bytes, text.Encode());
    }

    [Fact]
    public void Utf16_le_bom_is_removed_and_the_bytes_round_trip()
    {
        byte[] bytes = Concat([0xFF, 0xFE], Utf16Le("<a/>"));

        SourceText text = XmlDecoding.Decode(bytes, null);

        Assert.Equal("<a/>", text.Text);
        Assert.True(text.HasBom);
        Assert.Equal("utf-16", text.Encoding.WebName);
        Assert.Equal(bytes, text.Encode());
    }

    [Fact]
    public void Utf16_be_bom_is_removed_and_the_bytes_round_trip()
    {
        byte[] bytes = Concat([0xFE, 0xFF], Utf16Be("<a/>"));

        SourceText text = XmlDecoding.Decode(bytes, null);

        Assert.Equal("<a/>", text.Text);
        Assert.True(text.HasBom);
        Assert.Equal("utf-16BE", text.Encoding.WebName);
        Assert.Equal(bytes, text.Encode());
    }

    [Fact]
    public void Utf32_le_bom_is_unsupported()
    {
        byte[] bytes = [0xFF, 0xFE, 0x00, 0x00, 0x3C, 0x00, 0x00, 0x00];

        CfgeditException ex = Assert.Throws<CfgeditException>(() => XmlDecoding.Decode(bytes, "doc.xml"));

        Assert.Equal("UTF-32 LE is not supported", ex.Message);
        Assert.Equal("doc.xml", ex.Path);
    }

    [Fact]
    public void Utf32_be_bom_is_unsupported()
    {
        byte[] bytes = [0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x00, 0x3C];

        CfgeditException ex = Assert.Throws<CfgeditException>(() => XmlDecoding.Decode(bytes, null));

        Assert.Equal("UTF-32 BE is not supported", ex.Message);
    }

    // ----- Patterns without a byte order mark -----

    [Fact]
    public void Utf16_le_pattern_without_a_bom_is_utf16()
    {
        byte[] bytes = Utf16Le("<?xml version=\"1.0\"?><a/>");

        SourceText text = XmlDecoding.Decode(bytes, null);

        Assert.Equal("<?xml version=\"1.0\"?><a/>", text.Text);
        Assert.False(text.HasBom);
        Assert.Equal("utf-16", text.Encoding.WebName);
        Assert.Equal(bytes, text.Encode());
    }

    [Fact]
    public void Utf16_be_pattern_without_a_bom_is_utf16_big_endian()
    {
        byte[] bytes = Utf16Be("<?xml version=\"1.0\"?><a/>");

        SourceText text = XmlDecoding.Decode(bytes, null);

        Assert.Equal("<?xml version=\"1.0\"?><a/>", text.Text);
        Assert.False(text.HasBom);
        Assert.Equal("utf-16BE", text.Encoding.WebName);
        Assert.Equal(bytes, text.Encode());
    }

    // ----- Declared encodings -----

    [Fact]
    public void Windows_1252_declaration_decodes_0x80_and_0xE9_and_round_trips()
    {
        byte[] bytes = Concat(Utf8(Windows1252Declaration + "<a>"), [0x80, 0xE9], Utf8("</a>"));

        SourceText text = XmlDecoding.Decode(bytes, null);

        Assert.Equal(Windows1252Declaration + "<a>" + (char)0x20AC + (char)0xE9 + "</a>", text.Text);
        Assert.False(text.HasBom);
        Assert.Equal("windows-1252", text.Encoding.WebName);
        Assert.Equal(bytes, text.Encode());
    }

    [Fact]
    public void Iso_8859_1_declaration_is_used()
    {
        byte[] bytes = Concat(Utf8("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?><a>"), [0xE9], Utf8("</a>"));

        SourceText text = XmlDecoding.Decode(bytes, null);

        Assert.Equal((char)0xE9, text.Text[^5]);
        Assert.Equal("iso-8859-1", text.Encoding.WebName);
        Assert.Equal(bytes, text.Encode());
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("UTF-8")]
    [InlineData("Utf-8")]
    public void Utf8_declaration_is_case_insensitive(string declared)
    {
        byte[] bytes = Utf8($"<?xml version=\"1.0\" encoding=\"{declared}\"?><a/>");

        SourceText text = XmlDecoding.Decode(bytes, null);

        Assert.Equal("utf-8", text.Encoding.WebName);
        Assert.False(text.HasBom);
        Assert.Equal(bytes, text.Encode());
    }

    [Fact]
    public void No_declaration_means_utf8()
    {
        byte[] bytes = Utf8("<a>" + (char)0xE9 + "</a>");

        SourceText text = XmlDecoding.Decode(bytes, null);

        Assert.Equal("utf-8", text.Encoding.WebName);
        Assert.Equal("<a>" + (char)0xE9 + "</a>", text.Text);
        Assert.Equal(bytes, text.Encode());
    }

    [Fact]
    public void Declaration_without_an_encoding_means_utf8()
    {
        byte[] bytes = Utf8("<?xml version=\"1.0\" standalone=\"yes\"?><a/>");

        SourceText text = XmlDecoding.Decode(bytes, null);

        Assert.Equal("utf-8", text.Encoding.WebName);
        Assert.Equal(bytes, text.Encode());
    }

    [Fact]
    public void Declaration_with_single_quotes_and_extra_whitespace_still_yields_its_encoding()
    {
        byte[] bytes = Concat(
            Utf8("<?xml\tversion = '1.0'\r\n   encoding  =  'windows-1252'  ?><a>"), [0x80], Utf8("</a>"));

        SourceText text = XmlDecoding.Decode(bytes, null);

        Assert.Equal("windows-1252", text.Encoding.WebName);
        Assert.Equal((char)0x20AC, text.Text[^5]);
        Assert.Equal(bytes, text.Encode());
    }

    [Fact]
    public void Unknown_encoding_name_is_unsupported()
    {
        byte[] bytes = Utf8("<?xml version=\"1.0\" encoding=\"x\"?><a/>");

        CfgeditException ex = Assert.Throws<CfgeditException>(() => XmlDecoding.Decode(bytes, "doc.xml"));

        Assert.Equal("unsupported encoding 'x'", ex.Message);
        Assert.Equal("doc.xml", ex.Path);
    }

    [Fact]
    public void Utf7_declaration_is_unsupported()
    {
        byte[] bytes = Utf8("<?xml version=\"1.0\" encoding=\"utf-7\"?><a/>");

        CfgeditException ex = Assert.Throws<CfgeditException>(() => XmlDecoding.Decode(bytes, null));

        Assert.Equal("unsupported encoding 'utf-7'", ex.Message);
    }

    // ----- Contradictions -----

    [Fact]
    public void Utf8_bom_with_a_windows_1252_declaration_is_a_contradiction()
    {
        byte[] bytes = Concat([0xEF, 0xBB, 0xBF], Utf8(Windows1252Declaration + "<a/>"));

        CfgeditException ex = Assert.Throws<CfgeditException>(() => XmlDecoding.Decode(bytes, null));

        Assert.Equal("the UTF-8 byte order mark contradicts the declared encoding 'windows-1252'", ex.Message);
    }

    [Fact]
    public void Utf16_le_bom_with_a_utf8_declaration_is_a_contradiction()
    {
        byte[] bytes = Concat([0xFF, 0xFE], Utf16Le("<?xml version=\"1.0\" encoding=\"utf-8\"?><a/>"));

        CfgeditException ex = Assert.Throws<CfgeditException>(() => XmlDecoding.Decode(bytes, null));

        Assert.Equal("the UTF-16 LE byte order mark contradicts the declared encoding 'utf-8'", ex.Message);
    }

    [Fact]
    public void Utf16_be_bom_with_a_windows_1252_declaration_is_a_contradiction()
    {
        byte[] bytes = Concat([0xFE, 0xFF], Utf16Be(Windows1252Declaration + "<a/>"));

        CfgeditException ex = Assert.Throws<CfgeditException>(() => XmlDecoding.Decode(bytes, null));

        Assert.Equal("the UTF-16 BE byte order mark contradicts the declared encoding 'windows-1252'", ex.Message);
    }

    [Fact]
    public void Utf16_le_pattern_with_a_windows_1252_declaration_is_a_contradiction()
    {
        byte[] bytes = Utf16Le(Windows1252Declaration + "<a/>");

        CfgeditException ex = Assert.Throws<CfgeditException>(() => XmlDecoding.Decode(bytes, null));

        Assert.Equal(
            "the UTF-16 LE pattern (no byte order mark) contradicts the declared encoding 'windows-1252'",
            ex.Message);
    }

    [Fact]
    public void Ascii_compatible_bytes_with_a_utf16_declaration_have_no_matching_bom_or_pattern()
    {
        byte[] bytes = Utf8("<?xml version=\"1.0\" encoding=\"utf-16\"?><a/>");

        CfgeditException ex = Assert.Throws<CfgeditException>(() => XmlDecoding.Decode(bytes, null));

        Assert.Equal("the declared encoding 'utf-16' has no matching byte order mark or pattern", ex.Message);
    }

    [Fact]
    public void Ascii_compatible_bytes_with_a_utf32_declaration_have_no_matching_bom_or_pattern()
    {
        byte[] bytes = Utf8("<?xml version=\"1.0\" encoding=\"UTF-32\"?><a/>");

        CfgeditException ex = Assert.Throws<CfgeditException>(() => XmlDecoding.Decode(bytes, null));

        Assert.Equal("the declared encoding 'UTF-32' has no matching byte order mark or pattern", ex.Message);
    }

    [Fact]
    public void Utf16_bom_with_a_matching_utf16_declaration_is_accepted()
    {
        byte[] bytes = Concat([0xFF, 0xFE], Utf16Le("<?xml version=\"1.0\" encoding=\"UTF-16\"?><a/>"));

        SourceText text = XmlDecoding.Decode(bytes, null);

        Assert.Equal("utf-16", text.Encoding.WebName);
        Assert.Equal(bytes, text.Encode());
    }

    // ----- Invalid bytes -----

    [Theory]
    [InlineData(0x81)]
    [InlineData(0x8D)]
    [InlineData(0x8F)]
    [InlineData(0x90)]
    [InlineData(0x9D)]
    public void Undefined_windows_1252_bytes_are_errors_with_their_byte_offset(int undefinedByte)
    {
        // The declaration is 48 bytes long, so the undefined byte is at offset 48 and the last good character is the '>'
        // of "<a>" at column 48 of line 1.
        byte[] bytes = Concat(Utf8(Windows1252Declaration + "<a>"), [(byte)undefinedByte], Utf8("</a>"));

        CfgeditException ex = Assert.Throws<CfgeditException>(() => XmlDecoding.Decode(bytes, "doc.xml"));

        Assert.Equal("not valid windows-1252 at byte 48", ex.Message);
        Assert.Equal("doc.xml", ex.Path);
        Assert.Equal(1, ex.Line);
        Assert.Equal(48, ex.Column);
    }

    [Fact]
    public void Undefined_windows_1252_byte_0x81_is_an_error_at_its_offset()
    {
        byte[] bytes = Concat(Utf8(Windows1252Declaration + "<a>"), [0x81], Utf8("</a>"));

        CfgeditException ex = Assert.Throws<CfgeditException>(() => XmlDecoding.Decode(bytes, null));

        Assert.Equal("not valid windows-1252 at byte 48", ex.Message);
    }

    [Fact]
    public void Invalid_utf8_is_an_error_at_its_byte_offset()
    {
        byte[] bytes = [0x3C, 0x61, 0x3E, 0x80, 0x3C, 0x2F, 0x61, 0x3E];

        CfgeditException ex = Assert.Throws<CfgeditException>(() => XmlDecoding.Decode(bytes, null));

        Assert.Equal("not valid UTF-8 at byte 3", ex.Message);
        Assert.Equal(1, ex.Line);
        Assert.Equal(3, ex.Column);
    }

    [Fact]
    public void Odd_byte_count_in_utf16_is_an_error()
    {
        byte[] bytes = [0xFF, 0xFE, 0x3C, 0x00, 0x61];

        CfgeditException ex = Assert.Throws<CfgeditException>(() => XmlDecoding.Decode(bytes, null));

        Assert.Equal("not valid UTF-16 LE at byte 4: odd number of bytes", ex.Message);
    }

    [Fact]
    public void Unpaired_surrogate_in_utf16_is_an_error_at_its_byte_offset()
    {
        // The lone high surrogate 0xD800 is the unit at bytes 4 and 5.
        byte[] bytes = [0xFF, 0xFE, 0x61, 0x00, 0x00, 0xD8, 0x62, 0x00];

        CfgeditException ex = Assert.Throws<CfgeditException>(() => XmlDecoding.Decode(bytes, null));

        Assert.Equal("not valid UTF-16 LE at byte 4: unpaired surrogate", ex.Message);
    }

    // ----- Round-trip guard -----

    [Fact]
    public void Iso_2022_jp_with_a_canonical_escape_round_trips()
    {
        // ESC $ B selects JIS X 0208, and ESC ( B returns to ASCII. U+4E9C is 0x3021 in that set.
        byte[] bytes = Concat(
            Utf8("<?xml version=\"1.0\" encoding=\"iso-2022-jp\"?><a>"),
            [0x1B, 0x24, 0x42, 0x30, 0x21, 0x1B, 0x28, 0x42],
            Utf8("</a>"));

        SourceText text = XmlDecoding.Decode(bytes, null);

        Assert.Equal("iso-2022-jp", text.Encoding.WebName);
        Assert.Equal((char)0x4E9C, text.Text[^5]);
        Assert.Equal(bytes, text.Encode());
    }

    [Fact]
    public void Iso_2022_jp_with_a_non_canonical_escape_fails_the_round_trip_guard()
    {
        // ESC $ @ is the older JIS X 0208-1978 escape. The same character encodes back with ESC $ B, so the bytes would
        // change on save.
        byte[] bytes = Concat(
            Utf8("<?xml version=\"1.0\" encoding=\"iso-2022-jp\"?><a>"),
            [0x1B, 0x24, 0x40, 0x30, 0x21, 0x1B, 0x28, 0x42],
            Utf8("</a>"));

        CfgeditException ex = Assert.Throws<CfgeditException>(() => XmlDecoding.Decode(bytes, "doc.xml"));

        Assert.Equal(
            "the file's bytes do not round-trip through encoding iso-2022-jp; cfgedit cannot edit it safely",
            ex.Message);
        Assert.Equal("doc.xml", ex.Path);
    }

    // ----- Cross-check with XmlTextReader -----

    public static TheoryData<string, byte[]> DecodableFixtures()
    {
        return new TheoryData<string, byte[]>
        {
            { "utf-8 bom", Concat([0xEF, 0xBB, 0xBF], Utf8("<a/>")) },
            { "utf-8 declared", Utf8("<?xml version=\"1.0\" encoding=\"utf-8\"?><a/>") },
            { "utf-8 undeclared", Utf8("<a>" + (char)0xE9 + "</a>") },
            { "utf-16 le bom", Concat([0xFF, 0xFE], Utf16Le("<?xml version=\"1.0\"?><a/>")) },
            { "utf-16 be bom", Concat([0xFE, 0xFF], Utf16Be("<?xml version=\"1.0\"?><a/>")) },
            { "utf-16 le pattern", Utf16Le("<?xml version=\"1.0\"?><a/>") },
            { "utf-16 be pattern", Utf16Be("<?xml version=\"1.0\"?><a/>") },
            { "windows-1252", Concat(Utf8(Windows1252Declaration + "<a>"), [0x80, 0xE9], Utf8("</a>")) },
            { "iso-8859-1", Concat(Utf8("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?><a>"), [0xE9], Utf8("</a>")) },
            {
                "single quotes",
                Concat(Utf8("<?xml version='1.0' encoding='windows-1252'?><a>"), [0x80], Utf8("</a>"))
            },
            {
                "iso-2022-jp",
                Concat(
                    Utf8("<?xml version=\"1.0\" encoding=\"iso-2022-jp\"?><a>"),
                    [0x1B, 0x24, 0x42, 0x30, 0x21, 0x1B, 0x28, 0x42],
                    Utf8("</a>"))
            },
        };
    }

    [Theory]
    [MemberData(nameof(DecodableFixtures))]
    public void Encoding_cross_check_agrees_for_every_decodable_fixture(string name, byte[] bytes)
    {
        SourceText decoded = XmlDecoding.Decode(bytes, null);

        Assert.True(decoded.Text.Length > 0, name);
        XmlDecoding.CheckEncodingAgreesWithXmlReader(bytes, decoded);
    }

    [Fact]
    public void Encoding_cross_check_reports_a_disagreement()
    {
        // Seam: the reader sees no declaration and reports utf-8, so a text decoded as UTF-16 must be reported.
        byte[] bytes = Utf8("<a/>");
        SourceText decodedAsUtf16 = SourceText.CreateStrict("<a/>", new UnicodeEncoding(false, false, true), hasBom: false);

        CfgeditException ex = Assert.Throws<CfgeditException>(
            () => XmlDecoding.CheckEncodingAgreesWithXmlReader(bytes, decodedAsUtf16));

        Assert.Equal("the XML reader reads the encoding as 'utf-8' but cfgedit decoded it as 'utf-16'", ex.Message);
    }

    [Fact]
    public void Encoding_cross_check_passes_when_the_first_node_is_malformed()
    {
        // The reader fails on its first read, so there is no encoding to compare. The full parse reports the error.
        byte[] bytes = Utf8("<?xml version=\"1.0\" encoding=\"x\"?><a/>");
        SourceText decodedAsUtf16 = SourceText.CreateStrict("<a/>", new UnicodeEncoding(false, false, true), hasBom: false);

        XmlDecoding.CheckEncodingAgreesWithXmlReader(bytes, decodedAsUtf16);
    }
}
