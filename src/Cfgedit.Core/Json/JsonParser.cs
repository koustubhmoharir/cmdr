using System.Text;
using Shared.Core;

namespace Cfgedit.Core.Json;

/// <summary>
/// Builds the <see cref="JsonDocument"/> tree from the lexer's tokens. It is recursive descent over the tokens, with whitespace
/// skipped and comments collected as entries of the enclosing container. Nesting is capped at
/// <see cref="JsonDocument.MaxDepth"/>, so the recursion is bounded.
/// </summary>
/// <remarks>
/// The tokens are read one at a time, not stored. The text is still lexed in full before a syntax error is reported: every
/// syntax error is raised through <see cref="Error"/>, which lexes the rest of the text first, so a lexical error anywhere in
/// the text takes precedence over it, as it would if the whole text were tokenized up front.
/// </remarks>
internal sealed class JsonParser
{
    private const int MaxSharedNameLength = 64;
    private const int MaxSharedNames = 1024;

    private readonly SourceText _source;
    private readonly string _text;
    private readonly JsonDialect _dialect;
    private readonly JsonScanner _scanner;
    private readonly StringBuilder _scratch = new();

    /// <summary>
    /// The names of keys seen so far in this parse, so that a key that repeats shares one string. Only short names are kept,
    /// and only up to a bound, so that a document of distinct names does not grow the table without limit.
    /// </summary>
    private readonly Dictionary<string, string> _names = new();
    private readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _nameLookup;

    /// <summary>
    /// The comments between a value and the token after it. They become entries after the member or item, so they are held
    /// here until that is made, which needs to know whether a comma follows.
    /// </summary>
    private readonly List<JsonEntry> _trailing = new();

    /// <summary>The current token. It is not consumed until <see cref="Advance"/> moves past it.</summary>
    private JsonToken _current;

    private JsonParser(SourceText source, JsonDialect dialect)
    {
        _source = source;
        _text = source.Text;
        _dialect = dialect;
        _scanner = new JsonScanner(source, dialect);
        _nameLookup = _names.GetAlternateLookup<ReadOnlySpan<char>>();
        _current = _scanner.NextNonWhitespace();
    }

    /// <exception cref="CfgeditException">The text is not a valid document in the dialect.</exception>
    internal static JsonDocument Parse(SourceText source, JsonDialect dialect)
    {
        return new JsonParser(source, dialect).ParseDocument();
    }

    private void Advance()
    {
        _current = _scanner.NextNonWhitespace();
    }

