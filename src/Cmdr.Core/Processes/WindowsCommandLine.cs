using System.Text;

namespace Cmdr.Core.Processes;

/// <summary>
/// Builds the command line string for <c>CreateProcessW</c>, following the rules .NET's <c>Process</c> uses
/// (<c>PasteArguments</c> in dotnet/runtime) so that the child sees the same argv.
/// </summary>
/// <remarks>
/// Pure and platform-independent: the same output is produced on every host, so it is testable on Linux.
/// The argument parsing side is the MSVCRT / <c>CommandLineToArgvW</c> one.
/// </remarks>
internal static class WindowsCommandLine
{
    /// <summary>
    /// Builds the full command line: the quoted file name, then either the raw argument string (appended verbatim
    /// after a single space, nothing when empty) or each argument of <paramref name="argumentList"/> quoted with
    /// <see cref="QuoteArgument"/> and separated by single spaces.
    /// </summary>
    /// <param name="fileName">Program to run. Must not contain a double quote.</param>
    /// <param name="rawArguments">Already-escaped argument string (for cmd.exe batch launches), or null.</param>
    /// <param name="argumentList">Arguments to escape and join. Must be empty when <paramref name="rawArguments"/> is non-null.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="fileName"/> contains a double quote, or both <paramref name="rawArguments"/> and a non-empty
    /// <paramref name="argumentList"/> are given.
    /// </exception>
    public static string Build(string fileName, string? rawArguments, IReadOnlyList<string> argumentList)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(argumentList);

        if (rawArguments is not null && argumentList.Count != 0)
        {
            throw new ArgumentException("Raw arguments and an argument list cannot be used together.", nameof(rawArguments));
        }

        var sb = new StringBuilder();
        sb.Append(QuoteFileName(fileName));

        if (rawArguments is not null)
        {
            if (rawArguments.Length != 0)
            {
                sb.Append(' ').Append(rawArguments);
            }
            return sb.ToString();
        }

        foreach (string argument in argumentList)
        {
            sb.Append(' ').Append(QuoteArgument(argument));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Quotes one argument for the MSVCRT / <c>CommandLineToArgvW</c> parser. An empty argument becomes <c>""</c>;
    /// an argument with no whitespace and no double quote is returned bare. Otherwise it is wrapped in quotes,
    /// backslashes are doubled only when they precede a double quote or the closing quote, and each embedded double
    /// quote becomes <c>\"</c>.
    /// </summary>
    public static string QuoteArgument(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);

        if (argument.Length != 0 && !NeedsQuotes(argument))
        {
            return argument;
        }

        var sb = new StringBuilder(argument.Length + 2);
        sb.Append('"');

        int backslashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                // 2n backslashes + quote -> n backslashes, then the quote is literal: 2n+1 backslashes plus the quote.
                sb.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                // Backslashes not followed by a quote are literal.
                sb.Append('\\', backslashes).Append(c);
            }
            backslashes = 0;
        }

        // Trailing backslashes would escape the closing quote, so they are doubled.
        sb.Append('\\', backslashes * 2);
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>Quotes the program name when it has a space or tab, or is empty. A double quote is invalid on Windows.</summary>
    private static string QuoteFileName(string fileName)
    {
        if (fileName.Contains('"'))
        {
            throw new ArgumentException("A file name cannot contain a double quote.", nameof(fileName));
        }

        if (fileName.Length == 0 || fileName.Contains(' ') || fileName.Contains('\t'))
        {
            return "\"" + fileName + "\"";
        }

        return fileName;
    }

    /// <summary>Whether an argument needs quotes: it contains whitespace (per <see cref="char.IsWhiteSpace(char)"/>) or a double quote.</summary>
    private static bool NeedsQuotes(string argument)
    {
        foreach (char c in argument)
        {
            if (c == '"' || char.IsWhiteSpace(c))
            {
                return true;
            }
        }
        return false;
    }
}
