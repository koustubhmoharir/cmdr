using System.Text;
using Cfgedit.Core.Text;

namespace Cfgedit.Core.Json;

/// <summary>
/// Builds the <see cref="JsonDocument"/> tree from the lexer's tokens. It is recursive descent over the tokens, with whitespace
/// skipped and comments collected as entries of the enclosing container. Nesting is capped at
/// <see cref="JsonDocument.MaxDepth"/>, so the recursion is bounded.
/// </summary>
internal sealed class JsonParser
{
    private readonly SourceText _source;
    private readonly string _text;
    private readonly JsonDialect _dialect;
    private readonly IReadOnlyList<JsonToken> _tokens;
    private int _pos;

    private JsonParser(SourceText source, JsonDialect dialect, IReadOnlyList<JsonToken> tokens)
    {
        _source = source;
        _text = source.Text;
        _dialect = dialect;
        _tokens = tokens;
    }

    /// <exception cref="CfgeditException">The text is not a valid document in the dialect.</exception>
    internal static JsonDocument Parse(SourceText source, JsonDialect dialect)
    {
        IReadOnlyList<JsonToken> tokens = JsonLexer.Tokenize(source, dialect);
        return new JsonParser(source, dialect, tokens).ParseDocument();
    }

    private JsonToken Current => _tokens[_pos];

    private JsonDocument ParseDocument()
    {
        var leading = new List<JsonEntry>();
        SkipTrivia(leading);
        if (Current.Kind == JsonTokenKind.EndOfFile)
        {
            throw Expected("a value");
        }

        JsonValue root = ParseValue(0, "a value");

        var trailing = new List<JsonEntry>();
        SkipTrivia(trailing);
        if (Current.Kind != JsonTokenKind.EndOfFile)
        {
            throw Expected("end of file");
        }

        return new JsonDocument(
            _source,
            _dialect,
            leading.Cast<JsonComment>().ToList(),
            root,
            trailing.Cast<JsonComment>().ToList());
    }

    /// <summary>
    /// Moves past whitespace and comments. Each comment is added to <paramref name="entries"/>, so a comment becomes an
    /// entry of the container being parsed, in source order.
    /// </summary>
    private void SkipTrivia(List<JsonEntry> entries)
    {
        while (true)
        {
            JsonToken t = _tokens[_pos];
            switch (t.Kind)
            {
                case JsonTokenKind.Whitespace:
                    _pos++;
                    break;

                case JsonTokenKind.LineComment:
                    entries.Add(new JsonComment(t.Start, t.End, false, _text.Substring(t.Start + 2, t.Length - 2)));
                    _pos++;
                    break;

                case JsonTokenKind.BlockComment:
                    entries.Add(new JsonComment(t.Start, t.End, true, _text.Substring(t.Start + 2, t.Length - 4)));
                    _pos++;
                    break;

                default:
                    return;
            }
        }
    }

    /// <summary>
    /// The offset of the comma that follows the current position, skipping whitespace and comments, or null when the next
    /// significant token is not a comma. It does not move the position.
    /// </summary>
    private int? PeekComma()
    {
        for (int i = _pos; ; i++)
        {
            JsonToken t = _tokens[i];
            if (t.Kind is JsonTokenKind.Whitespace or JsonTokenKind.LineComment or JsonTokenKind.BlockComment)
            {
                continue;
            }

            return t.Kind == JsonTokenKind.Comma ? t.Start : (int?)null;
        }
    }

    private JsonValue ParseValue(int depth, string expected)
    {
        JsonToken t = Current;
        switch (t.Kind)
        {
            case JsonTokenKind.LeftBrace:
                return ParseObject(depth + 1);

            case JsonTokenKind.LeftBracket:
                return ParseArray(depth + 1);

            case JsonTokenKind.String:
                {
                    string raw = Raw(t);
                    _pos++;
                    return new JsonScalar(t.Start, t.End, JsonScalarKind.String, raw, JsonSyntax.DecodeString(raw, _dialect), raw[0]);
                }

            case JsonTokenKind.Number:
                _pos++;
                return new JsonScalar(t.Start, t.End, JsonScalarKind.Number, Raw(t), null, null);

            case JsonTokenKind.True:
                _pos++;
                return new JsonScalar(t.Start, t.End, JsonScalarKind.True, Raw(t), null, null);

            case JsonTokenKind.False:
                _pos++;
                return new JsonScalar(t.Start, t.End, JsonScalarKind.False, Raw(t), null, null);

            case JsonTokenKind.Null:
                _pos++;
                return new JsonScalar(t.Start, t.End, JsonScalarKind.Null, Raw(t), null, null);

            default:
                throw Expected(expected);
        }
    }

    private JsonObject ParseObject(int depth)
    {
        JsonToken open = Current;
        CheckDepth(depth, open);
        _pos++;

        var entries = new List<JsonEntry>();
        int? trailingComma = null;
        SkipTrivia(entries);
        if (Current.Kind != JsonTokenKind.RightBrace)
        {
            bool first = true;
            while (true)
            {
                // A closing brace is expected at the first position, and after a comma in the dialects that allow one.
                bool allowClose = first || _dialect != JsonDialect.Json;
                JsonMember member = ParseMember(entries, depth, allowClose);
                SkipTrivia(entries);
                if (member.CommaStart is not int comma)
                {
                    if (Current.Kind == JsonTokenKind.RightBrace)
                    {
                        break;
                    }

                    throw Expected("',' or '}'");
                }

                _pos++;
                SkipTrivia(entries);
                if (Current.Kind == JsonTokenKind.RightBrace && _dialect != JsonDialect.Json)
                {
                    trailingComma = comma;
                    break;
                }

                first = false;
            }
        }

        JsonToken close = Current;
        _pos++;
        return new JsonObject(open.Start, close.End, entries, trailingComma);
    }

