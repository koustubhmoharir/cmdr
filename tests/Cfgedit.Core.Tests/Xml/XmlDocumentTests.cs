using System.Text;
using Cfgedit.Core.Xml;
using Shared.Core;
using static Cfgedit.Core.Tests.Xml.XmlTestSupport;

namespace Cfgedit.Core.Tests.Xml;

public sealed class XmlDocumentTests
{
    // ----- Edge cases: the shape of the smallest documents -----

    [Fact]
    public void A_document_with_only_a_root_has_no_declaration_prolog_or_epilog()
    {
        XmlDocumentSyntax doc = Parse("<r/>");

        Assert.Null(doc.Declaration);
        Assert.Empty(doc.Prolog);
        Assert.Empty(doc.Epilog);
        Assert.Equal("r", doc.Root.Name);
        Assert.Equal(0, doc.Root.Start);
        Assert.Equal(4, doc.Root.End);
    }

    [Fact]
    public void Self_closing_element_without_a_space_is_empty_with_no_end_tag()
    {
        XmlElementSyntax root = Parse("<a/>").Root;

        Assert.True(root.IsEmpty);
        Assert.Null(root.EndTag);
        Assert.Empty(root.Children);
        Assert.Equal(4, root.StartTagEnd);
        Assert.Equal(1, root.NameStart);
    }

    [Fact]
    public void Self_closing_element_with_a_space_keeps_the_space_inside_the_start_tag()
    {
        XmlDocumentSyntax doc = Parse("<a />");

        Assert.True(doc.Root.IsEmpty);
        Assert.Null(doc.Root.EndTag);
        Assert.Equal("<a />", Slice(doc, doc.Root));
        Assert.Equal(5, doc.Root.StartTagEnd);
    }

    [Fact]
    public void Element_with_an_empty_end_tag_is_not_empty_and_has_an_end_tag()
    {
        XmlDocumentSyntax doc = Parse("<a></a>");

        Assert.False(doc.Root.IsEmpty);
        Assert.Empty(doc.Root.Children);
        Assert.NotNull(doc.Root.EndTag);
        Assert.Equal("a", doc.Root.EndTag.Name);
        Assert.Equal(5, doc.Root.EndTag.NameStart);
        Assert.Equal("</a>", Slice(doc, doc.Root.EndTag));
        Assert.Equal(7, doc.Root.End);
    }

    [Fact]
    public void Element_whose_content_is_one_space_has_one_whitespace_text_child()
    {
        XmlElementSyntax root = Parse("<a> </a>").Root;

        XmlTextSyntax text = Assert.IsType<XmlTextSyntax>(Assert.Single(root.Children));
        Assert.True(text.IsWhitespace);
        Assert.False(text.IsRaw);
        Assert.Equal(" ", text.Value);
        Assert.Equal(3, text.Start);
        Assert.Equal(4, text.End);
    }

    [Fact]
    public void Trailing_newline_after_the_root_is_epilog_whitespace()
    {
        XmlDocumentSyntax doc = Parse("<a/>\n");

        XmlTextSyntax text = Assert.IsType<XmlTextSyntax>(Assert.Single(doc.Epilog));
        Assert.True(text.IsWhitespace);
        Assert.Equal("\n", text.Value);
    }

    [Fact]
    public void Comments_and_processing_instructions_before_and_after_the_root_are_kept_in_order()
    {
        XmlDocumentSyntax doc = Parse("<?xml version=\"1.0\"?><!--before--><?pi one?><r/><!--after--><?pi two?>");

        Assert.Equal(2, doc.Prolog.Count);
        Assert.Equal("before", Assert.IsType<XmlCommentSyntax>(doc.Prolog[0]).Content);
        XmlProcessingInstructionSyntax first = Assert.IsType<XmlProcessingInstructionSyntax>(doc.Prolog[1]);
        Assert.Equal("pi", first.Target);
        Assert.Equal("one", first.Data);
        Assert.Equal("<?pi one?>", Slice(doc, first));

        Assert.Equal(2, doc.Epilog.Count);
        Assert.Equal("after", Assert.IsType<XmlCommentSyntax>(doc.Epilog[0]).Content);
        XmlProcessingInstructionSyntax second = Assert.IsType<XmlProcessingInstructionSyntax>(doc.Epilog[1]);
        Assert.Equal("pi", second.Target);
        Assert.Equal("two", second.Data);
    }

