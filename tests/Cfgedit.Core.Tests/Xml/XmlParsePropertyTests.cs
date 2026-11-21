using System.Text;
using Cfgedit.Core.Xml;
using static Cfgedit.Core.Tests.Xml.XmlTestSupport;

namespace Cfgedit.Core.Tests.Xml;

/// <summary>
/// Values through the full parse pipeline, including the cross-check against XmlReader. Each encoder's output is put in a
/// document, parsed, and the value read back must equal the input.
/// </summary>
public sealed class XmlParsePropertyTests
{
    private static readonly string[] Newlines = ["\n", "\r\n"];

    private static readonly string[] Quotes = ["\"", "'"];

    /// <summary>
    /// A document with the given line break. The declaration is followed by the line break too, so a CRLF document has CRLF
    /// line breaks outside the value as well.
    /// </summary>
    private static string Document(string newline, string root)
    {
        return "<?xml version=\"1.0\"?>" + newline + root + newline;
    }

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

    // A copy of the generator in XmlSyntaxTests, with the same seed and the same characters, so that both suites test the same strings.
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

    [Fact]
    public void Attribute_values_come_back_unchanged_through_the_full_parse_with_both_quotes_and_newlines()
    {
        int checkedCount = 0;
        foreach (string newline in Newlines)
        {
            foreach (string quote in Quotes)
            {
                char q = quote[0];
                foreach (string value in ValidValues())
                {
                    string xml = Document(newline, "<r a=" + quote + XmlSyntax.EncodeAttributeValue(value, q) + quote + "/>");

                    XmlDocumentSyntax doc = XmlTestSupport.Parse(xml);

                    XmlAttributeSyntax attribute = Assert.Single(doc.Root.Attributes);
                    Assert.Equal("a", attribute.Name);
                    Assert.Equal(q, attribute.Quote);
                    Assert.Equal(value, attribute.Value);
                    checkedCount++;
                }
            }
        }

        Assert.True(checkedCount > 2000);
    }

    [Fact]
    public void Text_values_come_back_unchanged_through_the_full_parse_with_both_newline_styles()
    {
        int checkedCount = 0;
        foreach (string newline in Newlines)
        {
            foreach (string value in ValidValues())
            {
                string xml = Document(newline, "<r>" + XmlSyntax.EncodeText(value, newline) + "</r>");

                XmlDocumentSyntax doc = XmlTestSupport.Parse(xml);

                if (value.Length == 0)
                {
                    Assert.Empty(doc.Root.Children);
                }
                else
                {
                    XmlTextSyntax text = Assert.IsType<XmlTextSyntax>(Assert.Single(doc.Root.Children));
                    Assert.Equal(value, text.Value);
                }

                checkedCount++;
            }
        }

        Assert.True(checkedCount > 2000);
    }

    [Fact]
    public void Cdata_values_come_back_unchanged_through_the_full_parse_and_cr_values_encode_to_null()
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

                string xml = Document(newline, "<r><![CDATA[" + encoded + "]]></r>");

                XmlDocumentSyntax doc = XmlTestSupport.Parse(xml);

                var content = new StringBuilder();
                foreach (XmlNodeSyntax child in doc.Root.Children)
                {
                    content.Append(Assert.IsType<XmlCDataSyntax>(child).Content);
                }

                Assert.Equal(value, content.ToString());
                checkedCount++;
            }
        }

        Assert.True(checkedCount > 1000);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Empty_values_make_an_empty_attribute_an_empty_element_and_one_empty_cdata(string newline)
    {
        XmlDocumentSyntax attribute = XmlTestSupport.Parse(Document(newline, "<r a=\"\"/>"));
        Assert.Equal(string.Empty, Assert.Single(attribute.Root.Attributes).Value);

        XmlDocumentSyntax empty = XmlTestSupport.Parse(Document(newline, "<r></r>"));
        Assert.Empty(empty.Root.Children);
        Assert.False(empty.Root.IsEmpty);

        XmlDocumentSyntax cdata = XmlTestSupport.Parse(Document(newline, "<r><![CDATA[]]></r>"));
        Assert.Equal(string.Empty, Assert.IsType<XmlCDataSyntax>(Assert.Single(cdata.Root.Children)).Content);
    }

    [Fact]
    public void Crlf_document_with_a_value_holding_crlf_reads_each_crlf_as_one_line_break()
    {
        // The encoder writes the value's LF as CRLF here, and the reader normalises it back to LF.
        string xml = Document("\r\n", "<r>" + XmlSyntax.EncodeText("one\ntwo", "\r\n") + "</r>");

        XmlDocumentSyntax doc = XmlTestSupport.Parse(xml);

        Assert.Equal("one\ntwo", Assert.IsType<XmlTextSyntax>(Assert.Single(doc.Root.Children)).Value);
        AssertAt(doc, doc.Root.NameStart, 2, 2);
    }
}
