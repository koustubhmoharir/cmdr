using Cfgedit.Core.Model;

namespace Cfgedit.Core.Tests.Model;

/// <summary>The shape of the XML document tree: kinds, parts, spans, end rows, children and paths.</summary>
public sealed class XmlTreeTests
{
    private static readonly IConfigFormat Xml = Formats.FromName("xml")!;

    [Fact]
    public void Declaration_and_text_only_elements_are_shaped_as_described()
    {
        // Text-only: b (empty, <b></b>), c (text x), d (whitespace-only text). Self-closing: a. The root has element children,
        // so it has an end row.
        const string text = "<?xml version=\"1.0\"?>\n<r><a/><b></b><c>x</c><d> </d></r>";
        ConfigDocument document = ModelTestSupport.Parse(text, Xml);

        string expected = """
            Document / [0,56)
              XmlDeclaration /Declaration [0,21) ReadOnly"<?xml version="1.0"?>"[0,21)-
              XmlElement /Element("r", 0) [22,56) ElementName"r"[23,24)+ end:EndName"r"[54,55)+
                XmlElement /Element("r", 0)/Element("a", 0) [25,29) ElementName"a"[26,27)+
                XmlElement /Element("r", 0)/Element("b", 0) [29,36) ElementName"b"[30,31)+ Text""[32,32)+
                XmlElement /Element("r", 0)/Element("c", 0) [36,44) ElementName"c"[37,38)+ Text"x"[39,40)+
                XmlElement /Element("r", 0)/Element("d", 0) [44,52) ElementName"d"[45,46)+ Text" "[47,48)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Mixed_content_has_text_comment_pi_cdata_and_same_name_siblings_numbered_per_name()
    {
        // Two x elements: their ordinals are 0 and 1. The text runs are numbered 0, 1 and 2 (CDATA included). The whitespace
        // before the end tag is not a node.
        const string text = "<r>a<!--c--><x/>b<x/><![CDATA[d]]><?p  q  ?> </r>";
        ConfigDocument document = ModelTestSupport.Parse(text, Xml);

        string expected = """
            Document / [0,49)
              XmlElement /Element("r", 0) [0,49) ElementName"r"[1,2)+ end:EndName"r"[47,48)+
                XmlText /Element("r", 0)/Text(0) [3,4) Text"a"[3,4)+
                XmlComment /Element("r", 0)/Comment(0) [4,12) Comment"c"[8,9)+
                XmlElement /Element("r", 0)/Element("x", 0) [12,16) ElementName"x"[13,14)+
                XmlText /Element("r", 0)/Text(1) [16,17) Text"b"[16,17)+
                XmlElement /Element("r", 0)/Element("x", 1) [17,21) ElementName"x"[18,19)+
                XmlCData /Element("r", 0)/Text(2) [21,34) Text"d"[30,31)+
                XmlProcessingInstruction /Element("r", 0)/PI(0) [34,44) PITarget"p"[36,37)+ PIData"q  "[39,42)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Self_closing_element_has_no_children_and_no_end_row()
    {
        ConfigDocument document = ModelTestSupport.Parse("<e a=\"1\" b='x&amp;y'/>", Xml);

        string expected = """
            Document / [0,22)
              XmlElement /Element("e", 0) [0,22) ElementName"e"[1,2)+ AttributeName#0"a"[3,4)+ AttributeValue#0"1"[6,7)+ AttributeName#1"b"[9,10)+ AttributeValue#1"x&y"[12,19)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Empty_container_written_across_lines_has_an_end_row_and_no_text_part()
    {
        ConfigDocument document = ModelTestSupport.Parse("<s>\r\n  </s>", Xml);

        string expected = """
            Document / [0,11)
              XmlElement /Element("s", 0) [0,11) ElementName"s"[1,2)+ end:EndName"s"[9,10)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Element_with_only_spaces_on_one_line_is_text_only()
    {
        ConfigDocument document = ModelTestSupport.Parse("<s>  </s>", Xml);

        string expected = """
            Document / [0,9)
              XmlElement /Element("s", 0) [0,9) ElementName"s"[1,2)+ Text"  "[3,5)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Element_with_only_a_cdata_child_is_not_text_only()
    {
        ConfigDocument document = ModelTestSupport.Parse("<c><![CDATA[x]]></c>", Xml);

        string expected = """
            Document / [0,20)
              XmlElement /Element("c", 0) [0,20) ElementName"c"[1,2)+ end:EndName"c"[18,19)+
                XmlCData /Element("c", 0)/Text(0) [3,16) Text"x"[12,13)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Whitespace_between_markup_is_not_a_node_and_the_end_row_follows_the_children()
    {
        ConfigDocument document = ModelTestSupport.Parse("<r>\n  <a/>\n</r>", Xml);

        string expected = """
            Document / [0,15)
              XmlElement /Element("r", 0) [0,15) ElementName"r"[1,2)+ end:EndName"r"[13,14)+
                XmlElement /Element("r", 0)/Element("a", 0) [6,10) ElementName"a"[7,8)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Doctype_and_declaration_are_read_only_and_the_doctype_is_a_document_child()
    {
        const string text = "<!DOCTYPE r [<!ENTITY e \"v\">]><r>&e;</r>";
        ConfigDocument document = ModelTestSupport.Parse(text, Xml);

        // The entity is custom, so the text is raw and the root is text-only: no end row, and the text is edited as source.
        string expected = """
            Document / [0,40)
              XmlDoctype /Doctype [0,30) ReadOnly"<!DOCTYPE r [<!ENTITY e "v">]>"[0,30)-
              XmlElement /Element("r", 0) [30,40) ElementName"r"[31,32)+ Text"&e;"[33,36)+raw
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Doctype_is_a_read_only_node_with_its_source_text()
    {
        ConfigDocument document = ModelTestSupport.Parse("<!DOCTYPE r [<!ELEMENT r ANY>]><r/>", Xml);

        DocNode doctype = document.Root.Children[0];
        Assert.Equal(NodeKind.XmlDoctype, doctype.Kind);
        Assert.Equal(PartKind.ReadOnly, Assert.Single(doctype.Parts).Kind);
        Assert.Equal("<!DOCTYPE r [<!ELEMENT r ANY>]>", doctype.Parts[0].Value);
        Assert.False(doctype.Parts[0].Editable);
        Assert.Empty(doctype.Children);
    }

    [Fact]
    public void Text_node_paths_count_non_whitespace_text_and_cdata_together()
    {
        ConfigDocument document = ModelTestSupport.Parse("<r> x <![CDATA[y]]> <?t?></r>", Xml);

        DocNode r = document.Root.Children[0];
        Assert.Equal(2, r.Children.Count(child => child.Kind is NodeKind.XmlText or NodeKind.XmlCData));
        Assert.Equal("/Element(\"r\", 0)/Text(0)", r.Children[0].Path.ToString());
        Assert.Equal("/Element(\"r\", 0)/Text(1)", r.Children[1].Path.ToString());
        Assert.Equal("/Element(\"r\", 0)/PI(0)", r.Children[2].Path.ToString());
    }
}