    [Fact]
    public void Declaration_reads_version_encoding_and_standalone()
    {
        XmlDocumentSyntax doc = Parse("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><r/>");

        XmlDeclarationSyntax declaration = Assert.IsType<XmlDeclarationSyntax>(doc.Declaration);
        Assert.Equal("1.0", declaration.Version);
        Assert.Equal("UTF-8", declaration.Encoding);
        Assert.Equal("yes", declaration.Standalone);
        Assert.Equal(0, declaration.Start);
        Assert.Equal(55, declaration.End);
        Assert.Empty(doc.Prolog);
    }

    [Fact]
    public void Declaration_without_encoding_or_standalone_has_null_for_them()
    {
        XmlDeclarationSyntax declaration = Assert.IsType<XmlDeclarationSyntax>(Parse("<?xml version='1.0' ?><r/>").Declaration);

        Assert.Equal("1.0", declaration.Version);
        Assert.Null(declaration.Encoding);
        Assert.Null(declaration.Standalone);
    }

    // ----- Doctype -----

    [Fact]
    public void Doctype_with_an_external_id_and_an_internal_subset_keeps_the_subset_raw()
    {
        string subset = Lines(
            "  <!ENTITY quoted \"a ] b > c\">",
            "  <!-- comment with ] inside -->",
            "  <!ENTITY other 'x]>y'>",
            string.Empty);
        string doctype = "<!DOCTYPE r PUBLIC \"-//Example//DTD R//EN\" \"r.dtd\" [\n" + subset + "]>";
        XmlDocumentSyntax doc = Parse("<?xml version=\"1.0\"?>\n" + doctype + "\n<r/>");

        XmlDoctypeSyntax node = Assert.IsType<XmlDoctypeSyntax>(Assert.Single(Significant(doc.Prolog)));
        Assert.Equal("r", node.Name);
        Assert.Equal("\n  <!ENTITY quoted \"a ] b > c\">\n  <!-- comment with ] inside -->\n  <!ENTITY other 'x]>y'>\n", node.InternalSubset);
        Assert.Equal(doctype, Slice(doc, node));
        AssertAt(doc, node.NameStart, 2, 11);
        Assert.Equal("r", doc.Root.Name);
    }

    [Fact]
    public void Doctype_without_a_subset_has_a_null_internal_subset()
    {
        XmlDoctypeSyntax node = Assert.IsType<XmlDoctypeSyntax>(Assert.Single(Parse("<!DOCTYPE r SYSTEM \"r.dtd\"><r/>").Prolog));

        Assert.Equal("r", node.Name);
        Assert.Null(node.InternalSubset);
    }

    [Fact]
    public void Attlist_default_is_not_in_the_source_and_so_not_in_the_tree()
    {
        XmlDocumentSyntax doc = Parse(Lines(
            "<!DOCTYPE item [",
            "  <!ATTLIST item kind CDATA \"default-kind\">",
            "]>",
            "<item name=\"x\"/>"));

        XmlAttributeSyntax attribute = Assert.Single(doc.Root.Attributes);
        Assert.Equal("name", attribute.Name);
        Assert.Equal("x", attribute.Value);
    }

    // ----- Text runs and references -----

    [Fact]
    public void Text_with_references_and_cdata_is_split_only_by_markup()
    {
        XmlElementSyntax root = Parse("<r>x&amp;y<![CDATA[z]]>w</r>").Root;

        Assert.Equal(3, root.Children.Count);
        XmlTextSyntax first = Assert.IsType<XmlTextSyntax>(root.Children[0]);
        Assert.Equal("x&amp;y", first.Raw);
        Assert.Equal("x&y", first.Value);
        Assert.False(first.IsWhitespace);
        Assert.Equal("z", Assert.IsType<XmlCDataSyntax>(root.Children[1]).Content);
        Assert.Equal("w", Assert.IsType<XmlTextSyntax>(root.Children[2]).Value);
    }

    [Fact]
    public void A_comment_splits_a_text_run_into_two()
    {
        XmlElementSyntax root = Parse("<r>a<!--c-->b</r>").Root;

        Assert.Equal("a", Assert.IsType<XmlTextSyntax>(root.Children[0]).Value);
        Assert.Equal("c", Assert.IsType<XmlCommentSyntax>(root.Children[1]).Content);
        Assert.Equal("b", Assert.IsType<XmlTextSyntax>(root.Children[2]).Value);
    }

    [Fact]
    public void A_character_reference_that_is_a_space_is_not_whitespace_text()
    {
        XmlTextSyntax text = Assert.IsType<XmlTextSyntax>(Assert.Single(Parse("<r>&#32;</r>").Root.Children));

        Assert.False(text.IsWhitespace);
        Assert.False(text.IsRaw);
        Assert.Equal(" ", text.Value);
        Assert.Equal("&#32;", text.Raw);
    }

