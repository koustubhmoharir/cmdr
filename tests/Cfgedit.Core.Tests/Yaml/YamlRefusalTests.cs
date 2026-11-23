using Cfgedit.Core;
using Cfgedit.Core.Text;
using Cfgedit.Core.Yaml;
using static Cfgedit.Core.Tests.Yaml.YamlTestSupport;

namespace Cfgedit.Core.Tests.Yaml;

/// <summary>
/// Each refusal and each syntax error: the message names the construct, and the line and column point at it. Outside the subset,
/// the parser refuses rather than reads.
/// </summary>
public sealed class YamlRefusalTests
{
    [Fact]
    public void Complex_key_is_refused()
    {
        AssertError("? a\n: b\n", "complex keys (?) are not supported", 1, 1);
    }

    [Fact]
    public void Collection_as_a_key_is_refused()
    {
        AssertError("[a]: b\n", "a collection as a key is not supported", 1, 1);
        AssertError("{a: 1}: b\n", "a collection as a key is not supported", 1, 1);
    }

    [Fact]
    public void Alias_as_a_key_is_refused()
    {
        AssertError("x: &a 1\n*a : b\n", "an alias as a key is not supported", 2, 1);
    }

    [Fact]
    public void Key_that_spans_lines_is_refused()
    {
        AssertError("\"a\n b\": c\n", "a key that spans lines is not supported", 1, 1);
        AssertError("{a\n: b}\n", "a key that spans lines is not supported", 1, 2);
    }

    [Fact]
    public void Empty_key_is_refused()
    {
        AssertError("a: 1\n: x\n", "an empty key is not supported", 2, 1);
        AssertError("{: x}\n", "an empty key is not supported", 1, 2);
    }

    [Fact]
    public void Duplicate_string_keys_are_refused_at_the_second_key()
    {
        AssertError("a: 1\na: 2\n", "duplicate key 'a'", 2, 1);
        AssertError("a: 1\n\"a\": 2\n", "duplicate key 'a'", 2, 1);
        AssertError("{a: 1, a: 2}\n", "duplicate key 'a'", 1, 8);
    }

    [Fact]
    public void Two_keys_that_are_not_strings_are_refused_as_possible_duplicates()
    {
        AssertError("1: a\n2: b\n", "a mapping with more than one key that is not a string is not supported", 2, 1);
    }

    [Fact]
    public void A_quoted_key_and_a_number_with_the_same_text_are_different_keys()
    {
        YamlBlockMapping mapping = RootMapping(Parse("1: a\n\"1\": b\n"));
        Assert.Equal(2, mapping.Entries.Count);
        Assert.Equal(YamlCoreKind.Integer, mapping.Entries[0].Key.Kind);
        Assert.Equal(YamlCoreKind.String, mapping.Entries[1].Key.Kind);
    }

    [Fact]
    public void Undefined_alias_is_refused_with_its_name()
    {
        AssertError("a: *x\n", "alias *x is not defined", 1, 4);
    }

    [Fact]
    public void Alias_to_an_anchor_that_contains_it_is_refused()
    {
        AssertError("a: &x [*x]\n", "alias *x refers to an anchor that contains it, which is not supported", 1, 8);
    }

    [Fact]
    public void Tab_in_indentation_is_refused()
    {
        AssertError("a:\n\tb: 1\n", "tabs are not allowed for indentation", 2, 1);
    }

    [Fact]
    public void Unterminated_double_quoted_scalar_is_refused()
    {
        AssertError("a: \"x\n", "unterminated double-quoted string", 1, 4);
    }

    [Fact]
    public void Unterminated_single_quoted_scalar_is_refused()
    {
        AssertError("a: 'x\n", "unterminated single-quoted string", 1, 4);
    }

    [Fact]
    public void Invalid_escape_is_refused_at_the_backslash()
    {
        AssertError("a: \"\\q\"\n", "invalid escape '\\q'", 1, 5);
    }

    [Fact]
    public void Escape_that_is_not_a_unicode_character_is_refused()
    {
        AssertError("a: \"\\uD800\"\n", "an escape is not a valid unicode character", 1, 5);
    }

    [Fact]
    public void Unterminated_flow_sequence_and_mapping_are_refused_at_their_opening_bracket()
    {
        AssertError("a: [1, 2\n", "unterminated flow sequence, expected ']'", 1, 4);
        AssertError("{a: 1", "unterminated flow mapping, expected '}'", 1, 1);
    }

    [Fact]
    public void Missing_comma_in_a_flow_sequence_is_refused()
    {
        AssertError("[\"a\" \"b\"]\n", "expected ',' or ']' in a flow sequence", 1, 6);
    }

    [Fact]
    public void Document_marker_inside_a_flow_collection_is_refused()
    {
        AssertError("[a,\n---\n]\n", "a document marker cannot be inside a flow collection", 2, 1);
    }

    [Fact]
    public void Single_pair_mapping_in_a_flow_sequence_is_refused()
    {
        AssertError("[a: 1]\n", "a single-pair mapping in a flow sequence is not supported", 1, 2);
    }

