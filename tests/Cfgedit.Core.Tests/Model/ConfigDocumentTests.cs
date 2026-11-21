using System.Text;
using Cfgedit.Core;
using Cfgedit.Core.Json;
using Cfgedit.Core.Model;
using Cfgedit.Core.Text;
using Cfgedit.Core.Xml;

namespace Cfgedit.Core.Tests.Model;

/// <summary>Loading documents from bytes and text, and the members that are not implemented before the edit milestone.</summary>
public sealed class ConfigDocumentTests
{
    private static readonly IConfigFormat StrictJson = Formats.FromName("json")!;
    private static readonly IConfigFormat Xml = Formats.FromName("xml")!;

    /// <summary>A test operation, to pass to the members that take an edit.</summary>
    private sealed record TestOp : EditOp;

    [Fact]
    public void Json_bytes_with_a_utf8_bom_load_with_the_bom_recorded_and_stripped()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("{\"a\": 1}")];

        ConfigDocument document = ConfigDocument.Load(bytes, "app.json", StrictJson);

        Assert.True(document.Source.HasBom);
        Assert.Equal("{\"a\": 1}", document.Source.Text);
        Assert.IsType<JsonDocument>(document.Syntax);
        string expected = """
            Document / [0,8)
              JsonObject /Item(0) [0,8) json=Object Summary"1"[0,1)-
                JsonScalar /Item(0)/Member("a", 0) [1,7) json=Number Key"a"[1,4)+ Value"1"[6,7)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Json_bytes_in_utf16_with_a_bom_decode_through_the_bom()
    {
        byte[] bytes = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("[true]")];

        ConfigDocument document = ConfigDocument.Load(bytes, null, StrictJson);

        Assert.Equal("utf-16", document.Source.Encoding.WebName);
        string expected = """
            Document / [0,6)
              JsonArray /Item(0) [0,6) json=Array Summary"1"[0,1)-
                JsonScalar /Item(0)/Item(0) [1,5) json=True Index"0"[1,1)- Value"true"[1,5)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Xml_bytes_in_utf16_with_a_bom_load_through_the_xml_decoder()
    {
        byte[] bytes = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("<r>x</r>")];

        ConfigDocument document = ConfigDocument.Load(bytes, "r.xml", Xml);

        Assert.IsType<XmlDocumentSyntax>(document.Syntax);
        Assert.Equal("utf-16", document.Source.Encoding.WebName);
        string expected = """
            Document / [0,8)
              XmlElement /Element("r", 0) [0,8) ElementName"r"[1,2)+ Text"x"[3,4)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Loading_bytes_and_parsing_the_same_text_give_the_same_tree()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("<a x=\"1\"><b>t</b><!--c--></a>");

        ConfigDocument fromBytes = ConfigDocument.Load(bytes, "a.xml", Xml);
        ConfigDocument fromText = ConfigDocument.Parse(SourceText.Create("<a x=\"1\"><b>t</b><!--c--></a>", Encoding.UTF8, false), Xml);

        Assert.Equal(ModelTestSupport.Dump(fromText.Root), ModelTestSupport.Dump(fromBytes.Root));
        Assert.Same(Xml, fromBytes.Format);
    }

    [Fact]
    public void Undecodable_bytes_raise_an_error_that_names_the_file()
    {
        byte[] bytes = [0x7B, 0xFF, 0x7D];

        CfgeditException error = Assert.Throws<CfgeditException>(() => ConfigDocument.Load(bytes, "bad.json", StrictJson));

        Assert.Equal("bad.json", error.Path);
    }

    [Fact]
    public void Text_that_is_not_valid_JSON_raises_a_parse_error()
    {
        Assert.Throws<CfgeditException>(() => ConfigDocument.Load("{"u8, "x.json", StrictJson));
    }

    [Fact]
    public void Text_that_is_not_well_formed_XML_raises_a_parse_error()
    {
        Assert.Throws<CfgeditException>(() => ConfigDocument.Load("<r>"u8, "x.xml", Xml));
    }

    [Fact]
    public void Apply_is_not_implemented_yet()
    {
        ConfigDocument document = ModelTestSupport.Parse("[1]", StrictJson);

        Assert.Throws<NotImplementedException>(() => document.Apply(new TestOp()));
    }

    [Fact]
    public void Plan_is_not_implemented_yet_in_either_format()
    {
        ConfigDocument json = ModelTestSupport.Parse("[1]", StrictJson);
        ConfigDocument xml = ModelTestSupport.Parse("<r/>", Xml);

        Assert.Throws<NotImplementedException>(() => StrictJson.Plan(json, new TestOp()));
        Assert.Throws<NotImplementedException>(() => Xml.Plan(xml, new TestOp()));
    }

    [Fact]
    public void To_logical_refuses_a_document_of_the_other_family()
    {
        ConfigDocument json = ModelTestSupport.Parse("[1]", StrictJson);

        Assert.Throws<ArgumentException>(() => Xml.ToLogical(json));
    }

    [Fact]
    public void Resolve_returns_null_when_a_step_is_missing_below_an_existing_node()
    {
        ConfigDocument document = ModelTestSupport.Parse("{\"a\": 1}", StrictJson);

        Assert.Null(document.Resolve(new NodePath([new ItemStep(0), new MemberStep("b", 0)])));
    }
}
