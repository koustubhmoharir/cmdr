using System.Text;
using Cfgedit.Core.Json;
using Cfgedit.Core.Model;
using Cfgedit.Core.Text;

namespace Cfgedit.Core.Tests.Model;

/// <summary>Formats by file extension and by format name, and document loading through a format.</summary>
public sealed class FormatsTests
{
    [Theory]
    [InlineData(".json5", "json5")]
    [InlineData(".JSON5", "json5")]
    [InlineData(".Json5", "json5")]
    [InlineData(".json", "jsonc")]
    [InlineData(".JSON", "jsonc")]
    [InlineData(".jsonc", "jsonc")]
    [InlineData(".JsonC", "jsonc")]
    [InlineData(".xml", "xml")]
    [InlineData(".XML", "xml")]
    [InlineData(".config", "xml")]
    [InlineData(".Config", "xml")]
    [InlineData(".csproj", "xml")]
    [InlineData(".props", "xml")]
    [InlineData(".targets", "xml")]
    [InlineData(".xaml", "xml")]
    [InlineData(".resx", "xml")]
    [InlineData(".nuspec", "xml")]
    [InlineData(".xsd", "xml")]
    [InlineData(".xslt", "xml")]
    [InlineData(".svg", "xml")]
    [InlineData(".SVG", "xml")]
    public void Every_listed_extension_gives_its_format_in_any_case(string extension, string expectedName)
    {
        IConfigFormat? format = Formats.ForPath("some/dir/file" + extension);

        Assert.NotNull(format);
        Assert.Equal(expectedName, format.Name);
    }

    [Theory]
    [InlineData("settings.txt")]
    [InlineData("settings.yaml")]
    [InlineData("settings.toml")]
    [InlineData("settings.ini")]
    [InlineData("settings")]
    [InlineData("")]
    [InlineData("dir.json/file")]
    [InlineData("settings.xml.bak")]
    public void Other_extensions_and_paths_without_one_give_null(string path)
    {
        Assert.Null(Formats.ForPath(path));
    }

    [Fact]
    public void A_json_path_gives_the_jsonc_dialect_and_a_json5_path_the_json5_dialect()
    {
        Assert.Equal(JsonDialect.Jsonc, Assert.IsType<JsonFormat>(Formats.ForPath("appsettings.json")).Dialect);
        Assert.Equal(JsonDialect.Json5, Assert.IsType<JsonFormat>(Formats.ForPath("example.json5")).Dialect);
        Assert.IsType<XmlFormat>(Formats.ForPath("web.config"));
    }

    [Theory]
    [InlineData("json", "json", JsonDialect.Json)]
    [InlineData("JSON", "json", JsonDialect.Json)]
    [InlineData("jsonc", "jsonc", JsonDialect.Jsonc)]
    [InlineData("Jsonc", "jsonc", JsonDialect.Jsonc)]
    [InlineData("json5", "json5", JsonDialect.Json5)]
    [InlineData("JSON5", "json5", JsonDialect.Json5)]
    public void A_json_format_name_gives_its_dialect(string name, string expectedName, JsonDialect dialect)
    {
        IConfigFormat? format = Formats.FromName(name);

        Assert.Equal(expectedName, format!.Name);
        Assert.Equal(dialect, Assert.IsType<JsonFormat>(format).Dialect);
    }

    [Theory]
    [InlineData("xml")]
    [InlineData("XML")]
    [InlineData("Xml")]
    public void The_xml_format_name_gives_the_xml_format(string name)
    {
        Assert.Equal("xml", Formats.FromName(name)!.Name);
        Assert.IsType<XmlFormat>(Formats.FromName(name));
    }

    [Theory]
    [InlineData("yaml")]
    [InlineData("toml")]
    [InlineData("xml5")]
    [InlineData("")]
    [InlineData(".json")]
    public void Unknown_format_names_give_null(string name)
    {
        Assert.Null(Formats.FromName(name));
    }

    [Fact]
    public void Format_names_are_the_four_listed_ones()
    {
        Assert.Equal("json", new JsonFormat(JsonDialect.Json).Name);
        Assert.Equal("jsonc", new JsonFormat(JsonDialect.Jsonc).Name);
        Assert.Equal("json5", new JsonFormat(JsonDialect.Json5).Name);
        Assert.Equal("xml", new XmlFormat().Name);
    }

    [Fact]
    public void A_document_loaded_through_a_format_keeps_that_format()
    {
        IConfigFormat format = Formats.FromName("json5")!;

        ConfigDocument document = ConfigDocument.Parse(SourceText.Create("{}", Encoding.UTF8, false), format);

        Assert.Same(format, document.Format);
    }
}