    [Fact]
    public void Character_references_expand_in_text_and_attribute_values_without_normalisation()
    {
        XmlDocumentSyntax doc = Parse("<r a=\"&#10;&#13;&#9;\">&#13;&#x1F600;&#65;</r>");

        Assert.Equal("\n\r\t", AttributeValue(doc.Root, "a"));
        Assert.Equal("\r" + char.ConvertFromUtf32(0x1F600) + "A", ((XmlTextSyntax)Assert.Single(doc.Root.Children)).Value);
    }

    [Fact]
    public void Literal_tab_and_newlines_in_an_attribute_value_each_become_one_space()
    {
        XmlAttributeSyntax attribute = Assert.Single(Parse("<r a=\"x\ty\nz\r\nw\rv\"/>").Root.Attributes);

        Assert.Equal("x y z w v", attribute.Value);
        Assert.Equal("x\ty\nz\r\nw\rv", attribute.RawValue);
    }

    [Fact]
    public void Text_keeps_tabs_and_turns_lone_cr_and_crlf_into_lf()
    {
        XmlTextSyntax text = Assert.IsType<XmlTextSyntax>(Assert.Single(Parse("<r>a\tb\rc\r\nd</r>").Root.Children));

        Assert.Equal("a\tb\nc\nd", text.Value);
        Assert.Equal("a\tb\rc\r\nd", text.Raw);
    }

    [Fact]
    public void Attribute_names_quotes_and_offsets_are_recorded_for_both_quote_kinds()
    {
        const string xml = "<r a=\"1\" b='2'/>";
        XmlDocumentSyntax doc = Parse(xml);
        XmlElementSyntax root = doc.Root;

        Assert.Equal(new[] { "a", "b" }, AttributeNames(root));
        Assert.Equal('"', root.Attributes[0].Quote);
        Assert.Equal('\'', root.Attributes[1].Quote);

        // In <r a="1" b='2'/> the name b starts at offset 9 and its opening quote is at offset 11.
        Assert.Equal("b='2'", Slice(doc, root.Attributes[1]));
        Assert.Equal(9, root.Attributes[1].Start);
        Assert.Equal(11, root.Attributes[1].ValueStart);
        Assert.Equal("2", root.Attributes[1].RawValue);
    }

    // ----- Normalisation of comments and PIs (line ends, as XmlReader reports them) -----

    [Fact]
    public void Comment_and_pi_line_ends_are_normalised_to_lf()
    {
        XmlDocumentSyntax doc = Parse("<!--a\r\nb\rc--><?pi x\r\ny?><r/>");

        Assert.Equal("a\nb\nc", Assert.IsType<XmlCommentSyntax>(doc.Prolog[0]).Content);
        Assert.Equal("x\ny", Assert.IsType<XmlProcessingInstructionSyntax>(doc.Prolog[1]).Data);
    }

    // ----- Raw-mode values -----

    [Fact]
    public void A_custom_entity_in_text_gives_a_raw_run_whose_value_is_the_raw_text()
    {
        XmlDocumentSyntax doc = Parse(Lines(
            "<!DOCTYPE r [",
            "  <!ENTITY t \"some text\">",
            "]>",
            "<r>a&t;b</r>"));

        XmlTextSyntax text = Assert.IsType<XmlTextSyntax>(Assert.Single(doc.Root.Children));
        Assert.True(text.IsRaw);
        Assert.False(text.IsWhitespace);
        Assert.Equal("a&t;b", text.Raw);
        Assert.Equal("a&t;b", text.Value);
        AssertAt(doc, text.Start, 4, 4);
    }

    [Fact]
    public void A_custom_entity_in_an_attribute_gives_a_raw_value()
    {
        XmlDocumentSyntax doc = Parse(Lines(
            "<!DOCTYPE r [",
            "  <!ENTITY t \"some text\">",
            "]>",
            "<r a=\"x&t;y\"/>"));

        XmlAttributeSyntax attribute = Assert.Single(doc.Root.Attributes);
        Assert.True(attribute.IsRaw);
        Assert.Equal("x&t;y", attribute.RawValue);
        Assert.Equal("x&t;y", attribute.Value);
    }

    [Fact]
    public void Predefined_references_are_not_raw()
    {
        XmlAttributeSyntax attribute = Assert.Single(Parse("<r a=\"&lt;&gt;&amp;&apos;&quot;\"/>").Root.Attributes);

        Assert.False(attribute.IsRaw);
        Assert.Equal("<>&'\"", attribute.Value);
        Assert.Equal("&lt;&gt;&amp;&apos;&quot;", attribute.RawValue);
    }

