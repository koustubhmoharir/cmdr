using Cfgedit.Core.Toml;
using Shared.Core;
using static Cfgedit.Core.Tests.Toml.TomlTestSupport;

namespace Cfgedit.Core.Tests.Toml;

/// <summary>
/// Realistic files: a Cargo manifest, a pyproject file and a CRLF file. Each is checked for its tiling, its byte-identical round
/// trip, its headers in order, and some of its values.
/// </summary>
public sealed class TomlFixtureTests
{
    [Theory]
    [InlineData("cargo.toml")]
    [InlineData("pyproject.toml")]
    [InlineData("crlf.toml")]
    public void A_fixture_tiles_and_round_trips_byte_for_byte(string name)
    {
        byte[] bytes = File.ReadAllBytes(FixturePath(name));
        SourceText source = SourceText.Decode(bytes, name);
        TomlDocument doc = TomlDocument.Parse(source);

        AssertTiles(doc);
        Assert.Equal(source.Text, Reassemble(doc));
        Assert.Equal(bytes, source.Encode());
    }

    [Fact]
    public void The_cargo_manifest_has_its_tables_and_array_tables_in_order()
    {
        TomlDocument doc = Fixture("cargo.toml");
        Assert.Equal(
            new[]
            {
                "T:package", "T:dependencies", "T:dev-dependencies", "A:bin", "A:bin", "T:profile.release",
                "T:features", "T:package.metadata.docs.rs",
            },
            HeaderNames(doc));
    }

    [Fact]
    public void The_cargo_manifest_values_decode()
    {
        TomlDocument doc = Fixture("cargo.toml");

        var description = Assert.IsType<TomlString>(Value(doc, "package", "description"));
        Assert.Equal(TomlStringForm.MultiLineBasic, description.Form);
        Assert.Equal("A sample crate whose manifest\nis used by the round trip tests.\n", description.Value);

        var edition = Assert.IsType<TomlString>(Value(doc, "package", "edition"));
        Assert.Equal("2021", edition.Value);

        var regex = Assert.IsType<TomlString>(Value(doc, "dependencies", "regex"));
        Assert.Equal(TomlStringForm.Literal, regex.Form);
        Assert.Equal("^\\d+\\.\\d+$", regex.Value);

        var lto = Assert.IsType<TomlBoolean>(Value(doc, "profile.release", "lto"));
        Assert.True(lto.Value);
        Assert.Equal(3, Assert.IsType<TomlInteger>(Value(doc, "profile.release", "opt-level")).Value);

        // The features array of this inline table spans lines, which an array may do inside an inline table.
        var tokio = Assert.IsType<TomlInlineTable>(Value(doc, "dependencies", "tokio"));
        Assert.Equal(new[] { "rt-multi-thread", "macros" }, InlineArrayStrings(tokio, "features"));
    }

    [Fact]
    public void The_pyproject_file_has_its_tables_and_array_tables_in_order()
    {
        TomlDocument doc = Fixture("pyproject.toml");
        Assert.Equal(
            new[]
            {
                "T:build-system", "T:project", "T:project.optional-dependencies", "T:project.scripts", "T:tool.ruff",
                "T:tool.ruff.lint", "T:tool.pytest.ini_options", "A:tool.mypy.overrides", "A:tool.mypy.overrides",
            },
            HeaderNames(doc));
    }

    [Fact]
    public void The_pyproject_values_decode_including_a_literal_with_double_quotes()
    {
        TomlDocument doc = Fixture("pyproject.toml");
        var dependencies = Assert.IsType<TomlArray>(Value(doc, "project", "dependencies"));
        var items = dependencies.Entries.OfType<TomlArrayItem>().Select(i => Assert.IsType<TomlString>(i.Value)).ToList();
        Assert.Equal(new[] { "httpx>=0.27", "tomli>=2.0; python_version < \"3.11\"" }, items.Select(s => s.Value));
        Assert.Equal(TomlStringForm.Literal, items[1].Form);

        var authors = Assert.IsType<TomlArray>(Value(doc, "project", "authors"));
        var author = Assert.IsType<TomlInlineTable>(authors.Entries.OfType<TomlArrayItem>().Single().Value);
        Assert.Equal(new[] { "name", "email" }, author.Entries.OfType<TomlInlineMember>().Select(m => m.Key.Parts[0].Name));
    }

    [Fact]
    public void The_crlf_file_reads_its_line_breaks_and_values_as_lf()
    {
        TomlDocument doc = Fixture("crlf.toml");
        Assert.Equal("\r\n", doc.Source.Newline);
        Assert.All(doc.Items, item => Assert.True(item.LineBreak.Length == 2 || item.LineBreak.Length == 0));
        Assert.Equal("line one\nline two", Assert.IsType<TomlString>(Value(doc, null, "text")).Value);

        var path = Assert.IsType<TomlString>(Value(doc, null, "path"));
        Assert.Equal("C:\\Users\\sample", path.Value);

        var nums = Assert.IsType<TomlArray>(Value(doc, null, "nums"));
        Assert.Equal(3, nums.Entries.OfType<TomlArrayItem>().Count());

        Assert.Equal("value", Assert.IsType<TomlString>(Value(doc, "section", "key")).Value);
        var keyItem = Assert.IsType<TomlKeyValueItem>(Assert.Single(doc.Items, i => i is TomlKeyValueItem kv && kv.Key.Parts[0].Name == "key"));
        Assert.Equal(" comment", keyItem.Comment!.Content);
    }

    /// <summary>The header names of a document in order: T for a table, A for an array table, each with its dotted key.</summary>
    private static string[] HeaderNames(TomlDocument doc)
    {
        var names = new List<string>();
        foreach (TomlItem item in doc.Items)
        {
            switch (item)
            {
                case TomlTableItem table:
                    names.Add("T:" + string.Join(".", table.Key.Parts.Select(p => p.Name)));
                    break;
                case TomlArrayTableItem array:
                    names.Add("A:" + string.Join(".", array.Key.Parts.Select(p => p.Name)));
                    break;
            }
        }

        return names.ToArray();
    }

    /// <summary>
    /// The value of <paramref name="key"/> in the table named by <paramref name="table"/> (a dotted name, or null for the root),
    /// where the table is defined by its header or by top-level key/values of the root.
    /// </summary>
    private static TomlValue Value(TomlDocument doc, string? table, string key)
    {
        // The root holds the key/values before the first header. A table holds those after its own header.
        bool inTable = table is null;
        foreach (TomlItem item in doc.Items)
        {
            if (item is TomlTableItem header)
            {
                inTable = table is not null && string.Join(".", header.Key.Parts.Select(p => p.Name)) == table;
                continue;
            }

            if (item is TomlHeaderItem)
            {
                inTable = false;
                continue;
            }

            if (inTable && item is TomlKeyValueItem kv && kv.Key.Parts.Count == 1 && kv.Key.Parts[0].Name == key)
            {
                return kv.Value;
            }
        }

        throw new InvalidOperationException($"no value {table}.{key}");
    }

    private static string[] InlineArrayStrings(TomlInlineTable table, string key)
    {
        var member = Assert.Single(table.Entries.OfType<TomlInlineMember>(), m => m.Key.Parts[0].Name == key);
        var array = Assert.IsType<TomlArray>(member.Value);
        return array.Entries.OfType<TomlArrayItem>().Select(i => ((TomlString)i.Value).Value).ToArray();
    }
}
