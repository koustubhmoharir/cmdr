# cfgedit architecture notes

Implementation notes that refine [cfgedit-design.md](cfgedit-design.md). Sections are added as milestones start. The
design's "Engineering rules" apply throughout.

## Source text (M1)

`Cfgedit.Core.Text.SourceText` is immutable. It is the single source of truth: every CST is computed from it, and every
edit produces a new SourceText.

```csharp
public sealed class SourceText
{
    public string Text { get; }            // decoded, without the BOM
    public Encoding Encoding { get; }      // strict (throws on invalid input), emits no preamble itself
    public bool HasBom { get; }
    public string Newline { get; }         // dominant line break in Text: "\r\n", "\n" or "\r"; Environment.NewLine when Text has none
    public bool EndsWithNewline { get; }

    public static SourceText Decode(ReadOnlySpan<byte> bytes, string? path = null);   // JSON family: BOM, else UTF-8
    public static SourceText Create(string text, Encoding encoding, bool hasBom);     // new documents and tests
    public SourceText WithText(string text);                                          // same encoding and BOM; Newline/EndsWithNewline recomputed
    public byte[] Encode();                                                            // BOM bytes (if HasBom) + Encoding.GetBytes(Text)
    public LinePosition GetPosition(int offset);                                       // 1-based line and UTF-16 column
    public int GetOffset(LinePosition position);
}
public readonly record struct LinePosition(int Line, int Column);
```

**Decoding**

- `Decode` recognises the UTF-8 (EF BB BF), UTF-16 LE (FF FE) and UTF-16 BE (FE FF) BOMs. Without a BOM it uses UTF-8.
  It always decodes strictly (`throwOnInvalidBytes: true`).
- Invalid bytes are a `CfgeditException`, e.g. "not valid UTF-8 at byte 1234". The exception carries `Path`, and the
  line and column of the last good character when that can be computed.
- An odd byte count in UTF-16 is invalid input.
- XML decoding (Appendix F, declared encodings) is added in M2 as a separate entry point. `Decode` stays the JSON-family
  one.
- Because decoding is strict, the round trip is exact: `Decode(b).Encode()` equals `b` for every valid input. This is a
  tested invariant.

**Positions**

- Positions match XmlReader's: CRLF, a lone CR and LF are each one line break, and the column counts UTF-16 units.
  U+2028 and U+2029 are not line breaks for positions.
- A position past the end of the text is the position just after the last character.

**Newline counting**

- "Dominant" means the most frequent line break. A tie goes to the one that occurs first.

## JSON family (M1)

Namespace `Cfgedit.Core.Json`.

### Dialects

```csharp
public enum JsonDialect { Json, Jsonc, Json5 }
```

| Feature | Json | Jsonc | Json5 |
|---|---|---|---|
| `//` and `/* */` comments | – | ✓ | ✓ |
| One trailing comma in an object or array | – | ✓ | ✓ |
| Single-quoted strings, unquoted identifier keys | – | – | ✓ |
| JSON5 numbers: hex, `+`, leading/trailing `.`, `Infinity`, `NaN` | – | – | ✓ |
| JSON5 string escapes: `\'`, `\v`, `\0`, `\xHH`, line continuations, any other non-digit char escaping itself; unescaped control characters other than CR and LF | – | – | ✓ |
| JSON5 whitespace: `\v`, `\f`, U+00A0, U+FEFF, U+2028, U+2029, category Zs | – | – | ✓ |

**Strict JSON (RFC 8259)**

- Whitespace is space, tab, LF and CR only.
- Strings use double quotes. The escapes are `\" \\ \/ \b \f \n \r \t \uXXXX`. Unescaped characters below U+0020 are an
  error; everything from U+0020 up, U+2028 and U+2029 included, is allowed unescaped.
- Numbers follow RFC 8259: `-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?`.
- A `\u` escape may produce a lone surrogate. That is accepted and kept in the decoded value.

**Jsonc** uses JSON's grammar plus comments and trailing commas.

