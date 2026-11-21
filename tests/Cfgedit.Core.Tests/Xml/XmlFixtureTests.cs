using Cfgedit.Core.Xml;
using static Cfgedit.Core.Tests.Xml.XmlTestSupport;

namespace Cfgedit.Core.Tests.Xml;

/// <summary>The synthetic fixtures under Fixtures/xml, parsed through the byte path. Every expectation is a literal.</summary>
public sealed class XmlFixtureTests
{
    public static IEnumerable<object[]> AllFixtures()
    {
        foreach (string path in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xml")))
        {
            // The markup-entity fixture is rejected by design, so it has no tree to round trip. It has its own test.
            if (Path.GetFileName(path) != "markupentity.xml")
            {
                yield return [Path.GetFileName(path)];
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Every_fixture_parses_and_its_bytes_round_trip(string name)
    {
        byte[] bytes = File.ReadAllBytes(FixturePath(name));

        XmlDocumentSyntax doc = XmlDocumentSyntax.Parse(bytes, name);

        Assert.False(doc.Source.HasBom);
        Assert.Equal("utf-8", doc.Source.Encoding.WebName);
        Assert.Equal(bytes, doc.Source.Encode());
    }

    [Fact]
    public void Web_config_has_its_declaration_comment_and_nested_sections()
    {
        XmlDocumentSyntax doc = Fixture("webconfig.config");

        Assert.Equal("1.0", doc.Declaration!.Version);
        Assert.Equal("utf-8", doc.Declaration.Encoding);
        Assert.Null(doc.Declaration.Standalone);
        Assert.Equal(3, doc.Prolog.Count);
        Assert.True(((XmlTextSyntax)doc.Prolog[0]).IsWhitespace);
        XmlCommentSyntax comment = Assert.IsType<XmlCommentSyntax>(doc.Prolog[1]);
        Assert.Equal(" web.config test fixture ", comment.Content);
        Assert.Equal("<!-- web.config test fixture -->", Slice(doc, comment));

        Assert.Equal("configuration", doc.Root.Name);
        AssertAt(doc, doc.Root.NameStart, 3, 2);
        Assert.Equal(new[] { "appSettings", "connectionStrings", "system.web" }, ElementNames(Significant(doc.Root.Children)));
        Assert.DoesNotContain(doc.Epilog, n => n is XmlElementSyntax);
    }

    [Fact]
    public void Web_config_app_settings_adds_have_names_values_and_spans()
    {
        XmlDocumentSyntax doc = Fixture("webconfig.config");
        XmlElementSyntax appSettings = Child(doc.Root, "appSettings");

        XmlElementSyntax[] adds = appSettings.Children.OfType<XmlElementSyntax>().ToArray();
        Assert.Equal(2, adds.Length);

        Assert.Equal(new[] { "key", "value" }, AttributeNames(adds[0]));
        Assert.Equal("Name", AttributeValue(adds[0], "key"));
        Assert.Equal("Sample", AttributeValue(adds[0], "value"));
        Assert.True(adds[0].IsEmpty);
        Assert.Null(adds[0].EndTag);
        Assert.Equal("<add key=\"Name\" value=\"Sample\" />", Slice(doc, adds[0]));
        Assert.All(adds[0].Attributes, a => Assert.Equal('"', a.Quote));

        XmlAttributeSyntax mode = adds[1].Attributes[1];
        Assert.Equal("value", mode.Name);
        Assert.Equal("a &amp; b", mode.RawValue);
        Assert.Equal("a & b", mode.Value);
        Assert.False(mode.IsRaw);

        Assert.Equal("appSettings", appSettings.EndTag!.Name);
        Assert.Equal("</appSettings>", Slice(doc, appSettings.EndTag));
    }

    [Fact]
    public void Web_config_connection_strings_and_nested_system_web_sections()
    {
        XmlDocumentSyntax doc = Fixture("webconfig.config");

        XmlElementSyntax main = Child(Child(doc.Root, "connectionStrings"), "add");
        Assert.Equal(new[] { "name", "connectionString", "providerName" }, AttributeNames(main));
        Assert.Equal("System.Data.SqlClient", AttributeValue(main, "providerName"));

        XmlElementSyntax systemWeb = Child(doc.Root, "system.web");
        List<XmlNodeSyntax> sections = Significant(systemWeb.Children);
        Assert.Equal(3, sections.Count);
        XmlElementSyntax compilation = Assert.IsType<XmlElementSyntax>(sections[0]);
        Assert.Equal("compilation", compilation.Name);
        Assert.Equal(new[] { "debug", "targetFramework" }, AttributeNames(compilation));
        Assert.Equal(" nested section ", Assert.IsType<XmlCommentSyntax>(sections[1]).Content);
        XmlElementSyntax customErrors = Assert.IsType<XmlElementSyntax>(sections[2]);
        Assert.True(customErrors.IsEmpty);
        Assert.Equal("RemoteOnly", AttributeValue(customErrors, "mode"));

        XmlElementSyntax assemblies = Child(compilation, "assemblies");
        Assert.Equal("System.Web, Version=4.0.0.0", AttributeValue(Child(assemblies, "add"), "assembly"));
        Assert.Equal("\n", Assert.IsType<XmlTextSyntax>(doc.Epilog[0]).Value);
    }

    [Fact]
    public void App_config_has_a_declaration_with_a_space_before_its_end()
    {
        XmlDocumentSyntax doc = Fixture("App.config");

        Assert.Equal("1.0", doc.Declaration!.Version);
        Assert.Equal("utf-8", doc.Declaration.Encoding);
        Assert.Equal(new[] { "configSections", "startup", "runtime" }, ElementNames(Significant(doc.Root.Children)));
    }

    [Fact]
    public void App_config_section_and_runtime_attributes_are_read()
    {
        XmlDocumentSyntax doc = Fixture("App.config");

        XmlElementSyntax section = Child(Child(doc.Root, "configSections"), "section");
        Assert.Equal(new[] { "name", "type", "requirePermission" }, AttributeNames(section));
        Assert.Equal(
            "System.Data.Entity.Internal.ConfigFile.EntityFrameworkSection, EntityFramework",
            AttributeValue(section, "type"));

        XmlElementSyntax supported = Child(Child(doc.Root, "startup"), "supportedRuntime");
        Assert.Equal(".NETFramework,Version=v4.8", AttributeValue(supported, "sku"));

        XmlElementSyntax assemblyBinding = Child(Child(doc.Root, "runtime"), "assemblyBinding");
        Assert.Equal(new[] { "xmlns" }, AttributeNames(assemblyBinding));
        Assert.Equal("urn:schemas-microsoft-com:asm.v1", AttributeValue(assemblyBinding, "xmlns"));

        XmlElementSyntax identity = Child(Child(assemblyBinding, "dependentAssembly"), "assemblyIdentity");
        Assert.Equal(new[] { "name", "publicKeyToken", "culture" }, AttributeNames(identity));
        Assert.Equal(new[] { "Newtonsoft.Json", "30ad4fe6b2a6aeed", "neutral" }, identity.Attributes.Select(a => a.Value).ToArray());

        XmlElementSyntax redirect = Child(Child(assemblyBinding, "dependentAssembly"), "bindingRedirect");
        Assert.True(redirect.IsEmpty);
        Assert.Equal("0.0.0.0-13.0.0.0", AttributeValue(redirect, "oldVersion"));
        Assert.Equal("13.0.0.0", AttributeValue(redirect, "newVersion"));
    }

    [Fact]
    public void Log4net_appenders_nest_elements_and_attributes()
    {
        XmlDocumentSyntax doc = Fixture("log4net.xml");

        Assert.Equal("UTF-8", doc.Declaration!.Encoding);
        Assert.Equal(new[] { "appender", "root" }, ElementNames(Significant(doc.Root.Children)));

        XmlElementSyntax appender = Child(doc.Root, "appender");
        Assert.Equal(new[] { "name", "type" }, AttributeNames(appender));
        Assert.Equal("RollingFile", AttributeValue(appender, "name"));
        Assert.Equal(new[] { "file", "layout" }, ElementNames(Significant(appender.Children)));

        XmlElementSyntax conversion = Child(Child(appender, "layout"), "conversionPattern");
        Assert.Equal("%date [%thread] %-5level %logger - %message%newline", AttributeValue(conversion, "value"));

        XmlElementSyntax root = Child(doc.Root, "root");
        XmlElementSyntax level = Child(root, "level");
        Assert.Equal("<level value=\"DEBUG\" />", Slice(doc, level));
        Assert.Equal("RollingFile", AttributeValue(Child(root, "appender-ref"), "ref"));
    }

    [Fact]
    public void Sdk_csproj_has_no_prolog_and_reads_its_property_text()
    {
        XmlDocumentSyntax doc = Fixture("Sample.csproj");

        Assert.Null(doc.Declaration);
        Assert.Empty(doc.Prolog);
        Assert.Equal("Project", doc.Root.Name);
        Assert.Equal("Microsoft.NET.Sdk", AttributeValue(doc.Root, "Sdk"));
        Assert.Equal(new[] { "PropertyGroup", "ItemGroup" }, ElementNames(Significant(doc.Root.Children)));

        XmlElementSyntax propertyGroup = Child(doc.Root, "PropertyGroup");
        XmlElementSyntax targetFramework = Child(propertyGroup, "TargetFramework");
        Assert.Equal("<TargetFramework>net10.0</TargetFramework>", Slice(doc, targetFramework));
        Assert.Equal("net10.0", Assert.IsType<XmlTextSyntax>(Assert.Single(targetFramework.Children)).Value);
        Assert.Equal("enable", Assert.IsType<XmlTextSyntax>(Assert.Single(Child(propertyGroup, "Nullable").Children)).Value);

        XmlElementSyntax reference = Child(Child(doc.Root, "ItemGroup"), "PackageReference");
        Assert.Equal(new[] { "Include", "Version" }, AttributeNames(reference));
        Assert.Equal("13.0.3", AttributeValue(reference, "Version"));
        Assert.True(reference.IsEmpty);

        XmlTextSyntax trailing = Assert.IsType<XmlTextSyntax>(Assert.Single(doc.Epilog));
        Assert.True(trailing.IsWhitespace);
    }

    [Fact]
    public void Mixed_content_keeps_namespaces_prefixes_cdata_pis_and_xml_space()
    {
        XmlDocumentSyntax doc = Fixture("mixed.xml");

        Assert.Equal("1.0", doc.Declaration!.Version);
        Assert.Null(doc.Declaration.Encoding);

        XmlProcessingInstructionSyntax stylesheet = Assert.IsType<XmlProcessingInstructionSyntax>(doc.Prolog.Single(n => n is XmlProcessingInstructionSyntax));
        Assert.Equal("stylesheet", stylesheet.Target);
        Assert.Equal("type=\"text/xsl\" href=\"style.xsl\"", stylesheet.Data);
        Assert.Equal("<?stylesheet type=\"text/xsl\" href=\"style.xsl\"?>", Slice(doc, stylesheet));

        Assert.Equal("doc:root", doc.Root.Name);
        Assert.Equal(new[] { "xmlns:doc", "xmlns:x", "x:flag" }, AttributeNames(doc.Root));
        Assert.Equal("urn:example:doc", AttributeValue(doc.Root, "xmlns:doc"));
        Assert.Equal(new[] { "doc:para", "doc:mixed", "x:empty", "doc:code" }, ElementNames(Significant(doc.Root.Children)));

        XmlElementSyntax para = Child(doc.Root, "doc:para");
        Assert.Equal(new[] { "xml:space" }, AttributeNames(para));
        Assert.Equal("preserve", AttributeValue(para, "xml:space"));
        XmlTextSyntax paraText = Assert.IsType<XmlTextSyntax>(Assert.Single(para.Children));
        Assert.Equal("  two  spaces &lt;kept&gt;", paraText.Raw);
        Assert.Equal("  two  spaces <kept>", paraText.Value);

        XmlElementSyntax mixed = Child(doc.Root, "doc:mixed");
        Assert.Equal(7, mixed.Children.Count);
        Assert.Equal("before ", Assert.IsType<XmlTextSyntax>(mixed.Children[0]).Value);
        Assert.Equal("bold", Assert.IsType<XmlTextSyntax>(Assert.Single(Assert.IsType<XmlElementSyntax>(mixed.Children[1]).Children)).Value);
        XmlTextSyntax references = Assert.IsType<XmlTextSyntax>(mixed.Children[2]);
        Assert.Equal(" &#x41;&#66; ", references.Raw);
        Assert.Equal(" AB ", references.Value);
        Assert.Equal("raw <markup> & ", Assert.IsType<XmlCDataSyntax>(mixed.Children[3]).Content);
        Assert.Equal(" after", Assert.IsType<XmlTextSyntax>(mixed.Children[4]).Value);
        XmlProcessingInstructionSyntax pi = Assert.IsType<XmlProcessingInstructionSyntax>(mixed.Children[5]);
        Assert.Equal("pi", pi.Target);
        Assert.Equal("data here", pi.Data);
        Assert.Equal(" tail", Assert.IsType<XmlTextSyntax>(mixed.Children[6]).Value);

        Assert.True(Child(doc.Root, "x:empty").IsEmpty);
        Assert.Equal("line one\nline two", Assert.IsType<XmlCDataSyntax>(Assert.Single(Child(doc.Root, "doc:code").Children)).Content);
    }

    [Fact]
    public void Raw_mode_entities_in_text_and_attributes_are_raw_and_keep_their_text()
    {
        XmlDocumentSyntax doc = Fixture("rawentity.xml");

        XmlDoctypeSyntax doctype = Assert.IsType<XmlDoctypeSyntax>(doc.Prolog.Single(n => n is XmlDoctypeSyntax));
        Assert.Equal("note", doctype.Name);
        AssertAt(doc, doctype.NameStart, 2, 11);
        Assert.Equal(
            "\n  <!ENTITY company \"Contoso Ltd\">\n  <!ENTITY signature \"&#x2014; the team\">\n",
            doctype.InternalSubset);

        Assert.Equal(new[] { "to", "from", "body", "tag" }, ElementNames(Significant(doc.Root.Children)));
        Assert.Equal("Reader", Assert.IsType<XmlTextSyntax>(Assert.Single(Child(doc.Root, "to").Children)).Value);

        XmlTextSyntax from = Assert.IsType<XmlTextSyntax>(Assert.Single(Child(doc.Root, "from").Children));
        Assert.True(from.IsRaw);
        Assert.Equal("&company;", from.Raw);
        Assert.Equal("&company;", from.Value);

        XmlTextSyntax body = Assert.IsType<XmlTextSyntax>(Assert.Single(Child(doc.Root, "body").Children));
        Assert.True(body.IsRaw);
        Assert.Equal("Thanks, &company;. Regards, &signature;", body.Value);

        XmlAttributeSyntax attr = Assert.Single(Child(doc.Root, "tag").Attributes);
        Assert.True(attr.IsRaw);
        Assert.Equal("see &company;", attr.RawValue);
        Assert.Equal("see &company;", attr.Value);
    }

    [Fact]
    public void Entity_with_markup_is_rejected_with_its_position_and_path()
    {
        CfgeditException error = Assert.Throws<CfgeditException>(() => Fixture("markupentity.xml"));

        Assert.Equal("entities that contain markup are not supported", error.Message);
        Assert.Equal("markupentity.xml", error.Path);
        Assert.Equal(5, error.Line);
        Assert.Equal(13, error.Column);
    }

    [Fact]
    public void Attlist_default_is_skipped_and_the_document_parses()
    {
        XmlDocumentSyntax doc = Fixture("attlist.xml");

        Assert.Equal("item", Assert.IsType<XmlDoctypeSyntax>(Assert.Single(Significant(doc.Prolog))).Name);
        XmlAttributeSyntax name = Assert.Single(doc.Root.Attributes);
        Assert.Equal("name", name.Name);
        Assert.Equal("x", name.Value);
        Assert.True(doc.Root.IsEmpty);
    }

    [Fact]
    public void Whitespace_inside_tags_and_multi_line_attributes_keep_their_spans_and_values()
    {
        XmlDocumentSyntax doc = Fixture("whitespace.xml");

        Assert.Equal("utf-8", doc.Declaration!.Encoding);
        XmlElementSyntax settings = doc.Root;
        Assert.Equal("settings", settings.Name);
        Assert.Equal(
            "<settings\n    owner = 'Team A'\n    notes=\"first line\nsecond line\"\n    path='C:\\temp'\n  >",
            Slice(doc, settings.Start, settings.StartTagEnd));
        Assert.Equal("settings", settings.EndTag!.Name);
        Assert.Equal("</settings>", Slice(doc, settings.EndTag));

        Assert.Equal(new[] { "owner", "notes", "path" }, AttributeNames(settings));
        XmlAttributeSyntax owner = settings.Attributes[0];
        Assert.Equal('\'', owner.Quote);
        Assert.Equal("Team A", owner.Value);

        XmlAttributeSyntax notes = settings.Attributes[1];
        Assert.Equal('"', notes.Quote);
        Assert.Equal("first line\nsecond line", notes.RawValue);
        Assert.Equal("first line second line", notes.Value);
        Assert.Equal("notes=\"first line\nsecond line\"", Slice(doc, notes));
        AssertAt(doc, notes.Start, 4, 5);

        XmlAttributeSyntax path = settings.Attributes[2];
        Assert.Equal("C:\\temp", path.Value);

        XmlElementSyntax[] items = Significant(settings.Children).OfType<XmlElementSyntax>().ToArray();
        Assert.Equal(2, items.Length);

        XmlElementSyntax first = items[0];
        Assert.Equal("<item   key = \"a\"   value\n     = 'b' />", Slice(doc, first));
        Assert.True(first.IsEmpty);
        Assert.Equal(new[] { "key", "value" }, AttributeNames(first));
        Assert.Equal("a", first.Attributes[0].Value);
        Assert.Equal('"', first.Attributes[0].Quote);
        Assert.Equal("b", first.Attributes[1].Value);
        Assert.Equal('\'', first.Attributes[1].Quote);

        XmlElementSyntax second = items[1];
        Assert.Equal(new[] { "key", "id" }, AttributeNames(second));
        Assert.Equal("multi line value", second.Attributes[0].Value);
        Assert.Equal("3", second.Attributes[1].Value);
        Assert.False(second.IsEmpty);
        Assert.Equal("</item >", Slice(doc, second.EndTag!));
        Assert.Equal("item", second.EndTag!.Name);
    }

    [Fact]
    public void Self_closing_element_without_a_space_is_the_root()
    {
        XmlDocumentSyntax doc = Fixture("empty-self-closing.xml");

        Assert.Equal("a", doc.Root.Name);
        Assert.True(doc.Root.IsEmpty);
        Assert.Null(doc.Root.EndTag);
        Assert.Equal("<a/>", Slice(doc, doc.Root));
        Assert.Equal("\n", Assert.IsType<XmlTextSyntax>(Assert.Single(doc.Epilog)).Value);
    }

    [Fact]
    public void Self_closing_element_with_a_space_has_no_end_tag()
    {
        XmlDocumentSyntax doc = Fixture("empty-with-space.xml");

        Assert.True(doc.Root.IsEmpty);
        Assert.Null(doc.Root.EndTag);
        Assert.Equal("<a />", Slice(doc, doc.Root));
        Assert.Equal(5, doc.Root.StartTagEnd);
    }

    [Fact]
    public void Element_with_an_empty_pair_of_tags_has_an_end_tag()
    {
        XmlDocumentSyntax doc = Fixture("empty-pair.xml");

        Assert.False(doc.Root.IsEmpty);
        Assert.Empty(doc.Root.Children);
        Assert.Equal("<a></a>", Slice(doc, doc.Root));
        Assert.Equal("</a>", Slice(doc, doc.Root.EndTag!));
        Assert.Equal(5, doc.Root.EndTag!.NameStart);
    }
}
