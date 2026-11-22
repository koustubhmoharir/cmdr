using Cfgedit.Core;
using Cfgedit.Core.Model;
using Cfgedit.Core.Text;
using Cfgedit.Core.Toml;

namespace Cfgedit.Core.Tests.Toml;

/// <summary>
/// The TOML rules that the syntax does not check: duplicate keys, tables defined twice or through dotted keys, inline tables and
/// static arrays that are extended, and nesting. Each refused text gives the message and the line and column of the offending key or
/// header. The valid texts beside them show the rules' edges.
/// </summary>
public sealed class TomlSemanticTests
{
    [Theory]
    [InlineData("a = 1\nb = 2\na = 3\n", "duplicate key 'a'", 3, 1)]
    [InlineData("a.b = 1\na.b = 2\n", "duplicate key 'b'", 2, 3)]
    [InlineData("a = 1\n\"a\" = 2\n", "duplicate key 'a'", 2, 1)]
    [InlineData("[[a]]\nx = 1\nx = 2\n", "duplicate key 'x'", 3, 1)]
    [InlineData("a = { b = 1, b = 2 }\n", "duplicate key 'b'", 1, 14)]
    [InlineData("a = [{x = 1, x = 2}]\n", "duplicate key 'x'", 1, 14)]
    [InlineData("[a]\nb = 1\n[a.b]\n", "duplicate key 'b'", 3, 4)]
    [InlineData("a = 1\n[[a]]\n", "duplicate key 'a'", 2, 3)]
    public void Duplicate_keys_are_refused_at_the_second_key(string text, string message, int line, int column)
    {
        AssertRefused(text, message, line, column);
    }

    [Theory]
    [InlineData("[a]\nb = 1\n[a]\n", "table 'a' is already defined", 3, 2)]
    [InlineData("[a.b]\n[a]\n[a]\n", "table 'a' is already defined", 3, 2)]
    [InlineData("[fruit]\napple.color = \"red\"\n[fruit.apple]\n", "table 'fruit.apple' is already defined", 3, 8)]
    [InlineData("[t]\n[[t]]\n", "table 't' is already defined", 2, 3)]
    [InlineData("[[t]]\n[t]\n", "table 't' is already defined", 2, 2)]
    [InlineData("[[a.b]]\n[a]\nb.y = 2\n", "'a.b' is not a table", 3, 1)]
    [InlineData("[a.b.c]\nz = 9\n[a]\nb.c.t = 1\n", "table 'a.b.c' is already defined", 4, 3)]
    [InlineData("[a]\nx.y = 1\n[a.x]\n", "table 'a.x' is already defined", 3, 4)]
    public void A_table_defined_twice_or_through_a_defined_table_is_refused_at_the_header_or_dotted_key(
        string text,
        string message,
        int line,
        int column)
    {
        AssertRefused(text, message, line, column);
    }

    [Theory]
    [InlineData("a = 1\na.b = 2\n", "'a' is not a table", 2, 1)]
    [InlineData("a = 1\n[a.b]\n", "'a' is not a table", 2, 2)]
    [InlineData("a = [1]\n[a.c]\nfoo = 1\n", "'a' is not a table", 2, 2)]
    [InlineData("a = [{ b = 1 }]\n[a.c]\n", "'a' is not a table", 2, 2)]
    [InlineData("a = { b = 1 }\na.c = 2\n", "inline table 'a' cannot be extended", 2, 1)]
    [InlineData("a = {}\n[a.b]\n", "inline table 'a' cannot be extended", 2, 2)]
    [InlineData("a = {}\n[[a]]\n", "inline table 'a' cannot be extended", 2, 3)]
    [InlineData("inline-t = { nest = {} }\n\n[[inline-t.nest]]\n", "inline table 'inline-t' cannot be extended", 3, 3)]
    [InlineData("a = { b = 1, b.c = 2 }\n", "'a.b' is not a table", 1, 14)]
    [InlineData("tab = { inner = { dog = \"best\" }, inner.cat = \"worst\" }\n", "inline table 'tab.inner' cannot be extended", 1, 35)]
    [InlineData("fruit = []\n[[fruit]]\n", "cannot append to static array 'fruit'", 2, 3)]
    [InlineData("[a]\n[a.b]\n[a.b.c]\n[a.b]\n", "table 'a.b' is already defined", 4, 4)]
    public void A_non_table_inline_table_or_static_array_is_not_extended(string text, string message, int line, int column)
    {
        AssertRefused(text, message, line, column);
    }

    [Theory]
    [InlineData("[t]\nk = 1\n")]
    [InlineData("[a.b.c]\nz = 1\n[a]\nb.x = 2\n")]
    [InlineData("[a]\nx.y = 1\n[a.x.z]\nw = 2\n")]
    [InlineData("[fruit]\napple.color = \"red\"\n[fruit.apple.texture]\nsmooth = true\n")]
    [InlineData("a = { b.c = 1, b.d = 2 }\n")]
    [InlineData("[[a.b]]\n[[a.b]]\n[a.b.c]\nx = 1\n")]
    [InlineData("a.b.c = 1\na.d = 2\n")]
    [InlineData("[x]\n[x.y]\n[x.y.z]\n")]
    public void Texts_the_rules_allow_are_accepted(string text)
    {
        ConfigDocument document = new TomlFormat().Parse(TomlTestSupport.Utf8(text));
        Assert.NotNull(document.Root);
    }

    [Fact]
    public void Dotted_keys_nesting_deeper_than_256_tables_is_refused_at_the_segment_that_goes_deeper()
    {
        // The last segment is a value, not a table. The first 257 segments make tables, and the table at depth 257 starts at segment 257
        // (index 256), at column 1 + 2 * 256.
        string key = string.Join('.', Enumerable.Repeat("a", 258));

        AssertRefused(key + " = 1\n", "nesting is deeper than 256 levels", 1, 513);
    }

    [Fact]
    public void A_header_of_257_segments_is_refused_at_the_257th_segment()
    {
        string header = "[" + string.Join('.', Enumerable.Repeat("a", 257)) + "]\n";

        AssertRefused(header, "nesting is deeper than 256 levels", 1, 514);
    }

    [Fact]
    public void A_header_of_256_segments_is_accepted_and_agrees_with_tomlyn()
    {
        string header = "[" + string.Join('.', Enumerable.Repeat("a", 256)) + "]\nk = 1\n";

        ConfigDocument document = new TomlFormat().Parse(TomlTestSupport.Utf8(header));

        Assert.NotNull(document.Root);
    }

    [Fact]
    public void A_header_of_256_segments_with_an_array_of_256_levels_under_it_is_accepted_and_agrees_with_tomlyn()
    {
        // The table depth and the value depth are each capped at 256, and both apply here, so Tomlyn must allow both at once.
        string header = "[" + string.Join('.', Enumerable.Repeat("a", 256)) + "]\n";
        string value = "v = " + new string('[', 256) + new string(']', 256) + "\n";

        ConfigDocument document = new TomlFormat().Parse(TomlTestSupport.Utf8(header + value));

        Assert.NotNull(document.Root);
    }

    private static void AssertRefused(string text, string message, int line, int column)
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => new TomlFormat().Parse(TomlTestSupport.Utf8(text)));
        Assert.Equal(message, ex.Message);
        Assert.Equal(line, ex.Line);
        Assert.Equal(column, ex.Column);
    }
}
