using System.Text;
using System.Xml;
using Cfgedit.Core.Xml;

namespace Cfgedit.Core.Tests.Xml;

public sealed class XmlSyntaxTests
{
    /// <summary>Builds a string from UTF-16 code units, so that surrogates and noncharacters can be written as numbers.</summary>
    private static string Units(params int[] units)
    {
        return new string(units.Select(u => (char)u).ToArray());
    }

    // ----- FindInvalidChar -----

    [Fact]
    public void Empty_and_ordinary_text_have_no_invalid_char()
    {
        Assert.Equal(-1, XmlSyntax.FindInvalidChar(""));
        Assert.Equal(-1, XmlSyntax.FindInvalidChar("a\tb\nc\rd \"'<>&"));
    }

    [Theory]
    [InlineData(0x00, 0)]
    [InlineData(0x01, 0)]
    [InlineData(0x0B, 0)]
    [InlineData(0x0C, 0)]
    [InlineData(0x1F, 0)]
    public void C0_controls_other_than_tab_lf_and_cr_are_invalid(int control, int expectedIndex)
    {
        Assert.Equal(expectedIndex, XmlSyntax.FindInvalidChar(Units(control)));
        Assert.Equal(2, XmlSyntax.FindInvalidChar(Units(0x61, 0x62, control)));
    }

    [Theory]
    [InlineData(0xFFFE)]
    [InlineData(0xFFFF)]
    public void Noncharacters_fffe_and_ffff_are_invalid(int noncharacter)
    {
        Assert.Equal(1, XmlSyntax.FindInvalidChar(Units(0x61, noncharacter)));
    }

    [Fact]
    public void A_lone_high_surrogate_is_invalid()
    {
        Assert.Equal(0, XmlSyntax.FindInvalidChar(Units(0xD800)));
        Assert.Equal(0, XmlSyntax.FindInvalidChar(Units(0xD800, 0x61)));
    }

    [Fact]
    public void A_lone_low_surrogate_is_invalid()
    {
        Assert.Equal(1, XmlSyntax.FindInvalidChar(Units(0x61, 0xDC00)));
    }

    [Fact]
    public void A_valid_surrogate_pair_is_allowed_and_an_invalid_char_after_it_is_found()
    {
        // U+1F600 is D83D DE00 and U+10FFFF is DBFF DFFF.
        Assert.Equal(-1, XmlSyntax.FindInvalidChar(Units(0xD83D, 0xDE00)));
        Assert.Equal(-1, XmlSyntax.FindInvalidChar(Units(0xDBFF, 0xDFFF)));
        Assert.Equal(2, XmlSyntax.FindInvalidChar(Units(0xD83D, 0xDE00, 0x01)));
    }

    [Theory]
    [InlineData(0x0085)]
    [InlineData(0x007F)]
    [InlineData(0x009F)]
    [InlineData(0xFFFD)]
    [InlineData(0xE000)]
    [InlineData(0x2028)]
    public void Characters_that_xml_1_0_allows_are_valid(int allowed)
    {
        Assert.Equal(-1, XmlSyntax.FindInvalidChar(Units(allowed)));
    }

    // ----- EncodeAttributeValue -----

    [Fact]
    public void Attribute_value_with_double_quotes_escapes_only_the_quote_in_use()
    {
        Assert.Equal("a&quot;b'c", XmlSyntax.EncodeAttributeValue("a\"b'c", '"'));
    }

    [Fact]
    public void Attribute_value_with_single_quotes_escapes_only_the_quote_in_use()
    {
        Assert.Equal("a\"b&apos;c", XmlSyntax.EncodeAttributeValue("a\"b'c", '\''));
    }

    [Fact]
    public void Attribute_value_escapes_ampersand_and_less_than_and_leaves_greater_than()
    {
        Assert.Equal("&amp;&lt;>", XmlSyntax.EncodeAttributeValue("&<>", '"'));
    }

    [Fact]
    public void Attribute_value_writes_tab_lf_and_cr_as_character_references()
    {
        Assert.Equal("a&#9;b&#10;c&#13;", XmlSyntax.EncodeAttributeValue("a\tb\nc\r", '"'));
    }

    [Fact]
    public void Attribute_value_with_the_other_quote_is_literal()
    {
        Assert.Equal("it's", XmlSyntax.EncodeAttributeValue("it's", '"'));
        Assert.Equal("say \"hi\"", XmlSyntax.EncodeAttributeValue("say \"hi\"", '\''));
    }

    [Fact]
    public void Attribute_value_rejects_a_quote_that_is_not_a_quote()
    {
        Assert.Throws<ArgumentException>(() => XmlSyntax.EncodeAttributeValue("a", 'x'));
    }

    [Fact]
    public void Attribute_value_rejects_a_character_xml_cannot_hold()
    {
        Assert.Throws<ArgumentException>(() => XmlSyntax.EncodeAttributeValue(Units(0x01), '"'));
    }

