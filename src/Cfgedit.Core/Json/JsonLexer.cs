using System.Globalization;
using System.Text;
using Cfgedit.Core.Text;

namespace Cfgedit.Core.Json;

/// <summary>
/// Splits JSON-family text into tokens. The tokens tile the text: their spans cover it exactly, with no gaps and no overlaps,
/// so they concatenate to <see cref="SourceText.Text"/>. Every lexical error is a <see cref="CfgeditException"/> with the line
/// and column of the offending character.
/// </summary>
public static class JsonLexer
{
    /// <summary>
    /// Tokenizes the text in the given dialect. The list ends with an <see cref="JsonTokenKind.EndOfFile"/> token of length 0.
    /// </summary>
    /// <exception cref="CfgeditException">The text has a lexical error for the dialect.</exception>
    public static IReadOnlyList<JsonToken> Tokenize(SourceText text, JsonDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(text);
        var scanner = new JsonScanner(text, dialect);
        var tokens = new List<JsonToken>();
        JsonToken token;
        do
        {
            token = scanner.Next();
            tokens.Add(token);
        }
        while (token.Kind != JsonTokenKind.EndOfFile);

        return tokens;
    }
}

/// <summary>
/// Produces the tokens of a text one at a time, for the parser. <see cref="Next"/> returns the token after the previous one,
/// and the end of the text is an <see cref="JsonTokenKind.EndOfFile"/> token of length 0.
/// </summary>
internal sealed class JsonScanner
{
    private readonly SourceText _source;
    private readonly string _s;
    private readonly JsonDialect _dialect;
    private int _pos;

    internal JsonScanner(SourceText source, JsonDialect dialect)
    {
        _source = source;
        _s = source.Text;
        _dialect = dialect;
    }

    /// <summary>Returns the next token. The first lexical error is thrown when its token is reached.</summary>
    /// <exception cref="CfgeditException">The text has a lexical error for the dialect.</exception>
    internal JsonToken Next()
    {
        string s = _s;
        if (_pos >= s.Length)
        {
            return new JsonToken(JsonTokenKind.EndOfFile, s.Length, 0);
        }

        int start = _pos;
        char c = s[start];

        if (IsWhitespace(c, _dialect))
        {
            _pos = SkipWhitespace(s, start, _dialect);
            return new JsonToken(JsonTokenKind.Whitespace, start, _pos - start);
        }

        JsonTokenKind? punctuation = c switch
        {
            '{' => JsonTokenKind.LeftBrace,
            '}' => JsonTokenKind.RightBrace,
            '[' => JsonTokenKind.LeftBracket,
            ']' => JsonTokenKind.RightBracket,
            ':' => JsonTokenKind.Colon,
            ',' => JsonTokenKind.Comma,
            _ => null,
        };
        if (punctuation is { } kind)
        {
            _pos = start + 1;
            return new JsonToken(kind, start, 1);
        }

        if (c == '"' || c == '\'')
        {
            if (c == '\'' && _dialect != JsonDialect.Json5)
            {
                throw Error(start, "single-quoted strings are only allowed in JSON5");
            }

            if (!JsonSyntax.TryScanString(s, start, _dialect, null, out int end, out int errorIndex, out string? error))
            {
                throw Error(errorIndex, error!);
            }

            _pos = end;
            return new JsonToken(JsonTokenKind.String, start, end - start);
        }

        if (c == '/')
        {
            return LexComment(start);
        }

        if (IsDigit(c) || c == '-' || c == '+' || c == '.')
        {
            int end = ScanNumberRun(s, start);
            if (!JsonSyntax.IsValidNumber(s.AsSpan(start, end - start), _dialect))
            {
                throw Error(start, $"invalid number '{s.Substring(start, end - start)}'");
            }

            _pos = end;
            return new JsonToken(JsonTokenKind.Number, start, end - start);
        }

        if (_dialect == JsonDialect.Json5 && (c == '\\' || IsIdentifierStartAt(s, start)))
        {
            if (!JsonSyntax.TryScanIdentifier(s, start, null, out int end, out int errorIndex, out string? error))
            {
                throw Error(errorIndex, error!);
            }

            _pos = end;
            return new JsonToken(Json5WordKind(s.AsSpan(start, end - start)), start, end - start);
        }

        if (_dialect != JsonDialect.Json5 && IsIdentifierStartAt(s, start))
        {
            int end = ScanWordRun(s, start);
            ReadOnlySpan<char> word = s.AsSpan(start, end - start);
            JsonTokenKind wordKind;
            if (word.SequenceEqual("true".AsSpan()))
            {
                wordKind = JsonTokenKind.True;
            }
            else if (word.SequenceEqual("false".AsSpan()))
            {
                wordKind = JsonTokenKind.False;
            }
            else if (word.SequenceEqual("null".AsSpan()))
            {
                wordKind = JsonTokenKind.Null;
            }
            else
            {
                throw Error(start, $"unquoted name '{word.ToString()}' is only allowed in JSON5");
            }

            _pos = end;
            return new JsonToken(wordKind, start, end - start);
        }

        throw Error(start, $"invalid character {DescribeAt(s, start)}");
    }

