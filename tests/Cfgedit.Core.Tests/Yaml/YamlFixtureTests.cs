using Cfgedit.Core.Text;
using Cfgedit.Core.Yaml;
using static Cfgedit.Core.Tests.Yaml.YamlTestSupport;

namespace Cfgedit.Core.Tests.Yaml;

/// <summary>
/// Realistic files: a compose file, a workflow, a two-document manifest, an appsettings file with anchors and merge keys, and a CRLF
/// file. Each round-trips byte for byte, and its structure and values are checked.
/// </summary>
public sealed class YamlFixtureTests
{
    public static IEnumerable<object[]> Fixtures()
    {
        yield return ["docker-compose.yml"];
        yield return ["github-workflow.yml"];
        yield return ["kubernetes.yaml"];
        yield return ["appsettings.yaml"];
        yield return ["crlf.yaml"];
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Fixture_parses_tiles_and_round_trips_byte_for_byte(string name)
    {
        byte[] bytes = File.ReadAllBytes(FixturePath(name));
        SourceText source = SourceText.Decode(bytes, name);
        YamlDocument doc = YamlDocument.Parse(source);
        AssertTiles(doc);
        Assert.Equal(source.Text, Reassemble(doc));
        Assert.Equal(bytes, source.Encode());
    }

    [Fact]
    public void Compose_file_has_services_with_ports_aliases_and_flow_lists()
    {
        YamlBlockMapping root = RootMapping(Fixture("docker-compose.yml"));
        Assert.Equal("3.9", ScalarValue(root, "version"));
        var services = Assert.IsType<YamlBlockMapping>(root.Entries.Single(e => e.Key.Value == "services").Value);
        Assert.Equal(new[] { "web", "db", "cache" }, services.Entries.Select(e => e.Key.Value).ToArray());
        var web = Assert.IsType<YamlBlockMapping>(services.Entries[0].Value);
        var ports = Assert.IsType<YamlBlockSequence>(web.Entries.Single(e => e.Key.Value == "ports").Value);
        Assert.Equal(2, ports.Items.Count);
        Assert.Equal("8443:443", Assert.IsType<YamlScalar>(ports.Items[1].Value).Value);
        var logging = Assert.IsType<YamlAlias>(web.Entries.Single(e => e.Key.Value == "logging").Value);
        Assert.Equal("default-logging", logging.Name);
        var dependsOn = Assert.IsType<YamlFlowSequence>(web.Entries.Single(e => e.Key.Value == "depends_on").Value);
        Assert.Equal(2, dependsOn.Items.Count);
        var xLogging = Assert.IsType<YamlBlockMapping>(root.Entries.Single(e => e.Key.Value == "x-logging").Value);
        Assert.Equal("&default-logging", xLogging.Anchor!.Text);
        var db = Assert.IsType<YamlBlockMapping>(services.Entries[1].Value);
        var environment = Assert.IsType<YamlBlockMapping>(db.Entries.Single(e => e.Key.Value == "environment").Value);
        Assert.Equal("placeholder-not-a-secret", ScalarValue(environment, "POSTGRES_PASSWORD"));
    }

    [Fact]
    public void Workflow_keeps_the_on_key_as_text_and_its_block_scripts()
    {
        YamlBlockMapping root = RootMapping(Fixture("github-workflow.yml"));
        Assert.Contains(root.Entries, e => e.Key.Value == "on" && e.Key.Kind == YamlCoreKind.String);
        var jobs = Assert.IsType<YamlBlockMapping>(root.Entries.Single(e => e.Key.Value == "jobs").Value);
        var test = Assert.IsType<YamlBlockMapping>(jobs.Entries.Single().Value);
        var strategy = Assert.IsType<YamlBlockMapping>(test.Entries.Single(e => e.Key.Value == "strategy").Value);
        var matrix = Assert.IsType<YamlBlockMapping>(strategy.Entries.Single().Value);
        Assert.Equal(new[] { "os", "dotnet" }, matrix.Entries.Select(e => e.Key.Value).ToArray());
        var steps = Assert.IsType<YamlBlockSequence>(test.Entries.Single(e => e.Key.Value == "steps").Value);
        var build = Assert.IsType<YamlBlockMapping>(steps.Items[1].Value);
        Assert.Equal("dotnet build -nologo -v:q\ndotnet test --no-build\n", ScalarValue(build, "run"));
        Assert.Equal(YamlScalarStyle.Literal, Assert.IsType<YamlScalar>(build.Entries[1].Value).Style);
    }

    [Fact]
    public void Manifest_has_two_documents_with_their_markers()
    {
        YamlDocument doc = Fixture("kubernetes.yaml");
        Assert.Equal(2, doc.Documents.Count);
        Assert.All(doc.Documents, d => Assert.NotNull(d.StartMarker));
        Assert.Equal("ConfigMap", ScalarValue(Assert.IsType<YamlBlockMapping>(doc.Documents[0].Root), "kind"));
        Assert.Equal("Deployment", ScalarValue(Assert.IsType<YamlBlockMapping>(doc.Documents[1].Root), "kind"));
        var configMap = Assert.IsType<YamlBlockMapping>(doc.Documents[0].Root);
        var data = Assert.IsType<YamlBlockMapping>(configMap.Entries.Single(e => e.Key.Value == "data").Value);
        Assert.Equal("{\n  \"logLevel\": \"info\"\n}\n", ScalarValue(data, "settings.json"));
    }

    [Fact]
    public void Appsettings_merge_keys_are_ordinary_entries_whose_values_are_aliases_or_sequences_of_them()
    {
        YamlBlockMapping root = RootMapping(Fixture("appsettings.yaml"));
        var development = Assert.IsType<YamlBlockMapping>(root.Entries.Single(e => e.Key.Value == "Development").Value);
        Assert.Equal("<<", development.Entries[0].Key.Value);
        Assert.Equal("defaults", Assert.IsType<YamlAlias>(development.Entries[0].Value).Name);
        var production = Assert.IsType<YamlBlockMapping>(root.Entries.Single(e => e.Key.Value == "Production").Value);
        var merged = Assert.IsType<YamlFlowSequence>(production.Entries[0].Value);
        Assert.Equal("defaults", Assert.IsType<YamlAlias>(Assert.Single(merged.Items)).Name);
        Assert.Equal("3", ScalarValue(production, "Retries"));
        Assert.Equal("yes", ScalarValue(production, "Enabled"));
        Assert.Equal(YamlCoreKind.Float, production.Entries[3].Value is YamlScalar s ? s.Kind : null);
    }

    [Fact]
    public void Crlf_file_reads_its_line_break_as_crlf_and_its_values_as_lf()
    {
        YamlDocument doc = Fixture("crlf.yaml");
        Assert.Equal("\r\n", doc.Source.Newline);
        YamlBlockMapping root = RootMapping(doc);
        Assert.Equal("first line\nsecond line\n", ScalarValue(root, "notes"));
        var items = Assert.IsType<YamlBlockSequence>(root.Entries.Single(e => e.Key.Value == "items").Value);
        Assert.Equal(new[] { "one", "two" }, items.Items.Select(i => Assert.IsType<YamlScalar>(i.Value).Value).ToArray());
    }
}