    // ----- EncodeText -----

    [Fact]
    public void Text_escapes_ampersand_and_less_than()
    {
        Assert.Equal("&amp;&lt;", XmlSyntax.EncodeText("&<", "\n"));
    }

    [Fact]
    public void Text_escapes_greater_than_only_directly_after_two_closing_brackets()
    {
        Assert.Equal("]]&gt;", XmlSyntax.EncodeText("]]>", "\n"));
        Assert.Equal("a]]&gt;b", XmlSyntax.EncodeText("a]]>b", "\n"));
        Assert.Equal("]]]&gt;", XmlSyntax.EncodeText("]]]>", "\n"));
        Assert.Equal("]]x>", XmlSyntax.EncodeText("]]x>", "\n"));
        Assert.Equal("]]", XmlSyntax.EncodeText("]]", "\n"));
    }

    [Fact]
    public void Text_writes_cr_as_a_character_reference_and_lf_as_the_newline()
    {
        Assert.Equal("a&#13;\nb", XmlSyntax.EncodeText("a\r\nb", "\n"));
        Assert.Equal("a&#13;\r\nb\r\nc", XmlSyntax.EncodeText("a\r\nb\nc", "\r\n"));
        Assert.Equal("a\rb", XmlSyntax.EncodeText("a\nb", "\r"));
    }

    [Fact]
    public void Text_keeps_tab_and_quotes()
    {
        Assert.Equal("a\tb\"'", XmlSyntax.EncodeText("a\tb\"'", "\n"));
    }

    [Fact]
    public void Text_rejects_a_newline_that_is_not_a_line_break()
    {
        Assert.Throws<ArgumentException>(() => XmlSyntax.EncodeText("a", "\n\n"));
    }

    [Fact]
    public void Text_rejects_a_character_xml_cannot_hold()
    {
        Assert.Throws<ArgumentException>(() => XmlSyntax.EncodeText(Units(0xFFFE), "\n"));
    }

    // ----- EncodeCData -----

    [Fact]
    public void Cdata_splits_each_closing_sequence()
    {
        Assert.Equal("a]]]]><![CDATA[>b", XmlSyntax.EncodeCData("a]]>b", "\n"));
        Assert.Equal("]]]]><![CDATA[>", XmlSyntax.EncodeCData("]]>", "\n"));
    }

    [Fact]
    public void Cdata_leaves_a_bracket_without_a_greater_than_alone()
    {
        Assert.Equal("]]", XmlSyntax.EncodeCData("]]", "\n"));
        Assert.Equal("a]b]]c", XmlSyntax.EncodeCData("a]b]]c", "\n"));
    }

    [Fact]
    public void Cdata_writes_lf_as_the_newline()
    {
        Assert.Equal("a\r\nb", XmlSyntax.EncodeCData("a\nb", "\r\n"));
        Assert.Equal("a\nb", XmlSyntax.EncodeCData("a\nb", "\n"));
    }

    [Fact]
    public void Cdata_of_an_empty_value_is_empty()
    {
        Assert.Equal("", XmlSyntax.EncodeCData("", "\n"));
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("a\rb")]
    [InlineData("a\r\nb")]
    public void Cdata_with_a_carriage_return_is_null(string value)
    {
        Assert.Null(XmlSyntax.EncodeCData(value, "\n"));
    }

    // ----- EncodeComment -----

    [Theory]
    [InlineData("")]
    [InlineData("a-b")]
    [InlineData("-a")]
    [InlineData("a - b")]
    public void Comment_without_double_hyphen_or_trailing_hyphen_is_unchanged(string value)
    {
        Assert.Equal(value, XmlSyntax.EncodeComment(value));
    }

    [Theory]
    [InlineData("a--b")]
    [InlineData("--")]
    [InlineData("a---")]
    [InlineData("a-")]
    [InlineData("-")]
    public void Comment_with_double_hyphen_or_trailing_hyphen_is_null(string value)
    {
        Assert.Null(XmlSyntax.EncodeComment(value));
    }

    [Fact]
    public void Comment_rejects_a_character_xml_cannot_hold()
    {
        Assert.Throws<ArgumentException>(() => XmlSyntax.EncodeComment(Units(0x00)));
    }

    // ----- Names -----

    [Theory]
    [InlineData("a:b", true)]
    [InlineData(":a", true)]
    [InlineData("a:", true)]
    [InlineData("a:b:c", true)]
    [InlineData("xml-name", true)]
    [InlineData("1a", false)]
    [InlineData("_a", true)]
    [InlineData("", false)]
    [InlineData("a b", false)]
    public void Xml_name_edge_cases(string name, bool expected)
    {
        Assert.Equal(expected, XmlSyntax.IsValidName(name));
    }