    /// <summary>
    /// Returns the next token that is not whitespace: a comment or a significant token. The parser has no use for whitespace,
    /// so this skips it without making a token of each run.
    /// </summary>
    /// <exception cref="CfgeditException">The text has a lexical error for the dialect.</exception>
    internal JsonToken NextNonWhitespace()
    {
        while (true)
        {
            JsonToken token = Next();
            if (token.Kind != JsonTokenKind.Whitespace)
            {
                return token;
            }
        }
    }

    /// <summary>Lexes the rest of the text, so that its first lexical error, if there is one, is thrown.</summary>
    /// <exception cref="CfgeditException">The rest of the text has a lexical error for the dialect.</exception>
    internal void ScanToEnd()
    {
        while (Next().Kind != JsonTokenKind.EndOfFile)
        {
        }
    }

    private JsonToken LexComment(int start)
    {
        string s = _s;
        if (_dialect == JsonDialect.Json)
        {
            throw Error(start, "comments are not allowed in JSON");
        }

        char next = start + 1 < s.Length ? s[start + 1] : '\0';
        if (next == '/')
        {
            int end = start + 2;
            while (end < s.Length && !IsLineBreak(s[end], _dialect))
            {
                end++;
            }

            _pos = end;
            return new JsonToken(JsonTokenKind.LineComment, start, end - start);
        }

        if (next == '*')
        {
            int close = s.IndexOf("*/", start + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                throw Error(start, "unterminated block comment");
            }

            int end = close + 2;
            _pos = end;
            return new JsonToken(JsonTokenKind.BlockComment, start, end - start);
        }

        throw Error(start, "invalid character '/'");
    }

    private static JsonTokenKind Json5WordKind(ReadOnlySpan<char> raw)
    {
        if (raw.Contains('\\'))
        {
            return JsonTokenKind.Identifier;
        }

        if (raw.SequenceEqual("true".AsSpan()))
        {
            return JsonTokenKind.True;
        }

        if (raw.SequenceEqual("false".AsSpan()))
        {
            return JsonTokenKind.False;
        }

        if (raw.SequenceEqual("null".AsSpan()))
        {
            return JsonTokenKind.Null;
        }

        if (raw.SequenceEqual("Infinity".AsSpan()) || raw.SequenceEqual("NaN".AsSpan()))
        {
            return JsonTokenKind.Number;
        }

        return JsonTokenKind.Identifier;
    }

    private static bool IsWhitespace(char c, JsonDialect dialect)
    {
        if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
        {
            return true;
        }

        if (dialect != JsonDialect.Json5)
        {
            return false;
        }

        // The ASCII characters that are JSON5 white space are the two checked here. The rest of ASCII is not white space.
        if (c < 0x80)
        {
            return c == '\v' || c == '\f';
        }

        return c == ' '
            || c == '﻿'
            || c == JsonSyntax.LineSeparator
            || c == JsonSyntax.ParagraphSeparator
            || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator;
    }

    private static int SkipWhitespace(string s, int i, JsonDialect dialect)
    {
        while (i < s.Length && IsWhitespace(s[i], dialect))
        {
            i++;
        }

        return i;
    }

    private static bool IsLineBreak(char c, JsonDialect dialect)
    {
        if (c == '\n' || c == '\r')
        {
            return true;
        }

        return dialect == JsonDialect.Json5 && (c == JsonSyntax.LineSeparator || c == JsonSyntax.ParagraphSeparator);
    }

    private static bool IsDigit(char c)
    {
        return c >= '0' && c <= '9';
    }

    private static bool IsIdentifierStartAt(string s, int i)
    {
        char c = s[i];
        if (c < 0x80)
        {
            return JsonSyntax.IsAsciiIdentifierStart(c);
        }

        return JsonSyntax.TryDecodeRune(s, i, out Rune rune, out _) && JsonSyntax.IsIdentifierStart(rune);
    }

    /// <summary>The end of a run of identifier parts, which is how a bare word in strict JSON is delimited.</summary>
    private static int ScanWordRun(string s, int i)
    {
        while (i < s.Length)
        {
            char c = s[i];
            if (c < 0x80)
            {
                if (!JsonSyntax.IsAsciiIdentifierPart(c))
                {
                    break;
                }

                i++;
                continue;
            }

            if (!JsonSyntax.TryDecodeRune(s, i, out Rune rune, out int width) || !JsonSyntax.IsIdentifierPart(rune))
            {
                break;
            }

            i += width;
        }

        return i;
    }

    /// <summary>
    /// The end of a number candidate: identifier parts and the characters <c>. + -</c>. Anything the dialect's number grammar
    /// rejects in it is reported as an invalid number, so that <c>01</c> and <c>1_000</c> are errors and not two tokens.
    /// </summary>
    private static int ScanNumberRun(string s, int i)
    {
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '.' || c == '+' || c == '-')
            {
                i++;
                continue;
            }

            if (c < 0x80)
            {
                if (!JsonSyntax.IsAsciiIdentifierPart(c))
                {
                    break;
                }

                i++;
                continue;
            }

            if (!JsonSyntax.TryDecodeRune(s, i, out Rune rune, out int width) || !JsonSyntax.IsIdentifierPart(rune))
            {
                break;
            }

            i += width;
        }

        return i;
    }

    private static string DescribeAt(string s, int i)
    {
        if (JsonSyntax.TryDecodeRune(s, i, out Rune rune, out _))
        {
            return JsonSyntax.Quoted(rune);
        }

        return $"U+{(int)s[i]:X4}";
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
