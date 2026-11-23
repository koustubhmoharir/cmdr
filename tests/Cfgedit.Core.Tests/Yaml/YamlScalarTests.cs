using Cfgedit.Core.Yaml;
using static Cfgedit.Core.Tests.Yaml.YamlTestSupport;

namespace Cfgedit.Core.Tests.Yaml;

/// <summary>
/// The decoded value of each scalar form, from the YAML 1.2 rules: line folding for plain and quoted scalars, the escapes of double
/// quotes, the chomping of block scalars, and the indentation indicators. Each table row is a whole document with the value under <c>k</c>.
/// </summary>
public sealed class YamlScalarTests
{
    public static IEnumerable<object[]> Values()
    {
        // Plain scalars: a line break folds to a space, an empty line to a line feed, and comments end the scalar.
        yield return ["k: a\n  b\n\n  c\n", "a b\nc"];
        yield return ["k: a b  c\n", "a b  c"];
        yield return ["k: a # comment\n", "a"];
        yield return ["k: a#b\n", "a#b"];
        yield return ["k: a:b\n", "a:b"];
        yield return ["k: ~\n", "~"];
        yield return ["k: \t tabbed\t\n", "tabbed"];

        // Single quotes: '' is one quote, line breaks fold, and inner blanks are kept.
        yield return ["k: 'it''s'\n", "it's"];
        yield return ["k: 'a\n\n  b'\n", "a\nb"];
        yield return ["k: '  spaced  '\n", "  spaced  "];
        yield return ["k: ''\n", ""];

        // Double quotes: escapes, folding, and an escaped line break that joins the lines.
        yield return ["k: \"a\\x41\\u00E9\\U0001F600\\t\\\\\\\"\"\n", "aA\u00E9\U0001F600\t\\\""];
        yield return ["k: \"\\0\\a\\b\\e\\N\\_\\L\\P\\ \\/\"\n", "\0\a\b\u001B\u0085\u00A0\u2028\u2029 /"];
        yield return ["k: \"a\\\n  b\"\n", "ab"];
        yield return ["k: \"a \\\n\n  b\"\n", "a \nb"];
        yield return ["k: \"x\n\n  y\"\n", "x\ny"];
        yield return ["k: \"x\n  y\"\n", "x y"];
        yield return ["k: \"\"\n", ""];
        yield return ["k: \"a\\\tb\"\n", "a\tb"];

        // Literal blocks: the lines as written, with the chomping indicator deciding the trailing line breaks.
        yield return ["k: |\n  a\n  b\n", "a\nb\n"];
        yield return ["k: |-\n  a\n  b\n", "a\nb"];
        yield return ["k: |+\n  a\n\n\nn: 1\n", "a\n\n\n"];
        yield return ["k: |\n  a\n\n\nn: 1\n", "a\n"];
        yield return ["k: |-\n  a\n\n\nn: 1\n", "a"];
        yield return ["k: |\n  a\n   b\n  c\n", "a\n b\nc\n"];
        yield return ["k: |\n  a\n\n  b\n", "a\n\nb\n"];
        yield return ["k: |\n\n  a\n", "\na\n"];
        yield return ["k: |+\n\nn: 1\n", "\n"];
        yield return ["k: |\nn: 1\n", ""];
        yield return ["k: |\n  \ttabbed\n", "\ttabbed\n"];
        yield return ["k: |\n  # not a comment\n", "# not a comment\n"];

        // Indentation indicators: the content is indented by the parent's indentation plus the indicator.
        yield return ["k: |2\n    x\n", "  x\n"];
        yield return ["k: |2-\n   x\n", " x"];
        yield return ["k: |1+\n  x\n\n", " x\n\n"];

        // Folded blocks: lines join with a space; more indented lines and empty lines keep their breaks.
        yield return ["k: >\n  a\n  b\n\n  c\n", "a b\nc\n"];
        yield return ["k: >\n  a\n    b\n  c\n", "a\n  b\nc\n"];
        yield return ["k: >-\n  a\n\n  b\n", "a\nb"];
        yield return ["k: >+\n  a\n\n", "a\n\n"];
        yield return ["k: >\n\n  a\n", "\na\n"];
        yield return ["k: >\n  a\n  \n  b\n", "a\nb\n"];
    }

    [Theory]
    [MemberData(nameof(Values))]
    public void Scalar_value_decodes_as_yaml_1_2_defines(string text, string expected)
    {
        YamlBlockMapping mapping = RootMapping(Parse(text));
        Assert.Equal(expected, ScalarValue(mapping, "k"));
    }

    [Fact]
    public void Top_level_block_scalar_keeps_its_lines()
    {
        YamlDocument doc = Parse("--- |\n  top\n  line\n");
        Assert.Equal("top\nline\n", Assert.IsType<YamlScalar>(Assert.Single(doc.Documents).Root).Value);
    }

    [Fact]
    public void Block_scalar_in_a_sequence_uses_the_dash_column_as_its_parent_indentation()
    {
        YamlDocument doc = Parse("- |1\n  x\n- >\n  folded\n");
        var sequence = Assert.IsType<YamlBlockSequence>(Assert.Single(doc.Documents).Root);
        Assert.Equal(" x\n", Assert.IsType<YamlScalar>(sequence.Items[0].Value).Value);
        Assert.Equal("folded\n", Assert.IsType<YamlScalar>(sequence.Items[1].Value).Value);
    }

    [Fact]
    public void Block_scalar_style_and_plain_style_are_kept_on_the_node()
    {
        YamlBlockMapping mapping = RootMapping(Parse("a: |\n  x\nb: >\n  y\nc: 'q'\nd: plain\n"));
        Assert.Equal(YamlScalarStyle.Literal, Assert.IsType<YamlScalar>(mapping.Entries[0].Value).Style);
        Assert.Equal(YamlScalarStyle.Folded, Assert.IsType<YamlScalar>(mapping.Entries[1].Value).Style);
        Assert.Equal(YamlScalarStyle.SingleQuoted, Assert.IsType<YamlScalar>(mapping.Entries[2].Value).Style);
        Assert.Equal(YamlScalarStyle.Plain, Assert.IsType<YamlScalar>(mapping.Entries[3].Value).Style);
    }

    [Theory]
    [InlineData("k: 1\n", YamlCoreKind.Integer)]
    [InlineData("k: 0x1F\n", YamlCoreKind.Integer)]
    [InlineData("k: 1.5e3\n", YamlCoreKind.Float)]
    [InlineData("k: .inf\n", YamlCoreKind.Float)]
    [InlineData("k: true\n", YamlCoreKind.Boolean)]
    [InlineData("k: null\n", YamlCoreKind.Null)]
    [InlineData("k:\n", YamlCoreKind.Null)]
    [InlineData("k: text\n", YamlCoreKind.String)]
    [InlineData("k: 'true'\n", YamlCoreKind.String)]
    public void Plain_scalar_kind_follows_the_core_schema_and_quoted_is_a_string(string text, YamlCoreKind kind)
    {
        Assert.Equal(kind, RootMapping(Parse(text)).Entries[0].Value is YamlScalar s ? s.Kind : YamlCoreKind.Null);
    }

    [Fact]
    public void Tagged_plain_scalar_of_another_type_has_no_resolved_kind()
    {
        YamlScalar scalar = Assert.IsType<YamlScalar>(RootMapping(Parse("k: !!int 5\n")).Entries[0].Value);
        Assert.Null(scalar.Kind);
        Assert.Equal("5", scalar.Value);
    }
}