    private JsonMember ParseMember(List<JsonEntry> entries, int depth, bool allowClose)
    {
        SkipTrivia(entries);
        JsonKey key = ParseKey(allowClose ? "a key or '}'" : "a key");
        _pos++;

        SkipTrivia(entries);
        if (Current.Kind != JsonTokenKind.Colon)
        {
            throw Expected("':'");
        }

        int colon = Current.Start;
        _pos++;

        SkipTrivia(entries);
        JsonValue value = ParseValue(depth, "a value");

        // The member is added before the comments that follow it, so that a comment between a value and its comma comes
        // after the member, and a comment before the key comes before it.
        var member = new JsonMember(key.Start, value.End, key, colon, value, PeekComma());
        entries.Add(member);
        return member;
    }

    private JsonKey ParseKey(string expected)
    {
        JsonToken t = Current;
        if (t.Kind == JsonTokenKind.String)
        {
            string raw = Raw(t);
            return new JsonKey(t.Start, t.End, JsonSyntax.DecodeString(raw, _dialect), raw[0]);
        }

        if (_dialect == JsonDialect.Json5)
        {
            // A JSON5 identifier key is an Identifier token, or a word the lexer read as a keyword or as Infinity or NaN.
            // Unsigned Infinity and NaN are valid keys, but a signed one, or any other number, is not.
            if (t.Kind == JsonTokenKind.Identifier)
            {
                var name = new StringBuilder();
                if (!JsonSyntax.TryScanIdentifier(_text, t.Start, name, out int end, out int errorIndex, out string? error))
                {
                    throw Error(errorIndex, error!);
                }

                return new JsonKey(t.Start, end, name.ToString(), null);
            }

            string word = Raw(t);
            bool wordKey = t.Kind is JsonTokenKind.True or JsonTokenKind.False or JsonTokenKind.Null
                || (t.Kind == JsonTokenKind.Number && (word == "Infinity" || word == "NaN"));
            if (wordKey)
            {
                return new JsonKey(t.Start, t.End, word, null);
            }
        }

        throw Expected(expected);
    }

    private JsonArray ParseArray(int depth)
    {
        JsonToken open = Current;
        CheckDepth(depth, open);
        _pos++;

        var entries = new List<JsonEntry>();
        int? trailingComma = null;
        SkipTrivia(entries);
        if (Current.Kind != JsonTokenKind.RightBracket)
        {
            bool first = true;
            while (true)
            {
                bool allowClose = first || _dialect != JsonDialect.Json;
                JsonItem item = ParseItem(entries, depth, allowClose);
                SkipTrivia(entries);
                if (item.CommaStart is not int comma)
                {
                    if (Current.Kind == JsonTokenKind.RightBracket)
                    {
                        break;
                    }

                    throw Expected("',' or ']'");
                }

                _pos++;
                SkipTrivia(entries);
                if (Current.Kind == JsonTokenKind.RightBracket && _dialect != JsonDialect.Json)
                {
                    trailingComma = comma;
                    break;
                }

                first = false;
            }
        }

        JsonToken close = Current;
        _pos++;
        return new JsonArray(open.Start, close.End, entries, trailingComma);
    }

    private JsonItem ParseItem(List<JsonEntry> entries, int depth, bool allowClose)
    {
        SkipTrivia(entries);
        JsonValue value = ParseValue(depth, allowClose ? "a value or ']'" : "a value");
        var item = new JsonItem(value.Start, value.End, value, PeekComma());
        entries.Add(item);
        return item;
    }

    private void CheckDepth(int depth, JsonToken open)
    {
        if (depth > JsonDocument.MaxDepth)
        {
            throw Error(open.Start, $"nesting is deeper than {JsonDocument.MaxDepth} levels");
        }
    }

    private string Raw(JsonToken t)
    {
        return _text.Substring(t.Start, t.Length);
    }

    /// <summary>The error for the current token: what was expected, and what was found.</summary>
    private CfgeditException Expected(string expected)
    {
        return Error(Current.Start, $"expected {expected} but found {Describe(Current.Kind)}");
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

    private string Describe(JsonTokenKind kind)
    {
        return kind switch
        {
            JsonTokenKind.LeftBrace => "'{'",
            JsonTokenKind.RightBrace => "'}'",
            JsonTokenKind.LeftBracket => "'['",
            JsonTokenKind.RightBracket => "']'",
            JsonTokenKind.Colon => "':'",
            JsonTokenKind.Comma => "','",
            JsonTokenKind.String => "string",
            JsonTokenKind.Number => "number",
            JsonTokenKind.True => "'true'",
            JsonTokenKind.False => "'false'",
            JsonTokenKind.Null => "'null'",
            JsonTokenKind.Identifier => "identifier",
            JsonTokenKind.EndOfFile => "end of file",
            _ => throw new InvalidOperationException($"a {kind} token is never reported"),
        };
    }
}