    [Theory]
    [InlineData("a:b", true)]
    [InlineData(":a", false)]
    [InlineData("a:", false)]
    [InlineData("a:b:c", false)]
    [InlineData("xml-name", true)]
    [InlineData("1a", false)]
    [InlineData("_a", true)]
    [InlineData("", false)]
    [InlineData("a::b", false)]
    public void Qualified_name_edge_cases(string name, bool expected)
    {
        Assert.Equal(expected, XmlSyntax.IsValidQName(name));
    }

    [Fact]
    public void Names_accept_non_ascii_letters()
    {
        string name = Units(0xE9);

        Assert.True(XmlSyntax.IsValidName(name));
        Assert.True(XmlSyntax.IsValidQName(name));
        Assert.True(XmlSyntax.IsValidQName("p:" + name));
    }

    [Fact]
    public void Names_reject_lone_surrogates()
    {
        Assert.False(XmlSyntax.IsValidName(Units(0x61, 0xD800)));
        Assert.False(XmlSyntax.IsValidQName(Units(0x61, 0xD800)));
    }

    // ----- XmlReader round trip -----

    private static readonly string[] Newlines = ["\n", "\r\n"];

    private static readonly string[] Quotes = ["\"", "'"];

    /// <summary>NastyStrings that XML 1.0 can hold, followed by 1,000 seeded random valid strings.</summary>
    private static IEnumerable<string> ValidValues()
    {
        foreach (string value in NastyStrings.All)
        {
            if (XmlSyntax.FindInvalidChar(value) < 0)
            {
                yield return value;
            }
        }

        var random = new Random(20260314);
        for (int i = 0; i < 1000; i++)
        {
            yield return RandomValid(random);
        }
    }

    private static readonly string[] Specials =
    [
        "]]>", "]]", "]", ">", "&", "<", "\"", "'", "\t", "\n", "\r", "\r\n", "--", "-", "&amp;", "<![CDATA[",
        new string((char)0x0085, 1),
    ];

    private static string RandomValid(Random random)
    {
        var sb = new StringBuilder();
        int length = random.Next(0, 40);
        for (int i = 0; i < length; i++)
        {
            if (random.Next(3) == 0)
            {
                sb.Append(Specials[random.Next(Specials.Length)]);
                continue;
            }

            int codePoint;
            do
            {
                codePoint = random.Next(4) switch
                {
                    0 => random.Next(0x20, 0x7F),
                    1 => random.Next(0x80, 0xD800),
                    2 => random.Next(0xE000, 0xFFFE),
                    _ => random.Next(0x10000, 0x110000),
                };
            }
            while (codePoint is >= 0xD800 and <= 0xDFFF);

            sb.Append(char.ConvertFromUtf32(codePoint));
        }

        return sb.ToString();
    }

    /// <summary>The attribute value read back by XmlReader for <c>&lt;r a=Q...Q/&gt;</c>.</summary>
    private static string ReadAttribute(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml));
        reader.MoveToContent();
        return reader.GetAttribute("a") ?? throw new InvalidOperationException("No attribute a.");
    }

    /// <summary>The text and CDATA of the root element, joined, as XmlReader reads them.</summary>
    private static string ReadContent(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml));
        reader.MoveToContent();
        var sb = new StringBuilder();
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    sb.Append(reader.Value);
                    break;
            }
        }

        return sb.ToString();
    }

    [Fact]
    public void Attribute_values_round_trip_through_xmlreader_with_both_quotes()
    {
        int checkedCount = 0;
        foreach (string quote in Quotes)
        {
            char q = quote[0];
            foreach (string value in ValidValues())
            {
                string xml = "<r a=" + quote + XmlSyntax.EncodeAttributeValue(value, q) + quote + "/>";

                Assert.Equal(value, ReadAttribute(xml));
                checkedCount++;
            }
        }

        Assert.True(checkedCount > 2000);
    }

    [Fact]
    public void Text_round_trips_through_xmlreader_with_both_newline_styles()
    {
        int checkedCount = 0;
        foreach (string newline in Newlines)
        {
            foreach (string value in ValidValues())
            {
                string xml = "<r>" + XmlSyntax.EncodeText(value, newline) + "</r>";

                Assert.Equal(value, ReadContent(xml));
                checkedCount++;
            }
        }

        Assert.True(checkedCount > 2000);
    }

    [Fact]
    public void Cdata_round_trips_through_xmlreader_with_both_newline_styles_and_cr_values_are_null()
    {
        int checkedCount = 0;
        foreach (string newline in Newlines)
        {
            foreach (string value in ValidValues())
            {
                string? encoded = XmlSyntax.EncodeCData(value, newline);
                if (value.Contains('\r'))
                {
                    Assert.Null(encoded);
                    continue;
                }

                string xml = "<r><![CDATA[" + encoded + "]]></r>";

                Assert.Equal(value, ReadContent(xml));
                checkedCount++;
            }
        }

        Assert.True(checkedCount > 1000);
    }
}