    // ----- Markup-bearing entities -----

    [Fact]
    public void An_entity_with_markup_is_rejected_at_its_first_reference()
    {
        string text = Lines(
            "<!DOCTYPE r [<!ENTITY t \"text\"><!ENTITY e \"<b/>\">]>",
            "<r>&t; &e;</r>");

        CfgeditException error = Assert.Throws<CfgeditException>(() => Parse(text));

        Assert.Equal("entities that contain markup are not supported", error.Message);
        Assert.Equal(2, error.Line);
        Assert.Equal(8, error.Column);
    }

    [Fact]
    public void An_entity_with_markup_reached_through_another_entity_is_reported_at_the_doctype()
    {
        string text = Lines(
            "<?xml version=\"1.0\"?>",
            "<!DOCTYPE r [<!ENTITY e \"<b/>\"><!ENTITY a \"&e;\">]>",
            "<r>&a;</r>");

        CfgeditException error = Assert.Throws<CfgeditException>(() => Parse(text));

        Assert.Equal("entities that contain markup are not supported", error.Message);
        Assert.Equal(2, error.Line);
        Assert.Equal(1, error.Column);
    }

    [Fact]
    public void A_character_reference_that_makes_an_entity_contain_markup_is_rejected()
    {
        string text = Lines(
            "<!DOCTYPE r [<!ENTITY e \"&#60;b/>\">]>",
            "<r>&e;</r>");

        CfgeditException error = Assert.Throws<CfgeditException>(() => Parse(text));

        Assert.Equal("entities that contain markup are not supported", error.Message);
        Assert.Equal(2, error.Line);
        Assert.Equal(4, error.Column);
    }

    // ----- Positions with line breaks -----

    [Fact]
    public void Crlf_documents_count_each_line_break_once_and_values_are_normalised()
    {
        XmlDocumentSyntax doc = Parse("<?xml version=\"1.0\"?>\r\n<r a=\"x\r\ny\">\r\n  line1\r\nline2\r\n<c/>\r\n</r>\r\n");

        Assert.Equal("x y", AttributeValue(doc.Root, "a"));
        AssertAt(doc, doc.Root.NameStart, 2, 2);
        XmlElementSyntax child = Child(doc.Root, "c");
        AssertAt(doc, child.NameStart, 6, 2);
        XmlTextSyntax text = Assert.IsType<XmlTextSyntax>(doc.Root.Children[0]);
        Assert.Equal("\n  line1\nline2\n", text.Value);
        Assert.Equal("\r\n", text.Raw[^2..]);
    }

    [Fact]
    public void A_lone_cr_is_a_line_break_for_positions()
    {
        XmlDocumentSyntax doc = Parse("<r>\r<c/></r>");

        AssertAt(doc, Child(doc.Root, "c").NameStart, 2, 2);
    }

    // ----- Deep nesting -----

    [Fact]
    public void Nesting_one_level_beyond_the_cap_is_an_error_at_the_element_name()
    {
        // Each start tag on its own line: the 257th is at line 257, and its name at column 2.
        var sb = new StringBuilder();
        for (int i = 0; i < 257; i++)
        {
            sb.Append("<a>\n");
        }

        for (int i = 0; i < 257; i++)
        {
            sb.Append("</a>");
        }

        CfgeditException ex = Assert.Throws<CfgeditException>(() => Parse(sb.ToString()));

        Assert.Equal("nesting is deeper than 256 levels", ex.Message);
        Assert.Equal(257, ex.Line);
        Assert.Equal(2, ex.Column);
    }

