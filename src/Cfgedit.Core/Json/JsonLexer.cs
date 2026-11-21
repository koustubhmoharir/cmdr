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
        string s = text.Text;
        var tokens = new List<JsonToken>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            int start = i;

            if (IsWhitespace(c, dialect))
            {
                i = SkipWhitespace(s, i, dialect);
                tokens.Add(new JsonToken(JsonTokenKind.Whitespace, start, i - start));
                continue;
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
                tokens.Add(new JsonToken(kind, start, 1));
                i++;
                continue;
            }

            if (c == '"' || c == '\'')
            {
                if (c == '\'' && dialect != JsonDialect.Json5)
                {
                    throw Error(text, i, "single-quoted strings are only allowed in JSON5");
                }

                if (!JsonSyntax.TryScanString(s, i, dialect, null, out int end, out int errorIndex, out string? error))
                {
                    throw Error(text, errorIndex, error!);
                }

                tokens.Add(new JsonToken(JsonTokenKind.String, start, end - start));
                i = end;
                continue;
            }

            if (c == '/')
            {
                i = LexComment(text, s, i, dialect, tokens);
                continue;
            }

            if (IsDigit(c) || c == '-' || c == '+' || c == '.')
            {
                int end = ScanNumberRun(s, i);
                string raw = s[i..end];
                if (!JsonSyntax.IsValidNumber(raw, dialect))
                {
                    throw Error(text, i, $"invalid number '{raw}'");
                }

                tokens.Add(new JsonToken(JsonTokenKind.Number, start, end - start));
                i = end;
                continue;
            }

            if (dialect == JsonDialect.Json5 && (c == '\\' || IsIdentifierStartAt(s, i)))
            {
                if (!JsonSyntax.TryScanIdentifier(s, i, null, out int end, out int errorIndex, out string? error))
                {
                    throw Error(text, errorIndex, error!);
                }

                string raw = s[i..end];
                tokens.Add(new JsonToken(Json5WordKind(raw), start, end - start));
                i = end;
                continue;
            }

            if (dialect != JsonDialect.Json5 && IsIdentifierStartAt(s, i))
            {
                int end = ScanWordRun(s, i);
                string word = s[i..end];
                JsonTokenKind wordKind = word switch
                {
                    "true" => JsonTokenKind.True,
                    "false" => JsonTokenKind.False,
                    "null" => JsonTokenKind.Null,
                    _ => throw Error(text, i, $"unquoted name '{word}' is only allowed in JSON5"),
                };
                tokens.Add(new JsonToken(wordKind, start, end - start));
                i = end;
                continue;
            }

            throw Error(text, i, $"invalid character {DescribeAt(s, i)}");
        }

        tokens.Add(new JsonToken(JsonTokenKind.EndOfFile, s.Length, 0));
        return tokens;
    }

    private static int LexComment(SourceText text, string s, int i, JsonDialect dialect, List<JsonToken> tokens)
    {
        if (dialect == JsonDialect.Json)
        {
            throw Error(text, i, "comments are not allowed in JSON");
        }

        char next = i + 1 < s.Length ? s[i + 1] : '\0';
        if (next == '/')
        {
            int end = i + 2;
            while (end < s.Length && !IsLineBreak(s[end], dialect))
            {
                end++;
            }

            tokens.Add(new JsonToken(JsonTokenKind.LineComment, i, end - i));
            return end;
        }

        if (next == '*')
        {
            int close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                throw Error(text, i, "unterminated block comment");
            }

            int end = close + 2;
            tokens.Add(new JsonToken(JsonTokenKind.BlockComment, i, end - i));
            return end;
        }

        throw Error(text, i, "invalid character '/'");
    }

    private static JsonTokenKind Json5WordKind(string raw)
    {
        if (raw.Contains('\\'))
        {
            return JsonTokenKind.Identifier;
        }

        return raw switch
        {
            "true" => JsonTokenKind.True,
            "false" => JsonTokenKind.False,
            "null" => JsonTokenKind.Null,
            "Infinity" or "NaN" => JsonTokenKind.Number,
            _ => JsonTokenKind.Identifier,
        };
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

        return c == '\v'
            || c == '\f'
            || c == '\u00A0'
            || c == '\uFEFF'
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
        return JsonSyntax.TryDecodeRune(s, i, out Rune rune, out _) && JsonSyntax.IsIdentifierStart(rune);
    }

    /// <summary>The end of a run of identifier parts, which is how a bare word in strict JSON is delimited.</summary>
    private static int ScanWordRun(string s, int i)
    {
        while (i < s.Length && JsonSyntax.TryDecodeRune(s, i, out Rune rune, out int width) && JsonSyntax.IsIdentifierPart(rune))
        {
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

    private static CfgeditException Error(SourceText text, int offset, string message)
    {
        LinePosition at = text.GetPosition(offset);
        return new CfgeditException(message)
        {
            Line = at.Line,
            Column = at.Column,
        };
    }
}
