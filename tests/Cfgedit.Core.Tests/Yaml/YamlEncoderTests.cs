using System.Text;
using Cfgedit.Core;
using Cfgedit.Core.Yaml;
using static Cfgedit.Core.Tests.Yaml.YamlTestSupport;

namespace Cfgedit.Core.Tests.Yaml;

/// <summary>
/// The encoders: the style each value is written in, the exact text of the common cases, the core-schema kinds, and the round trip
/// property. Every string the encoders accept reads back as itself, as the value of an entry at any indentation, with LF and with CRLF.
/// </summary>
public sealed class YamlEncoderTests
{
    private const int RandomSeed = 20261123;
    private const int RandomCount = 1000;

    private static readonly YamlScalarStyle?[] Preferences =
    [
        null,
        YamlScalarStyle.Plain,
        YamlScalarStyle.SingleQuoted,
        YamlScalarStyle.DoubleQuoted,
        YamlScalarStyle.Literal,
        YamlScalarStyle.Folded,
    ];

    /// <summary>The rows name the preferred style: "auto" for none, or the style's name.</summary>
    public static IEnumerable<object[]> Scalars()
    {
        yield return ["abc", "auto", "abc"];
        yield return ["", "auto", "''"];
        yield return ["true", "Plain", "'true'"];
        yield return ["x", "SingleQuoted", "'x'"];
        yield return ["x", "DoubleQuoted", "\"x\""];
        yield return ["a: b", "auto", "'a: b'"];
        yield return ["a #b", "auto", "'a #b'"];
        yield return ["a#b", "auto", "a#b"];
        yield return ["it's", "auto", "it's"];
        yield return ["'x", "auto", "'''x'"];
        yield return ["- item", "auto", "'- item'"];
        yield return ["[x]", "auto", "'[x]'"];
        yield return ["  sp  ", "auto", "'  sp  '"];
        yield return ["a\tb", "auto", "a\tb"];
        yield return ["a\\b", "DoubleQuoted", "\"a\\\\b\""];
        yield return ["\u0001", "auto", "\"\\x01\""];
        yield return ["\u007F", "auto", "\"\\x7F\""];
        yield return ["\uFEFF", "auto", "\"\\uFEFF\""];
        yield return ["a\rb", "auto", "\"a\\rb\""];
        yield return ["tab\there\n", "auto", "|\n  tab\there"];
        yield return ["a\nb", "auto", "|-\n  a\n  b"];
        yield return ["a\n", "auto", "|\n  a"];
        yield return ["a\n\n", "auto", "|+\n  a\n"];
        yield return ["\n", "auto", "|+\n"];
        yield return [" lead\nx", "auto", "|2-\n   lead\n  x"];
        yield return ["a\nb", "SingleQuoted", "|-\n  a\n  b"];
        yield return ["x", "Literal", "|-\n  x"];
        yield return ["x", "Folded", "|-\n  x"];
        yield return ["a\n", "Folded", "|\n  a"];
    }

    [Theory]
    [MemberData(nameof(Scalars))]
    public void Scalar_is_written_in_the_plainest_style_that_holds_it(string value, string preferredName, string expected)
    {
        YamlScalarStyle? preferred = preferredName == "auto" ? null : Enum.Parse<YamlScalarStyle>(preferredName);
        Assert.Equal(expected, YamlEncoder.EncodeScalar(value, preferred, 0, "\n", mustStayString: true));
    }

    [Fact]
    public void Literal_block_lines_sit_at_the_owner_column_plus_two_with_crlf_breaks()
    {
        string text = YamlEncoder.EncodeScalar("a\nb\n", null, 4, "\r\n", mustStayString: true);
        Assert.Equal("|\r\n      a\r\n      b", text);
    }

    [Fact]
    public void Plain_text_that_reads_as_a_number_or_a_boolean_is_quoted_when_it_must_stay_a_string()
    {
        Assert.Equal("'1.5'", YamlEncoder.EncodeScalar("1.5", YamlScalarStyle.Plain, 0, "\n", mustStayString: true));
        Assert.Equal("1.5", YamlEncoder.EncodeScalar("1.5", YamlScalarStyle.Plain, 0, "\n", mustStayString: false));
        Assert.Equal("'null'", YamlEncoder.EncodeScalar("null", null, 0, "\n", mustStayString: true));
    }

