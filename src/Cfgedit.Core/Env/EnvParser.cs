using System.Text;
using Shared.Core;

namespace Cfgedit.Core.Env;

/// <summary>
/// Builds the <see cref="EnvDocument"/> tree line by line. Each item starts at the first character of a line and ends after
/// its last line break, so the items tile the text. A quoted value may run over several lines, and the entry then ends on the
/// line that closes it.
/// </summary>
/// <remarks>
/// The rules are those of the design's dotenv syntax, with three decisions fixed here: a line break inside quotes decodes to
/// <c>\n</c>; an unquoted value ends at a <c>#</c> that follows a space or tab; and a <c>#</c> straight after the <c>=</c>
/// (with no space before it) is part of an unquoted value.
/// </remarks>
internal sealed class EnvParser
{
    private readonly SourceText _source;
    private readonly string _text;
    private readonly List<EnvItem> _items = [];

    private EnvParser(SourceText source)
    {
        _source = source;
        _text = source.Text;
    }

    /// <exception cref="CfgeditException">The text is not valid .env syntax.</exception>
    internal static EnvDocument Parse(SourceText source)
    {
        return new EnvParser(source).ParseDocument();
    }

    private EnvDocument ParseDocument()
    {
        int pos = 0;
        while (pos < _text.Length)
        {
            pos = ParseLine(pos);
        }

        return new EnvDocument(_source, _items.AsReadOnly());
    }

    /// <summary>Parses the item that starts at a line start, adds it, and returns the offset of the item's end.</summary>
    private int ParseLine(int lineStart)
    {
        int lineEnd = LineContentEnd(lineStart);
        int first = SkipBlanks(lineStart);
        if (first == lineEnd)
        {
            int blankEnd = lineEnd + BreakLength(lineEnd);
            _items.Add(new EnvBlankLine(lineStart, blankEnd));
            return blankEnd;
        }

        if (_text[first] == '#')
        {
            int commentEnd = lineEnd + BreakLength(lineEnd);
            _items.Add(new EnvCommentLine(lineStart, commentEnd, new EnvComment(first, lineEnd, _text[(first + 1)..lineEnd])));
            return commentEnd;
        }

        return ParseEntry(lineStart, first);
    }

    /// <summary>Parses an entry whose first significant character is at <paramref name="first"/>.</summary>
    private int ParseEntry(int lineStart, int first)
    {
        string text = _text;
        int length = text.Length;
        int p = first;

        EnvSpan? export = null;
        if (StartsWithExport(p) && IsBlank(text[p + 6]))
        {
            // The keyword is taken only when a key follows it. "export = 1" is a key named export.
            int afterKeyword = SkipBlanks(p + 6);
            if (afterKeyword < length && EnvKey.IsStartChar(text[afterKeyword]))
            {
                export = new EnvSpan(p, p + 6);
                p = afterKeyword;
            }
        }

        if (!EnvKey.IsStartChar(text[p]))
        {
            throw Error(p, "invalid key");
        }

        int keyStart = p;
        int keyEnd = p;
        while (keyEnd < length && EnvKey.IsPartChar(text[keyEnd]))
        {
            keyEnd++;
        }

        int equalsStart = SkipBlanks(keyEnd);
        if (equalsStart >= length || text[equalsStart] != '=')
        {
            throw Error(equalsStart, "expected '='");
        }

        int valueStart = SkipBlanks(equalsStart + 1);
        char c = valueStart < length ? text[valueStart] : '\0';
        if (c is '\'' or '"')
        {
            return ParseQuotedEntry(lineStart, export, text[keyStart..keyEnd], new EnvSpan(keyStart, keyEnd), equalsStart, valueStart);
        }

        if (c == '`')
        {
            throw Error(valueStart, "backtick quotes are not supported");
        }

        return ParseUnquotedEntry(lineStart, export, text[keyStart..keyEnd], new EnvSpan(keyStart, keyEnd), equalsStart, valueStart);
    }