**Json5** follows the JSON5 1.0.0 spec (https://spec.json5.org).

- Identifier keys are ES5 IdentifierName: start = Unicode letter (Lu Ll Lt Lm Lo Nl), `$`, `_` or a `\uXXXX` escape of
  one; part = start, Mn, Mc, Nd, Pc, U+200C or U+200D.
- Reserved words are allowed as keys.
- Line continuations are a backslash followed by LF, CR, CRLF, U+2028 or U+2029. They decode to nothing.
- `\0` must not be followed by a digit, and `\1`–`\9` are errors.

### Lexer

```csharp
public enum JsonTokenKind
{
    LeftBrace, RightBrace, LeftBracket, RightBracket, Colon, Comma,
    String, Number, True, False, Null, Identifier,
    Whitespace, LineComment, BlockComment, EndOfFile,
}
public readonly record struct JsonToken(JsonTokenKind Kind, int Start, int Length);
```

- `Identifier` is a JSON5 identifier key.
- `Infinity` and `NaN`, with or without a sign, are `Number`.
- A `LineComment` covers `//` up to, but not including, the line break.

`JsonLexer.Tokenize(SourceText text, JsonDialect dialect) → IReadOnlyList<JsonToken>`

- The token list ends with `EndOfFile` (length 0).
- **Tiling invariant:** the tokens cover the text exactly, with no gaps and no overlaps, so their spans concatenate to
  `Text`. Tests check this.
- Errors are `CfgeditException` with line and column. Examples: unterminated string, invalid escape, control character in
  a string, unterminated block comment, invalid character, comment in strict JSON, single-quoted string outside JSON5.

### CST

The CST holds immutable classes with spans (start offset, end offset exclusive) into the SourceText. It carries no
trivia objects: the whitespace between nodes is whatever the text holds between their spans. Comments are entries, so
edit planning (M3) can attach them and move them with their nodes.

```csharp
public sealed class JsonDocument
{
    public SourceText Source { get; }
    public JsonDialect Dialect { get; }
    public IReadOnlyList<JsonComment> Leading { get; }    // comments before the root value
    public JsonValue Root { get; }
    public IReadOnlyList<JsonComment> Trailing { get; }   // comments after it
    public static JsonDocument Parse(SourceText source, JsonDialect dialect);
}

public abstract class JsonValue { public int Start { get; } public int End { get; } }

public sealed class JsonObject : JsonValue
{
    public IReadOnlyList<JsonEntry> Entries { get; }      // members and comments, in source order
    public int? TrailingCommaStart { get; }               // a comma after the last member (Jsonc/Json5)
}

public sealed class JsonArray : JsonValue
{
    public IReadOnlyList<JsonEntry> Entries { get; }      // items and comments, in source order
    public int? TrailingCommaStart { get; }
}

public abstract class JsonEntry { public int Start { get; } public int End { get; } }

public sealed class JsonComment : JsonEntry
{
    public bool IsBlock { get; }
    public string Content { get; }                        // without the markers
}

public sealed class JsonMember : JsonEntry               // Start = key start, End = value end (the comma is not included)
{
    public JsonKey Key { get; }
    public int ColonStart { get; }
    public JsonValue Value { get; }
    public int? CommaStart { get; }                       // the comma that follows this member, if any
}

public sealed class JsonItem : JsonEntry
{
    public JsonValue Value { get; }
    public int? CommaStart { get; }
}

public sealed class JsonKey
{
    public int Start { get; }
    public int End { get; }
    public string Name { get; }                           // decoded
    public char? Quote { get; }                           // '"' or '\'', null for an identifier key
}

public enum JsonScalarKind { String, Number, True, False, Null }

public sealed class JsonScalar : JsonValue
{
    public JsonScalarKind Kind { get; }
    public string Raw { get; }                            // exact source text
    public string? StringValue { get; }                   // decoded, for String
    public char? Quote { get; }                           // for String
}
```

**Parser behaviour**

- `Parse` is a recursive-descent parser over the token list, skipping Whitespace tokens.
- Nesting deeper than `JsonDocument.MaxDepth` = 256 is a `CfgeditException` at the opening bracket that exceeds it.
- Empty input, or input with only comments, is an error ("no value").
- Content after the root value, other than whitespace and comments, is an error.
- Duplicate keys are accepted by the parser. The editor refuses to create them (M3).
- Error messages name what was expected and what was found, e.g. "expected ',' or '}' but found ':'". The position is
  the start of the offending token.

### Encoders and validators

`JsonSyntax`:

- `string EncodeString(string value, char quote, JsonDialect dialect)` returns the full literal, quotes included. It
  escapes:
  - the quote character in use and `\`
  - `\b \f \n \r \t`
  - other characters below U+0020, and U+007F, as `\u00XX` (lowercase hex)
  - U+2028 and U+2029 as ` ` / ` `
  - lone surrogates as `\uXXXX`

  Everything else is written literally, including non-ASCII characters and the other quote character. A single quote is
  only valid for Json5; otherwise the method throws `ArgumentException`.
- `bool IsIdentifierName(string name)` follows the ES5 IdentifierName rule above, without escapes. It is false for an
  empty string.
- `string EncodeKey(string name, char? originalQuote, char preferredQuote, JsonDialect dialect)` returns the bare name
  when the dialect is Json5, `originalQuote` is null and `IsIdentifierName(name)` holds. Otherwise it returns
  `EncodeString(name, originalQuote ?? preferredQuote, dialect)`.
- `bool IsValidNumber(string text, JsonDialect dialect)` uses the full token grammar.
- `string DecodeString(string literal, JsonDialect dialect)` returns the decoded value of a literal, quotes included. It
  is the same code the lexer and parser use.

**Property tests**

- For any string `s` (random, plus the nasty corpus: quotes of both kinds, backslashes, control characters, U+2028, lone
  surrogates, emoji, ZWJ sequences, NUL) and each quote valid for the dialect:
  `DecodeString(EncodeString(s, q, d), d) == s`.
- The literal must parse as the value of a whole document: `JsonDocument.Parse(SourceText.Create(literal…), d)`.

### Tests and fixtures

- Fixtures go in `tests/Cfgedit.Core.Tests/Fixtures/json/` and are synthetic. They are never copied from other
  repositories, because real configs can hold secrets. They include:
  - an appsettings-style JSONC file with comments and trailing commas
  - a tsconfig-style file
  - the JSON5 spec's example document
  - files with CRLF line endings, with a UTF-8 BOM, in UTF-16 LE/BE, and without a final newline
  - deep nesting at 256 (accepted) and 257 (rejected)
- For each fixture, tests check:
  - the byte round trip through SourceText
  - token tiling
  - the expected tree (keys, kinds, decoded values, comment entries, comma positions)
- Error tests assert the exact message and line:column. That includes a Unicode character before the error (an astral
  character counts as 2 columns).
- Real configs, e.g. from the sibling SheetKraft repository, are checked by a scratchpad probe at review time, never by
  committed tests.