    [Fact]
    public void A_lone_surrogate_is_refused_in_every_style()
    {
        foreach (YamlScalarStyle? preferred in Preferences)
        {
            Assert.Throws<CfgeditException>(() => YamlEncoder.EncodeScalar("a\uD800", preferred, 0, "\n", mustStayString: true));
        }

        Assert.Throws<CfgeditException>(() => YamlEncoder.EncodeKey("a\uD800"));
    }

    [Fact]
    public void A_surrogate_pair_is_written_as_itself()
    {
        Assert.Equal("\"\U0001F600\"", YamlEncoder.EncodeScalar("\U0001F600", YamlScalarStyle.DoubleQuoted, 0, "\n", mustStayString: true));
        Assert.Equal("\U0001F600", YamlEncoder.EncodeScalar("\U0001F600", null, 0, "\n", mustStayString: true));
    }

    public static IEnumerable<object[]> Keys()
    {
        yield return ["a b", "a b"];
        yield return ["name", "name"];
        yield return ["", "\"\""];
        yield return ["true", "\"true\""];
        yield return ["1.5", "\"1.5\""];
        yield return ["null", "\"null\""];
        yield return ["a: b", "\"a: b\""];
        yield return ["-x", "\"-x\""];
        yield return ["a\nb", "\"a\\nb\""];
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void Key_is_written_plain_when_it_stays_a_string_and_double_quoted_otherwise(string name, string expected)
    {
        Assert.Equal(expected, YamlEncoder.EncodeKey(name));
    }

    public static IEnumerable<object[]> Kinds()
    {
        yield return ["", YamlCoreKind.Null];
        yield return ["~", YamlCoreKind.Null];
        yield return ["null", YamlCoreKind.Null];
        yield return ["NULL", YamlCoreKind.Null];
        yield return ["true", YamlCoreKind.Boolean];
        yield return ["False", YamlCoreKind.Boolean];
        yield return ["TRUE", YamlCoreKind.Boolean];
        yield return ["yes", YamlCoreKind.String];
        yield return ["1", YamlCoreKind.Integer];
        yield return ["-12", YamlCoreKind.Integer];
        yield return ["+7", YamlCoreKind.Integer];
        yield return ["0o17", YamlCoreKind.Integer];
        yield return ["0x1F", YamlCoreKind.Integer];
        yield return ["0b1", YamlCoreKind.String];
        yield return ["1_000", YamlCoreKind.String];
        yield return ["1.5", YamlCoreKind.Float];
        yield return [".5", YamlCoreKind.Float];
        yield return ["1.", YamlCoreKind.Float];
        yield return ["1e3", YamlCoreKind.Float];
        yield return ["1.0E+3", YamlCoreKind.Float];
        yield return [".inf", YamlCoreKind.Float];
        yield return ["-.Inf", YamlCoreKind.Float];
        yield return [".NaN", YamlCoreKind.Float];
        yield return [".", YamlCoreKind.String];
        yield return ["+", YamlCoreKind.String];
        yield return ["1.2.3", YamlCoreKind.String];
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Core_schema_kind_of_plain_text(string text, YamlCoreKind expected)
    {
        Assert.Equal(expected, YamlEncoder.CoreSchemaKind(text));
    }

    /// <summary>
    /// For every string the encoders accept, the text written as an entry's value reads back as the same string, at each indentation
    /// and with each line break, in each preferred style, and a value written plain stays a string when it must.
    /// </summary>
    [Fact]
    public void Every_accepted_string_round_trips_as_an_entry_value()
    {
        int accepted = 0;
        foreach (string value in Corpus())
        {
            if (HasLoneSurrogate(value))
            {
                foreach (YamlScalarStyle? preferred in Preferences)
                {
                    Assert.Throws<CfgeditException>(() => YamlEncoder.EncodeScalar(value, preferred, 0, "\n", mustStayString: true));
                }

                continue;
            }

            accepted++;
            foreach (int indent in new[] { 0, 2, 4 })
            {
                foreach (string newline in new[] { "\n", "\r\n" })
                {
                    foreach (YamlScalarStyle? preferred in Preferences)
                    {
                        foreach (bool mustStay in new[] { true, false })
                        {
                            AssertEntryRoundTrips(value, preferred, indent, newline, mustStay);
                        }
                    }
                }
            }
        }

        Assert.True(accepted >= 100, $"the corpus should hold many accepted strings, found {accepted}");
    }

    [Fact]
    public void Every_accepted_string_round_trips_as_a_key()
    {
        foreach (string name in Corpus().Where(name => !HasLoneSurrogate(name)))
        {
            string key;
            try
            {
                key = YamlEncoder.EncodeKey(name);
            }
            catch (CfgeditException ex)
            {
                // A key too long to be an implicit key is refused, not written.
                Assert.Equal("a key longer than 1024 characters cannot be written", ex.Message);
                Assert.True(name.Length >= 1000);
                continue;
            }

            string text = key + ": 1\n";
            YamlDocument doc = Parse(text);
            YamlMappingEntry entry = Assert.Single(RootMapping(doc).Entries);
            Assert.Equal(name, entry.Key.Value);
            Assert.Equal(YamlCoreKind.String, entry.Key.Kind);
        }
    }

    [Fact]
    public void Entries_written_one_after_another_keep_their_values()
    {
        var values = Corpus().Where(v => !HasLoneSurrogate(v)).Take(200).ToList();
        var sb = new StringBuilder();
        for (int i = 0; i < values.Count; i++)
        {
            sb.Append("k").Append(i).Append(": ").Append(YamlEncoder.EncodeScalar(values[i], null, 0, "\n", mustStayString: true)).Append('\n');
        }

        YamlDocument doc = Parse(sb.ToString());
        AssertTiles(doc);
        Assert.Equal(sb.ToString(), Reassemble(doc));
        YamlBlockMapping mapping = RootMapping(doc);
        Assert.Equal(values.Count, mapping.Entries.Count);
        for (int i = 0; i < values.Count; i++)
        {
            Assert.Equal(values[i], Assert.IsType<YamlScalar>(mapping.Entries[i].Value).Value);
        }
    }

    [Fact]
    public void Seeded_random_strings_round_trip_as_entry_values()
    {
        var random = new Random(RandomSeed);
        for (int i = 0; i < RandomCount; i++)
        {
            string value = RandomText(random);
            YamlScalarStyle? preferred = Preferences[random.Next(Preferences.Length)];
            int indent = new[] { 0, 2, 4 }[random.Next(3)];
            string newline = random.Next(2) == 0 ? "\n" : "\r\n";
            AssertEntryRoundTrips(value, preferred, indent, newline, mustStayString: random.Next(2) == 0);
        }
    }

    private static void AssertEntryRoundTrips(string value, YamlScalarStyle? preferred, int indent, string newline, bool mustStayString)
    {
        string encoded = YamlEncoder.EncodeScalar(value, preferred, indent, newline, mustStayString);
        string pad = new(' ', indent);
        string text = pad + "k: " + encoded + newline + pad + "next: 1" + newline;
        YamlDocument doc = Parse(text);
        AssertTiles(doc);
        YamlBlockMapping mapping = RootMapping(doc);
        Assert.Equal(2, mapping.Entries.Count);
        YamlScalar scalar = Assert.IsType<YamlScalar>(mapping.Entries[0].Value);
        Assert.True(
            scalar.Value == value,
            $"value {Show(value)} written as {Show(encoded)} (indent {indent}, preferred {preferred}, mustStayString {mustStayString}) read back as {Show(scalar.Value)}");
        if (mustStayString && scalar.Style == YamlScalarStyle.Plain)
        {
            Assert.Equal(YamlCoreKind.String, scalar.Kind);
        }
    }

    private static string Show(string text) => "\"" + text.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";

    /// <summary>The shared nasty strings, then the seeded random ones.</summary>
    private static IEnumerable<string> Corpus()
    {
        foreach (string value in NastyStrings.All)
        {
            yield return value;
        }

        var random = new Random(RandomSeed);
        for (int i = 0; i < RandomCount; i++)
        {
            yield return RandomText(random);
        }
    }

    private static readonly string[] Alphabet =
    [
        "a", "Z", "0", "9", " ", "  ", "\t", "\n", "\r\n", "\r", "'", "\"", "\\", ":", "#", "-", "?", ",", "[", "]", "{", "}",
        "&", "*", "!", "|", ">", "%", "@", "`", "~", ".", "_", "\u0001", "\u007F", "\u0085", "\u00A0", "\u2028", "\uFEFF",
        "\u00E9", "\uFFFD", "\U0001F600", "\u200D", "\u0301",
    ];

    private static string RandomText(Random random)
    {
        int length = random.Next(0, 13);
        var sb = new StringBuilder();
        for (int i = 0; i < length; i++)
        {
            sb.Append(Alphabet[random.Next(Alphabet.Length)]);
        }

        return sb.ToString();
    }

    private static bool HasLoneSurrogate(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                return true;
            }
        }

        return false;
    }
}
