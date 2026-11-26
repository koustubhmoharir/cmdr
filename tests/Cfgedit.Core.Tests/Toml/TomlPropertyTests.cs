using System.Text;
using Cfgedit.Core.Toml;
using Shared.Core;
using static Cfgedit.Core.Tests.Toml.TomlTestSupport;

namespace Cfgedit.Core.Tests.Toml;

/// <summary>
/// Every string the encoders accept survives writing and parsing: <c>k = </c> followed by the token and a line break decodes
/// to the same value, in every form, with LF and with CRLF as the document's line break. Strings with a lone surrogate are
/// refused. Keys round trip too. The corpus is the shared nasty strings and a seeded generator.
/// </summary>
public sealed class TomlPropertyTests
{
    private const int RandomSeed = 20261123;
    private const int RandomCount = 1000;

    private static readonly TomlStringForm[] Forms =
    [
        TomlStringForm.Basic,
        TomlStringForm.Literal,
        TomlStringForm.MultiLineBasic,
        TomlStringForm.MultiLineLiteral,
    ];

    public static IEnumerable<object[]> Newlines()
    {
        yield return ["\n"];
        yield return ["\r\n"];
    }

    [Theory]
    [MemberData(nameof(Newlines))]
    public void Every_accepted_string_round_trips_through_each_form(string newline)
    {
        int accepted = 0;
        foreach (string value in Corpus())
        {
            if (HasLoneSurrogate(value))
            {
                Assert.Throws<CfgeditException>(() => TomlEncoder.EncodeString(value, newline));
                foreach (TomlStringForm form in Forms)
                {
                    Assert.Throws<CfgeditException>(() => TomlEncoder.EncodeStringKeeping(value, form, newline));
                }

                continue;
            }

            accepted++;
            AssertRoundTrip(value, TomlEncoder.EncodeString(value, newline), newline);
            foreach (TomlStringForm form in Forms)
            {
                AssertRoundTrip(value, TomlEncoder.EncodeStringKeeping(value, form, newline), newline);
            }
        }

        Assert.True(accepted >= 100, $"the corpus should hold many accepted strings, found {accepted}");
    }

    [Theory]
    [MemberData(nameof(Newlines))]
    public void Every_accepted_name_round_trips_as_a_key(string newline)
    {
        int accepted = 0;
        foreach (string name in Corpus())
        {
            if (HasLoneSurrogate(name))
            {
                Assert.Throws<CfgeditException>(() => TomlEncoder.EncodeKey(name));
                continue;
            }

            accepted++;
            string key = TomlEncoder.EncodeKey(name);
            string text = key + " = 1" + newline;
            TomlDocument doc = Parse(text);
            var item = Assert.IsType<TomlKeyValueItem>(Assert.Single(doc.Items));
            TomlKeyPart part = Assert.Single(item.Key.Parts);
            Assert.Equal(name, part.Name);
            Assert.Equal(key, part.Raw);
            AssertTiles(doc);
        }

        Assert.True(accepted >= 100, $"the corpus should hold many accepted names, found {accepted}");
    }

    [Fact]
    public void Many_accepted_strings_in_one_document_keep_their_values_and_tile()
    {
        // Several strings in one document, one per line, keep their own values and the tiling.
        var sb = new StringBuilder();
        var values = new List<string>();
        foreach (string value in Corpus().Where(v => !HasLoneSurrogate(v)).Take(200))
        {
            values.Add(value);
            sb.Append('k').Append(values.Count).Append(" = ").Append(TomlEncoder.EncodeString(value, "\n")).Append('\n');
        }

        TomlDocument doc = Parse(sb.ToString());
        AssertTiles(doc);
        Assert.Equal(sb.ToString(), Reassemble(doc));
        Assert.Equal(values.Count, doc.Items.Count);
        for (int i = 0; i < values.Count; i++)
        {
            var item = Assert.IsType<TomlKeyValueItem>(doc.Items[i]);
            Assert.Equal(values[i], ((TomlString)item.Value).Value);
        }
    }

    private static void AssertRoundTrip(string value, string token, string newline)
    {
        string text = "k = " + token + newline;
        TomlDocument doc = Parse(text);
        var item = Assert.IsType<TomlKeyValueItem>(Assert.Single(doc.Items));
        Assert.Equal(value, Assert.IsType<TomlString>(item.Value).Value);
        Assert.Equal(token, doc.Source.Text.Substring(item.Value.Start, item.Value.End - item.Value.Start));
        AssertTiles(doc);
        Assert.Equal(text, Reassemble(doc));
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
    /// Random strings that mix the characters the encoders treat differently: quotation marks, apostrophes, backslashes, spaces and
    /// tabs, line feeds and carriage returns, control characters (tab excepted), DEL, U+2028 and U+2029, Latin-1, astral
    /// characters, and occasionally a lone surrogate, which is refused.
    /// </summary>
    private static string RandomString(Random random)
    {
        var sb = new StringBuilder();
        int length = random.Next(0, 30);
        for (int i = 0; i < length; i++)
        {
            switch (random.Next(16))
            {
                case 0:
                case 1:
                case 2:
                    sb.Append((char)random.Next('a', 'z' + 1));
                    break;
                case 3:
                    sb.Append("\"'"[random.Next(2)]);
                    break;
                case 4:
                    sb.Append("\"\"\"'''"[random.Next(6)]);
                    break;
                case 5:
                    sb.Append('\\');
                    break;
                case 6:
                    sb.Append(" \t"[random.Next(2)]);
                    break;
                case 7:
                    sb.Append('\n');
                    break;
                case 8:
                    sb.Append('\r');
                    break;
                case 9:
                    sb.Append((char)random.Next(0x00, 0x20));
                    break;
                case 10:
                    sb.Append('\u007F');
                    break;
                case 11:
                    sb.Append((char)(random.Next(2) == 0 ? 0x2028 : 0x2029));
                    break;
                case 12:
                    sb.Append((char)random.Next(0x80, 0x100));
                    break;
                case 13:
                    sb.Append(char.ConvertFromUtf32(random.Next(0x10000, 0x10FFFF)));
                    break;
                case 14:
                    const string punctuation = "#=]$`{}[,:.";
                    sb.Append(punctuation[random.Next(punctuation.Length)]);
                    break;
                default:
                    sb.Append((char)random.Next(0xD800, 0xE000));
                    break;
            }
        }

        return sb.ToString();
    }
}