    private JsonDocument ParseDocument()
    {
        var leading = new List<JsonEntry>();
        SkipTrivia(leading);
        if (_current.Kind == JsonTokenKind.EndOfFile)
        {
            throw Expected("a value");
        }

        JsonValue root = ParseValue(0, "a value");

        var trailing = new List<JsonEntry>();
        SkipTrivia(trailing);
        if (_current.Kind != JsonTokenKind.EndOfFile)
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
            JsonToken t = _current;
            switch (t.Kind)
            {
                case JsonTokenKind.LineComment:
                    entries.Add(new JsonComment(t.Start, t.End, false, _text.Substring(t.Start + 2, t.Length - 2)));
                    Advance();
                    break;

                case JsonTokenKind.BlockComment:
                    entries.Add(new JsonComment(t.Start, t.End, true, _text.Substring(t.Start + 2, t.Length - 4)));
                    Advance();
                    break;

                default:
                    return;
            }
        }
    }

    /// <summary>
    /// Reads the trivia after a value into <see cref="_trailing"/>, and returns the offset of the comma that follows it, or
    /// null when the next significant token is not a comma.
    /// </summary>
    private int? ReadTrailing()
    {
        SkipTrivia(_trailing);
        return _current.Kind == JsonTokenKind.Comma ? _current.Start : null;
    }

    /// <summary>Adds the comments read by <see cref="ReadTrailing"/> to the entries, after the member or item just added.</summary>
    private void FlushTrailing(List<JsonEntry> entries)
    {
        entries.AddRange(_trailing);
        _trailing.Clear();
    }

    private JsonValue ParseValue(int depth, string expected)
    {
        JsonToken t = _current;
        switch (t.Kind)
        {
            case JsonTokenKind.LeftBrace:
                return ParseObject(depth + 1);

            case JsonTokenKind.LeftBracket:
                return ParseArray(depth + 1);

            case JsonTokenKind.String:
                {
                    string raw = Raw(t);
                    string value = JsonSyntax.DecodeValidated(_text, t.Start, t.Length, _dialect, _scratch);
                    Advance();
                    return new JsonScalar(t.Start, t.End, JsonScalarKind.String, raw, value, _text[t.Start]);
                }

            case JsonTokenKind.Number:
                Advance();
                return new JsonScalar(t.Start, t.End, JsonScalarKind.Number, Raw(t), null, null);

            case JsonTokenKind.True:
                Advance();
                return new JsonScalar(t.Start, t.End, JsonScalarKind.True, "true", null, null);

            case JsonTokenKind.False:
                Advance();
                return new JsonScalar(t.Start, t.End, JsonScalarKind.False, "false", null, null);

            case JsonTokenKind.Null:
                Advance();
                return new JsonScalar(t.Start, t.End, JsonScalarKind.Null, "null", null, null);

            default:
                throw Expected(expected);
        }
    }

    private JsonObject ParseObject(int depth)
    {
        JsonToken open = _current;
        CheckDepth(depth, open);
        Advance();

        var entries = new List<JsonEntry>();
        int? trailingComma = null;
        SkipTrivia(entries);
        if (_current.Kind != JsonTokenKind.RightBrace)
        {
            bool first = true;
            while (true)
            {
                // A closing brace is expected at the first position, and after a comma in the dialects that allow one.
                bool allowClose = first || _dialect != JsonDialect.Json;
                JsonMember member = ParseMember(entries, depth, allowClose);
                if (member.CommaStart is not int comma)
                {
                    if (_current.Kind == JsonTokenKind.RightBrace)
                    {
                        break;
                    }

                    throw Expected("',' or '}'");
                }

                Advance();
                SkipTrivia(entries);
                if (_current.Kind == JsonTokenKind.RightBrace && _dialect != JsonDialect.Json)
                {
                    trailingComma = comma;
                    break;
                }

                first = false;
            }
        }

        JsonToken close = _current;
        Advance();
        return new JsonObject(open.Start, close.End, entries, trailingComma);
    }

    private JsonMember ParseMember(List<JsonEntry> entries, int depth, bool allowClose)
    {
        SkipTrivia(entries);
        JsonKey key = ParseKey(allowClose ? "a key or '}'" : "a key");
        Advance();

        SkipTrivia(entries);
        if (_current.Kind != JsonTokenKind.Colon)
        {
            throw Expected("':'");
        }

        int colon = _current.Start;
        Advance();

        SkipTrivia(entries);
        JsonValue value = ParseValue(depth, "a value");

        // The member is added before the comments that follow it, so that a comment between a value and its comma comes
        // after the member, and a comment before the key comes before it.
        int? comma = ReadTrailing();
        var member = new JsonMember(key.Start, value.End, key, colon, value, comma);
        entries.Add(member);
        FlushTrailing(entries);
        return member;
    }

    private JsonKey ParseKey(string expected)
    {
        JsonToken t = _current;
        if (t.Kind == JsonTokenKind.String)
        {
            ReadOnlySpan<char> content = _text.AsSpan(t.Start + 1, t.Length - 2);
            string name = content.Length <= MaxSharedNameLength && content.IndexOf('\\') < 0
                ? Share(content)
                : JsonSyntax.DecodeValidated(_text, t.Start, t.Length, _dialect, _scratch);
            return new JsonKey(t.Start, t.End, name, _text[t.Start]);
        }

        if (_dialect == JsonDialect.Json5)
        {
            // A JSON5 identifier key is an Identifier token, or a word the lexer read as a keyword or as Infinity or NaN.
            // Unsigned Infinity and NaN are valid keys, but a signed one, or any other number, is not.
            if (t.Kind == JsonTokenKind.Identifier)
            {
                if (!JsonSyntax.TryScanIdentifier(_text, t.Start, null, out int end, out int errorIndex, out string? error))
                {
                    throw Error(errorIndex, error!);
                }

                ReadOnlySpan<char> name = _text.AsSpan(t.Start, end - t.Start);
                if (name.Length <= MaxSharedNameLength && name.IndexOf('\\') < 0)
                {
                    return new JsonKey(t.Start, end, Share(name), null);
                }

                _scratch.Clear();
                JsonSyntax.TryScanIdentifier(_text, t.Start, _scratch, out _, out _, out _);
                return new JsonKey(t.Start, end, _scratch.ToString(), null);
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

    /// <summary>The string for a name: the one already held for it, or a new one that the table keeps while it has room.</summary>
    private string Share(ReadOnlySpan<char> name)
    {
        if (_nameLookup.TryGetValue(name, out string? shared))
        {
            return shared;
        }

        string created = name.ToString();
        if (_names.Count < MaxSharedNames)
        {
            _names.Add(created, created);
        }

        return created;
    }

    private JsonArray ParseArray(int depth)
    {
        JsonToken open = _current;
        CheckDepth(depth, open);
        Advance();

        var entries = new List<JsonEntry>();
        int? trailingComma = null;
        SkipTrivia(entries);
        if (_current.Kind != JsonTokenKind.RightBracket)
        {
            bool first = true;
            while (true)
            {
                bool allowClose = first || _dialect != JsonDialect.Json;
                JsonItem item = ParseItem(entries, depth, allowClose);
                if (item.CommaStart is not int comma)
                {
                    if (_current.Kind == JsonTokenKind.RightBracket)
                    {
                        break;
                    }

                    throw Expected("',' or ']'");
                }

                Advance();
                SkipTrivia(entries);
                if (_current.Kind == JsonTokenKind.RightBracket && _dialect != JsonDialect.Json)
                {
                    trailingComma = comma;
                    break;
                }

                first = false;
            }
        }

        JsonToken close = _current;
        Advance();
        return new JsonArray(open.Start, close.End, entries, trailingComma);
    }

    private JsonItem ParseItem(List<JsonEntry> entries, int depth, bool allowClose)
    {
        SkipTrivia(entries);
        JsonValue value = ParseValue(depth, allowClose ? "a value or ']'" : "a value");
        int? comma = ReadTrailing();
        var item = new JsonItem(value.Start, value.End, value, comma);
        entries.Add(item);
        FlushTrailing(entries);
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
        return Error(_current.Start, $"expected {expected} but found {Describe(_current.Kind)}");
    }

    /// <summary>
    /// A syntax error at an offset. The rest of the text is lexed first: a lexical error there is thrown instead, since the
    /// text is lexed in full before it is parsed.
    /// </summary>
    private CfgeditException Error(int offset, string message)
    {
        _scanner.ScanToEnd();
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