    private int ParseQuotedEntry(
        int lineStart, EnvSpan? export, string key, EnvSpan keySpan, int equalsStart, int quoteStart)
    {
        string text = _text;
        int length = text.Length;
        char quote = text[quoteStart];
        var value = new StringBuilder();

        int j = quoteStart + 1;
        while (true)
        {
            if (j >= length)
            {
                throw Error(quoteStart, "unterminated quoted value");
            }

            char ch = text[j];
            if (ch == quote)
            {
                break;
            }

            if (ch == '\\' && quote == '"' && j + 1 < length && TryDecodeEscape(text[j + 1], out char decoded))
            {
                value.Append(decoded);
                j += 2;
                continue;
            }

            if (ch is '\r' or '\n')
            {
                value.Append('\n');
                j += BreakLength(j);
                continue;
            }

            value.Append(ch);
            j++;
        }

        int closeEnd = j + 1;
        int afterValue = SkipBlanks(closeEnd);
        int lineEnd = LineContentEnd(afterValue);
        EnvComment? comment = null;
        if (afterValue < lineEnd)
        {
            if (text[afterValue] != '#')
            {
                throw Error(afterValue, "unexpected text after closing quote");
            }

            comment = new EnvComment(afterValue, lineEnd, text[(afterValue + 1)..lineEnd]);
        }

        int entryEnd = lineEnd + BreakLength(lineEnd);
        var valueSpan = new EnvSpan(quoteStart, closeEnd);
        _items.Add(new EnvEntry(
            lineStart,
            entryEnd,
            export,
            key,
            keySpan,
            equalsStart,
            valueSpan,
            value.ToString(),
            quote == '"' ? EnvQuote.Double : EnvQuote.Single,
            comment));
        return entryEnd;
    }

    private int ParseUnquotedEntry(
        int lineStart, EnvSpan? export, string key, EnvSpan keySpan, int equalsStart, int valueStart)
    {
        string text = _text;
        int length = text.Length;

        // The value ends before a comment, which starts at a '#' that follows a space or tab, and before the line break.
        int valueEnd = valueStart;
        int i = valueStart;
        while (i < length && text[i] is not ('\r' or '\n'))
        {
            if (text[i] == '#' && IsBlank(text[i - 1]))
            {
                break;
            }

            if (!IsBlank(text[i]))
            {
                valueEnd = i + 1;
            }

            i++;
        }

        int lineEnd = LineContentEnd(i);
        EnvComment? comment = null;
        if (i < lineEnd)
        {
            comment = new EnvComment(i, lineEnd, text[(i + 1)..lineEnd]);
        }

        int entryEnd = lineEnd + BreakLength(lineEnd);
        _items.Add(new EnvEntry(
            lineStart,
            entryEnd,
            export,
            key,
            keySpan,
            equalsStart,
            new EnvSpan(valueStart, valueEnd),
            text[valueStart..valueEnd],
            EnvQuote.None,
            comment));
        return entryEnd;
    }

    /// <summary>The escapes of a double-quoted value. A backslash before any other character is kept as written.</summary>
    private static bool TryDecodeEscape(char next, out char decoded)
    {
        switch (next)
        {
            case 'n':
                decoded = '\n';
                return true;
            case 'r':
                decoded = '\r';
                return true;
            case 't':
                decoded = '\t';
                return true;
            case '"':
                decoded = '"';
                return true;
            case '\\':
                decoded = '\\';
                return true;
            default:
                decoded = '\0';
                return false;
        }
    }

    /// <summary>True when the text has the word <c>export</c> at the offset. The caller checks the character after it.</summary>
    private bool StartsWithExport(int p)
    {
        return p + 6 < _text.Length && string.CompareOrdinal(_text, p, "export", 0, 6) == 0;
    }

    /// <summary>The offset of the first space or tab at or after the offset, or of the first character that is not one.</summary>
    private int SkipBlanks(int offset)
    {
        int i = offset;
        while (i < _text.Length && IsBlank(_text[i]))
        {
            i++;
        }

        return i;
    }

    /// <summary>The offset of the first CR or LF at or after the offset, or the end of the text.</summary>
    private int LineContentEnd(int offset)
    {
        int i = offset;
        while (i < _text.Length && _text[i] is not ('\r' or '\n'))
        {
            i++;
        }

        return i;
    }

    /// <summary>The length of the line break at the offset: 2 for CRLF, 1 for LF or CR, and 0 when there is none.</summary>
    private int BreakLength(int offset)
    {
        if (offset >= _text.Length)
        {
            return 0;
        }

        if (_text[offset] == '\r')
        {
            return offset + 1 < _text.Length && _text[offset + 1] == '\n' ? 2 : 1;
        }

        return _text[offset] == '\n' ? 1 : 0;
    }

    private static bool IsBlank(char c)
    {
        return c is ' ' or '\t';
    }

    private CfgeditException Error(int offset, string message)
    {
        LinePosition at = _source.GetPosition(offset);
        return new CfgeditException(message)
        {
            Line = at.Line,
            Column = at.Column,
        };
    }
}
