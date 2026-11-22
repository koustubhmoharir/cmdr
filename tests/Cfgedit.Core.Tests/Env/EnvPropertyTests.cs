using System.Text;
using Cfgedit.Core.Env;
using static Cfgedit.Core.Tests.Env.EnvTestSupport;

namespace Cfgedit.Core.Tests.Env;

/// <summary>
/// Every string the encoder accepts survives writing and parsing: <c>K=</c> followed by the token and a line break decodes to
/// the same value, in every quote style, with LF and with CRLF as the document's line break. Strings with a control character
/// the encoder refuses are refused. The corpus is the shared nasty strings and a seeded generator.
/// </summary>
public sealed class EnvPropertyTests
{
    private const int RandomSeed = 20261123;
    private const int RandomCount = 1000;

    // U+2028 and U+2029 are written as casts so that no source file holds the raw characters, which C# treats as line breaks.
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    public static IEnumerable<object[]> Newlines()
    {
        yield return ["\n"];
        yield return ["\r\n"];
    }

    [Theory]
    [MemberData(nameof(Newlines))]
    public void Every_accepted_string_round_trips_through_each_style(string newline)
    {
        int accepted = 0;
        foreach (string value in Corpus())
        {
            if (HasRefusedControl(value))
            {
                Assert.Throws<CfgeditException>(() => EnvEncoder.Encode(value, newline));
                continue;
            }

            accepted++;
            AssertRoundTrip(value, EnvEncoder.Encode(value, newline), newline);
            AssertRoundTrip(value, EnvEncoder.EncodeKeeping(value, EnvQuote.None, newline), newline);
            AssertRoundTrip(value, EnvEncoder.EncodeKeeping(value, EnvQuote.Single, newline), newline);
            AssertRoundTrip(value, EnvEncoder.EncodeKeeping(value, EnvQuote.Double, newline), newline);
        }

        Assert.True(accepted >= 100, $"the corpus should hold many accepted strings, found {accepted}");
    }

    [Fact]
    public void Double_quotes_keep_the_style_they_are_asked_for()
    {
        foreach (string value in Corpus().Where(v => !HasRefusedControl(v)))
        {
            Assert.Equal(EnvQuote.Double, EnvEncoder.StyleOf(EnvEncoder.EncodeKeeping(value, EnvQuote.Double, "\n")));
        }
    }

    private static void AssertRoundTrip(string value, string token, string newline)
    {
        string text = "K=" + token + newline;
        EnvDocument doc = Parse(text);

        EnvEntry entry = Assert.IsType<EnvEntry>(Assert.Single(doc.Items));
        Assert.Equal("K", entry.Key);
        Assert.Equal(value, entry.Value);
        Assert.Equal(EnvEncoder.StyleOf(token), entry.Quote);
        Assert.Equal(token, Slice(doc, entry.ValueSpan));
        Assert.Equal(text, Reassemble(doc));
    }

    private static bool HasRefusedControl(string value)
    {
        foreach (char c in value)
        {
            if ((c <= '\u001F' || c == '\u007F') && c is not ('\t' or '\n' or '\r'))
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
    /// Random strings that mix every class of character the encoder treats differently: the value's own quotes, backslashes,
    /// <c>$</c>, <c>#</c>, backticks, <c>=</c>, spaces and tabs at any position, line breaks, U+2028 and U+2029, control
    /// characters (some refused), surrogate code units (lone or paired), astral characters and Latin-1.
    /// </summary>
    private static string RandomString(Random random)
    {
        var sb = new StringBuilder();
        int length = random.Next(0, 30);
        for (int i = 0; i < length; i++)
        {
            switch (random.Next(14))
            {
                case 0:
                case 1:
                case 2:
                    sb.Append((char)random.Next('a', 'z' + 1));
                    break;
                case 3:
                    sb.Append(" \t"[random.Next(2)]);
                    break;
                case 4:
                    sb.Append("#'\"\\$`="[random.Next(7)]);
                    break;
                case 5:
                    sb.Append(random.Next(2) == 0 ? '\n' : '\r');
                    break;
                case 6:
                    sb.Append(random.Next(2) == 0 ? LineSeparator : ParagraphSeparator);
                    break;
                case 7:
                    sb.Append((char)random.Next(0x00, 0x20));
                    break;
                case 8:
                    sb.Append((char)random.Next(0xD800, 0xE000));
                    break;
                case 9:
                    sb.Append(char.ConvertFromUtf32(random.Next(0x10000, 0x110000)));
                    break;
                case 10:
                    sb.Append((char)random.Next(0x80, 0x800));
                    break;
                case 11:
                    sb.Append((char)random.Next(0x7F, 0x100));
                    break;
                default:
                    sb.Append((char)random.Next(0x20, 0x7F));
                    break;
            }
        }

        return sb.ToString();
    }
}
