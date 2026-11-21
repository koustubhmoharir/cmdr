using System.Text;
using Cfgedit.Core.Json;
using static Cfgedit.Core.Tests.Json.JsonTestSupport;

namespace Cfgedit.Core.Tests.Json;

/// <summary>
/// Every string, from the nasty corpus and from a seeded generator, survives encoding and parsing as a root string, as a
/// member value and as a member key, in each quote that each dialect allows.
/// </summary>
public sealed class JsonPropertyTests
{
    private const int RandomSeed = 20260602;
    private const int RandomCount = 1000;

    // U+2028 and U+2029 are written as casts so that no source file holds the raw characters, which C# treats as line breaks.
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    public static IEnumerable<object[]> Combinations()
    {
        yield return [JsonDialect.Json, '"'];
        yield return [JsonDialect.Jsonc, '"'];
        yield return [JsonDialect.Json5, '"'];
        yield return [JsonDialect.Json5, '\''];
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Every_string_parses_as_a_root_string_with_the_same_value(JsonDialect dialect, char quote)
    {
        foreach (string value in Corpus())
        {
            string literal = JsonSyntax.EncodeString(value, quote, dialect);

            JsonDocument doc = Parse(literal, dialect);

            AssertString(doc.Root, literal, value, quote);
        }
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Every_string_parses_as_a_member_value_with_the_same_value(JsonDialect dialect, char quote)
    {
        foreach (string value in Corpus())
        {
            string literal = JsonSyntax.EncodeString(value, quote, dialect);

            JsonDocument doc = Parse("{\"k\": " + literal + "}", dialect);

            JsonObject root = Assert.IsType<JsonObject>(doc.Root);
            JsonMember member = Assert.IsType<JsonMember>(Assert.Single(root.Entries));
            Assert.Equal("k", member.Key.Name);
            AssertString(member.Value, literal, value, quote);
        }
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Every_string_parses_as_a_member_key_with_the_same_name(JsonDialect dialect, char quote)
    {
        foreach (string value in Corpus())
        {
            string literal = JsonSyntax.EncodeString(value, quote, dialect);

            JsonDocument doc = Parse("{" + literal + ": 1}", dialect);

            JsonObject root = Assert.IsType<JsonObject>(doc.Root);
            JsonMember member = Assert.IsType<JsonMember>(Assert.Single(root.Entries));
            Assert.Equal(value, member.Key.Name);
            Assert.Equal(quote, member.Key.Quote);
            Assert.Equal(literal.Length, member.Key.End - member.Key.Start);
        }
    }

    private static IEnumerable<string> Corpus()
    {
        foreach (string value in NastyStrings.All)
        {
            yield return value;
        }

        var random = new Random(RandomSeed);
        for (int n = 0; n < RandomCount; n++)
        {
            yield return RandomString(random);
        }
    }

    /// <summary>
    /// Random strings that mix every class of character the encoder treats differently: printable ASCII with both quotes,
    /// control characters, backslashes, U+2028 and U+2029, surrogate code units (lone or paired), astral characters,
    /// combining marks and Latin-1. The expectation is the input itself.
    /// </summary>
    private static string RandomString(Random random)
    {
        var sb = new StringBuilder();
        int length = random.Next(0, 40);
        for (int i = 0; i < length; i++)
        {
            switch (random.Next(12))
            {
                case 0:
                case 1:
                case 2:
                    sb.Append((char)random.Next(0x20, 0x7F));
                    break;
                case 3:
                    sb.Append((char)random.Next(0x00, 0x20));
                    break;
                case 4:
                    sb.Append(random.Next(2) == 0 ? '"' : '\'');
                    break;
                case 5:
                    sb.Append(random.Next(2) == 0 ? '\\' : '/');
                    break;
                case 6:
                    sb.Append(random.Next(2) == 0 ? LineSeparator : ParagraphSeparator);
                    break;
                case 7:
                    sb.Append((char)random.Next(0xD800, 0xE000));
                    break;
                case 8:
                    sb.Append(char.ConvertFromUtf32(random.Next(0x10000, 0x110000)));
                    break;
                case 9:
                    sb.Append((char)random.Next(0x80, 0x800));
                    break;
                case 10:
                    sb.Append((char)random.Next(0x300, 0x370));
                    break;
                default:
                    sb.Append((char)random.Next(0x7F, 0x100));
                    break;
            }
        }

        return sb.ToString();
    }
}