    [Fact]
    public void Entry_indented_less_than_its_mapping_ends_the_mapping_and_a_stray_line_is_refused()
    {
        AssertError("a:\n  b: 1\n c: 2\n", "bad indentation of a mapping entry", 3, 2);
    }

    [Fact]
    public void A_line_indented_more_than_a_plain_value_continues_it_and_a_colon_after_it_is_refused()
    {
        AssertError("a: 1\n  b: 2\n", "a key that spans lines is not supported", 1, 4);
    }

    [Fact]
    public void Key_without_a_colon_is_refused()
    {
        AssertError("a: 1\nb\n", "expected ':' after the key", 2, 2);
    }

    [Fact]
    public void Block_sequence_on_the_line_of_its_key_is_refused()
    {
        AssertError("a: - x\n", "a block sequence cannot start on the same line as its key", 1, 4);
    }

    [Fact]
    public void Mapping_value_on_the_line_of_a_key_is_refused()
    {
        AssertError("a: b: c\n", "mapping values are not allowed here", 1, 4);
    }

    [Fact]
    public void Text_after_a_quoted_value_is_refused()
    {
        AssertError("a: \"x\" y\n", "unexpected text after the value", 1, 8);
    }

    [Fact]
    public void Block_scalar_header_with_extra_text_is_refused()
    {
        AssertError("a: |x\n", "unexpected text after the block scalar header", 1, 5);
    }

    [Fact]
    public void Indentation_indicator_zero_is_refused()
    {
        AssertError("a: |0\n", "the indentation indicator of a block scalar must be 1 to 9", 1, 5);
    }

    [Fact]
    public void Directive_must_be_followed_by_an_explicit_start()
    {
        AssertError("%YAML 1.2\na: 1\n", "a directive must be followed by '---'", 2, 1);
    }

    [Fact]
    public void Yaml_1_1_directive_is_refused_with_its_version()
    {
        AssertError("%YAML 1.1\n---\na: 1\n", "YAML version 1.1 is not supported", 1, 7);
    }

    [Fact]
    public void Unknown_directive_is_refused()
    {
        AssertError("%FOO bar\n---\n", "the %FOO directive is not supported", 1, 1);
    }

    [Fact]
    public void Undefined_tag_handle_is_refused()
    {
        AssertError("a: !x!y z\n", "tag handle !x! is not defined", 1, 4);
    }

    [Fact]
    public void Anchor_without_a_name_is_refused()
    {
        AssertError("a: & x\n", "an anchor needs a name", 1, 4);
    }

    [Fact]
    public void Control_character_is_refused_wherever_it_is()
    {
        AssertError("a: \u0001\n", "character U+0001 is not allowed in YAML", 1, 4);
        AssertError("# \u007F\n", "character U+007F is not allowed in YAML", 1, 3);
    }

    [Fact]
    public void Lone_surrogate_is_refused()
    {
        AssertError("a: \uD800\n", "a lone surrogate is not allowed in YAML", 1, 4);
    }

    [Fact]
    public void Implicit_key_longer_than_1024_characters_is_refused()
    {
        string key = new string('k', 1025);
        AssertError(key + ": v\n", "an implicit key is longer than 1024 characters", 1, 1);
    }

    [Fact]
    public void Text_after_the_document_end_is_refused()
    {
        AssertError("a\n... x\n", "unexpected text after the value", 2, 5);
    }

    [Fact]
    public void Text_that_is_not_a_yaml_node_is_refused()
    {
        AssertError("a: @x\n", "unexpected character '@'", 1, 4);
    }

    [Fact]
    public void Comment_that_does_not_follow_a_space_is_refused()
    {
        AssertError("a: 'x'#c\n", "a comment must be preceded by a space", 1, 7);
        AssertError("[ a, b, c, ]#invalid\n", "a comment must be preceded by a space", 1, 13);
        AssertError("k: |#c\n  x\n", "a comment must be preceded by a space", 1, 5);
    }

    [Fact]
    public void Tab_only_line_in_a_block_scalar_is_refused()
    {
        AssertError("foo: |\n\t\nbar: 1\n", "tabs are not allowed for indentation", 2, 1);
    }

    [Fact]
    public void Tab_in_the_indentation_of_a_flow_collection_line_is_refused()
    {
        AssertError("- [\n\tfoo,\n foo\n ]\n", "bad indentation in a flow collection", 2, 2);
    }

    [Fact]
    public void Compact_collection_after_a_tab_is_refused()
    {
        AssertError("-\t-\n", "tabs are not allowed for indentation", 1, 3);
    }

    [Fact]
    public void Non_utf8_source_is_refused()
    {
        var source = SourceText.Create("a: 1\n", System.Text.Encoding.Unicode, false);
        CfgeditException ex = Assert.Throws<CfgeditException>(() => YamlDocument.Parse(source));
        Assert.Equal("a YAML file must be UTF-8", ex.Message);
    }
}
