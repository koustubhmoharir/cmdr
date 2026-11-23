using Cfgedit.Core;
using Cfgedit.Core.Model;
using Cfgedit.Core.Yaml;
using YamlDotNet.RepresentationModel;

namespace Cfgedit.Core.Tests.Yaml;

/// <summary>
/// The YAML cross-check at the level of its comparer: a difference is named by its path, both sides are described, and a mismatch is an
/// internal parse mismatch. The conversion of a YamlDotNet stream is checked against cfgedit's own logical tree for a text with an alias.
/// </summary>
public sealed class YamlCrossCheckTests
{
    private static LogicalNode Object(params LogicalNode[] children)
    {
        return new LogicalNode(LogicalKind.Object, null, null, null, [], children);
    }

    private static LogicalNode Integer(string name, string value)
    {
        return new LogicalNode(LogicalKind.Scalar, name, null, value, [], [], yamlKind: YamlValueKind.Integer, yamlPlain: true);
    }

    [Fact]
    public void Equal_trees_have_no_difference()
    {
        LogicalNode tree = Object(Integer("a", "1"));

        Assert.Null(YamlCrossCheck.FirstDifference(tree, Object(Integer("a", "1"))));
    }

    [Fact]
    public void A_value_difference_names_its_path_and_both_sides()
    {
        LogicalNode expected = Object(Integer("a", "2"));
        LogicalNode actual = Object(Integer("a", "1"));

        Assert.Equal(
            "at a: cfgedit has Integer \"2\" plain, YamlDotNet has Integer \"1\" plain",
            YamlCrossCheck.FirstDifference(expected, actual));
    }

    [Fact]
    public void A_tag_difference_is_a_difference_and_names_the_tag()
    {
        var expected = new LogicalNode(
            LogicalKind.Scalar,
            "x",
            null,
            "x",
            [],
            [],
            yamlKind: YamlValueKind.String,
            yamlPlain: true,
            yamlTag: YamlTags.CorePrefix + "str");
        var actual = new LogicalNode(LogicalKind.Scalar, "x", null, "x", [], [], yamlKind: YamlValueKind.String, yamlPlain: true);

        Assert.Equal(
            "at x: cfgedit has String \"x\" plain tagged tag:yaml.org,2002:str, YamlDotNet has String \"x\" plain",
            YamlCrossCheck.FirstDifference(Object(expected), Object(actual)));
    }

    [Fact]
    public void A_child_count_difference_is_named_at_its_mapping()
    {
        LogicalNode expected = Object(Integer("a", "1"), Integer("b", "2"));
        LogicalNode actual = Object(Integer("a", "1"));

        Assert.Equal("cfgedit has 2 entries, YamlDotNet has 1", YamlCrossCheck.FirstDifference(expected, actual));
    }

    [Fact]
    public void A_mismatch_is_an_internal_parse_mismatch_with_a_request_for_a_report()
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(
            () => YamlCrossCheck.Compare(Object(Integer("a", "2")), Object(Integer("a", "1"))));

        Assert.Equal(
            "internal parse mismatch: at a: cfgedit has Integer \"2\" plain, YamlDotNet has Integer \"1\" plain (please report this)",
            ex.Message);
    }

    [Fact]
    public void Verify_compares_the_text_with_the_given_data()
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => YamlCrossCheck.Verify("a: 1\n", Object(Integer("a", "3"))));

        Assert.StartsWith("internal parse mismatch: at a: ", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void YamlDotNet_reads_an_alias_as_the_repeat_of_its_anchor_and_the_conversion_matches_cfgedit()
    {
        const string text = "a: &x 1\nb: *x\n";
        var stream = new YamlStream();
        stream.Load(new StringReader(text));

        LogicalNode converted = YamlCrossCheck.FromYamlDotNet(stream);
        LogicalNode cfgedit = Object(
            new LogicalNode(LogicalKind.Scalar, "a", null, "1", [], [], yamlKind: YamlValueKind.Integer, yamlPlain: true, yamlAnchor: "x"),
            new LogicalNode(LogicalKind.Alias, "b", null, "x", [], []));

        Assert.Null(YamlCrossCheck.FirstDifference(cfgedit, converted));
    }
}
