using System.Text;
using Cmdr.Core.Processes;

namespace Cmdr.Core.Tests.Processes;

/// <summary>Pure tests of the CreateProcessW command line builder (runs on every OS).</summary>
public class WindowsCommandLineTests
{
    [Fact]
    public void BuildJoinsPlainExeAndArguments()
    {
        string line = WindowsCommandLine.Build(@"C:\tools\git.exe", null, new[] { "status", "-s" });

        Assert.Equal(@"C:\tools\git.exe status -s", line);
    }

    [Fact]
    public void BuildWithNoArgumentsIsJustTheFileName()
    {
        Assert.Equal(@"C:\tools\git.exe", WindowsCommandLine.Build(@"C:\tools\git.exe", null, Array.Empty<string>()));
    }

    [Fact]
    public void BuildQuotesFileNameWithSpaces()
    {
        string line = WindowsCommandLine.Build(@"C:\Program Files\app.exe", null, new[] { "x" });

        Assert.Equal(@"""C:\Program Files\app.exe"" x", line);
    }

    [Fact]
    public void BuildQuotesFileNameWithTab()
    {
        Assert.Equal("\"C:\\a\tb\\app.exe\"", WindowsCommandLine.Build("C:\\a\tb\\app.exe", null, Array.Empty<string>()));
    }

    [Fact]
    public void BuildQuotesEmptyFileName()
    {
        Assert.Equal("\"\" x", WindowsCommandLine.Build("", null, new[] { "x" }));
    }

    [Fact]
    public void BuildQuotesArgumentsThatNeedIt()
    {
        string line = WindowsCommandLine.Build("app.exe", null, new[] { "a b", "c\"d" });

        Assert.Equal(@"app.exe ""a b"" ""c\""d""", line);
    }

    [Fact]
    public void BuildAppendsRawArgumentsVerbatim()
    {
        const string raw = @"/d /s /c ""^""C:\x.cmd^"" ^""a^ b^""""";

        string line = WindowsCommandLine.Build("cmd.exe", raw, Array.Empty<string>());

        Assert.Equal("cmd.exe " + raw, line);
    }

    [Fact]
    public void BuildAppendsNothingForEmptyRawArguments()
    {
        Assert.Equal("cmd.exe", WindowsCommandLine.Build("cmd.exe", "", Array.Empty<string>()));
    }

    [Fact]
    public void BuildWithRawArgumentsAndListThrows()
    {
        Assert.Throws<ArgumentException>(() => WindowsCommandLine.Build("cmd.exe", "/c x", new[] { "y" }));
    }

    [Fact]
    public void BuildWithQuoteInFileNameThrows()
    {
        Assert.Throws<ArgumentException>(() => WindowsCommandLine.Build("a\"b.exe", null, Array.Empty<string>()));
    }

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("", "\"\"")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("a\\b", "a\\b")]
    [InlineData("a\\\\b c", "\"a\\\\b c\"")]
    [InlineData("a b\\", "\"a b\\\\\"")]
    [InlineData("a\\\"b", "\"a\\\\\\\"b\"")]
    [InlineData("\\\\server\\share dir\\", "\"\\\\server\\share dir\\\\\"")]
    [InlineData("a\tb", "\"a\tb\"")]
    public void QuoteArgumentMatchesDotNetRules(string argument, string expected)
    {
        Assert.Equal(expected, WindowsCommandLine.QuoteArgument(argument));
    }

    [Fact]
    public void QuoteArgumentRoundTripsThroughArgvParser()
    {
        const string alphabet = "a b\\\"\t";
        var random = new Random(20260314);

        for (int i = 0; i < 400; i++)
        {
            int length = random.Next(0, 13);
            var sb = new StringBuilder(length);
            for (int j = 0; j < length; j++)
            {
                sb.Append(alphabet[random.Next(alphabet.Length)]);
            }
            string input = sb.ToString();

            List<string> parsed = ParseCommandLine(WindowsCommandLine.QuoteArgument(input));

            Assert.True(
                parsed.Count == 1 && parsed[0] == input,
                $"Input \"{Escape(input)}\" quoted as \"{Escape(WindowsCommandLine.QuoteArgument(input))}\" parsed back as " +
                $"[{string.Join(", ", parsed.Select(p => "\"" + Escape(p) + "\""))}].");
        }
    }

    /// <summary>
    /// Reference parser of the CommandLineToArgvW rules for a command line made of whitespace-separated arguments.
    /// Backslashes: 2n followed by a quote gives n backslashes and toggles quote mode; 2n+1 followed by a quote gives
    /// n backslashes and a literal quote; other backslashes are literal. Inputs never contain an unescaped quote pair,
    /// so the special "" inside quotes case is not needed.
    /// </summary>
    private static List<string> ParseCommandLine(string commandLine)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        bool inToken = false;
        bool inQuotes = false;

        int i = 0;
        while (i < commandLine.Length)
        {
            char c = commandLine[i];

            if (c == '\\')
            {
                int count = 0;
                while (i < commandLine.Length && commandLine[i] == '\\')
                {
                    count++;
                    i++;
                }

                inToken = true;
                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    current.Append('\\', count / 2);
                    if (count % 2 == 1)
                    {
                        current.Append('"');
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                    i++;
                }
                else
                {
                    current.Append('\\', count);
                }
                continue;
            }

            if (c == '"')
            {
                inToken = true;
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && (c == ' ' || c == '\t'))
            {
                if (inToken)
                {
                    args.Add(current.ToString());
                    current.Clear();
                    inToken = false;
                }
            }
            else
            {
                inToken = true;
                current.Append(c);
            }
            i++;
        }

        if (inToken)
        {
            args.Add(current.ToString());
        }

        return args;
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