    [Fact]
    public void A_document_100000_elements_deep_fails_cleanly_with_the_depth_error()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 100000; i++)
        {
            sb.Append("<a>");
        }

        for (int i = 0; i < 100000; i++)
        {
            sb.Append("</a>");
        }

        CfgeditException ex = Assert.Throws<CfgeditException>(() => Parse(sb.ToString()));

        Assert.Equal("nesting is deeper than 256 levels", ex.Message);
        Assert.Equal(1, ex.Line);
        Assert.Equal((256 * 3) + 2, ex.Column);
    }

    [Fact]
    public void A_document_at_the_nesting_cap_parses()
    {
        const int depth = 256;
        var sb = new StringBuilder();
        for (int i = 0; i < depth; i++)
        {
            sb.Append("<a>");
        }

        sb.Append('x');
        for (int i = 0; i < depth; i++)
        {
            sb.Append("</a>");
        }

        XmlDocumentSyntax doc = Parse(sb.ToString());

        int levels = 1;
        XmlElementSyntax current = doc.Root;
        while (current.Children.Count == 1 && current.Children[0] is XmlElementSyntax child)
        {
            levels++;
            current = child;
        }

        Assert.Equal(depth, levels);
        Assert.Equal("x", Assert.IsType<XmlTextSyntax>(Assert.Single(current.Children)).Value);
        Assert.NotNull(current.EndTag);
    }

    // ----- Parse errors: the reader's message without its position suffix, and its position -----

    [Fact]
    public void Mismatched_end_tag_is_reported_at_the_end_tag_name()
    {
        CfgeditException error = Assert.Throws<CfgeditException>(() => Parse("<a></b>"));

        Assert.Equal("The 'a' start tag on line 1 position 2 does not match the end tag of 'b'.", error.Message);
        Assert.Equal(1, error.Line);
        Assert.Equal(6, error.Column);
    }

    [Fact]
    public void Duplicate_attribute_is_reported_at_the_second_attribute()
    {
        CfgeditException error = Assert.Throws<CfgeditException>(() => Parse("<a x=\"1\" x=\"2\"/>"));

        Assert.Equal("'x' is a duplicate attribute name.", error.Message);
        Assert.Equal(1, error.Line);
        Assert.Equal(10, error.Column);
    }

    [Fact]
    public void Less_than_in_an_attribute_value_is_reported_at_the_character()
    {
        CfgeditException error = Assert.Throws<CfgeditException>(() => Parse("<a x=\"<\"/>"));

        Assert.Equal("'<', hexadecimal value 0x3C, is an invalid attribute character.", error.Message);
        Assert.Equal(1, error.Line);
        Assert.Equal(7, error.Column);
    }

    [Fact]
    public void Control_character_u0001_is_reported_at_the_character()
    {
        CfgeditException error = Assert.Throws<CfgeditException>(() => Parse("<a>" + (char)1 + "</a>"));

        Assert.Equal("'" + (char)1 + "', hexadecimal value 0x01, is an invalid character.", error.Message);
        Assert.Equal(1, error.Line);
        Assert.Equal(4, error.Column);
    }

    [Fact]
    public void Undeclared_entity_is_reported_at_the_reference()
    {
        CfgeditException error = Assert.Throws<CfgeditException>(() => Parse("<a>&nope;</a>"));

        Assert.Equal("Reference to undeclared entity 'nope'.", error.Message);
        Assert.Equal(1, error.Line);
        Assert.Equal(5, error.Column);
    }

    [Fact]
    public void Two_roots_are_reported_at_the_second_root()
    {
        CfgeditException error = Assert.Throws<CfgeditException>(() => Parse("<a/><b/>"));

        Assert.Equal("There are multiple root elements.", error.Message);
        Assert.Equal(1, error.Line);
        Assert.Equal(6, error.Column);
    }

    [Fact]
    public void Undeclared_prefix_is_reported_at_the_element_name()
    {
        CfgeditException error = Assert.Throws<CfgeditException>(() => Parse("<p:a/>"));

        Assert.Equal("'p' is an undeclared prefix.", error.Message);
        Assert.Equal(1, error.Line);
        Assert.Equal(2, error.Column);
    }

    [Fact]
    public void Unterminated_attribute_value_is_reported_at_the_end_of_input()
    {
        CfgeditException error = Assert.Throws<CfgeditException>(() => Parse("<a x=\"1/>"));

        Assert.Equal("There is an unclosed literal string.", error.Message);
        Assert.Equal(1, error.Line);
        Assert.Equal(10, error.Column);
    }

    // ----- The test hook: a moved position is reported as an internal mismatch -----

    [Fact]
    public void A_moved_scanner_position_is_an_internal_parse_mismatch_at_the_scanned_node()
    {
        var source = SourceText.Create("<a/>", Encoding.UTF8, false);

        CfgeditException error = Assert.Throws<CfgeditException>(() => XmlDocumentSyntax.Parse(source, perturbedIndex: 0));

        Assert.Equal(
            "internal parse mismatch: element 'a' is at line 1, column 3 in the scanner but line 1, column 2 in the reader (please report this)",
            error.Message);
        Assert.Equal(1, error.Line);
        Assert.Equal(3, error.Column);
    }

    [Fact]
    public void Without_the_hook_the_same_document_parses()
    {
        var source = SourceText.Create("<a/>", Encoding.UTF8, false);

        XmlDocumentSyntax doc = XmlDocumentSyntax.Parse(source, perturbedIndex: null);

        Assert.Equal("a", doc.Root.Name);
    }
}
