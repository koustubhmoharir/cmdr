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

## XML (M2)

Namespace `Cfgedit.Core.Xml`. The probe results behind this section are summarised in the design's "Parsing" subsection
under XML.

### Decoding

`XmlDecoding.Decode(ReadOnlySpan<byte> bytes, string? path) → SourceText` follows XML 1.0 Appendix F.

**Step 1: choose the encoding**

1. **Byte order mark.**
   - EF BB BF → UTF-8.
   - FF FE → UTF-16 LE. If the next two bytes are 00 00, it is UTF-32 LE, which is unsupported.
   - FE FF → UTF-16 BE.
   - 00 00 FE FF → UTF-32 BE, which is unsupported.
2. **No BOM.**
   - `3C 00 3F 00` → UTF-16 LE, and `00 3C 00 3F` → UTF-16 BE.
   - `3C 3F 78 6D` (`<?xm`) → read the declaration as ASCII up to `?>` and take its `encoding` pseudo-attribute.
   - Anything else is UTF-8.
3. **The declared encoding** applies only when there is no BOM and no UTF-16 pattern. It is looked up with
   `Encoding.GetEncoding(name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)`.
   `CodePagesEncodingProvider` is registered once, in a static constructor of `XmlDecoding`.
   - `utf-8` maps to the strict UTF-8 encoding.
   - A name .NET does not know is an error: "unsupported encoding 'x'".

**Step 2: check that the encoding is consistent**

These cases are errors, each naming what was found:

- a BOM or UTF-16 pattern that contradicts a declared encoding (e.g. a UTF-8 BOM with `encoding="windows-1252"`, or
  ASCII-compatible bytes with `encoding="utf-16"`)
- a declared UTF-16 or UTF-32 without a matching BOM or pattern

**Step 3: decode and verify**

- Decoding is strict.
- Afterwards `Encode()` must reproduce the input bytes exactly. Otherwise the error is "the file's bytes do not
  round-trip through encoding X; cfgedit cannot edit it safely". This guards against non-bijective multi-byte code
  pages.
- The BOM is stripped. SourceText gets an internal factory that accepts any strict `Encoding` (the public `Create` keeps
  its UTF-only check).
- `Encode()` of text holding a character that the code page cannot represent is a `CfgeditException` naming the
  character and its position, as the lone-surrogate case already is.

### Parsing pipeline

`XmlDocumentSyntax.Parse(SourceText source) → XmlDocumentSyntax`. The name avoids `System.Xml.XmlDocument`.

1. **Validate.** `XmlReader.Create(new StringReader(source.Text), settings)` with:
   - `DtdProcessing.Parse`, `XmlResolver = null`, `MaxCharactersFromEntities = 1_000_000`
   - `IgnoreWhitespace/IgnoreComments/IgnoreProcessingInstructions = false`, `CheckCharacters = true`

   The reader is walked to the end, recording a flat list of reader nodes. Each records the kind, name, value, depth,
   IsEmptyElement, line and column, and for elements their attributes (name, value, quote, line, column, IsDefault).

   An `XmlException` becomes `CfgeditException(message, Line = ex.LineNumber, Column = ex.LinePosition)`. The message is
   `ex.Message` with .NET's trailing " Line n, position m." removed.

   As a cross-check of the decoding, when the input came from bytes, a stream `XmlTextReader` over those bytes reads one
   node. Its `Encoding.WebName` must equal the decoded encoding's WebName. `XmlDecoding` returns that check as part of a
   `DecodedXml` result, or as a separate method; choose the simplest that keeps Parse free of bytes.
2. **Scan.** A forward scanner builds the CST from `source.Text`. It may assume the text is well-formed, but it never
   loops forever or throws an unhandled exception on unexpected input. Anything unexpected is a scan mismatch
   (step 4).
3. **Cross-check.** The reader nodes and the CST are compared in document order:
   - every Element, EndElement (for non-empty elements), Comment, CDATA, PI, XmlDeclaration and DocumentType: kind, name
     and reported position
   - every attribute: name, position, quote, and the decoded value
   - every text run: start position and decoded value

   The reported positions per kind:
   - Element and EndElement: the name.
   - Attribute: the name.
   - Comment: content start (Start + 4). CDATA: content start (Start + 9).
   - PI: target (Start + 2). XmlDeclaration: `xml` (Start + 2).
   - DocumentType: its name.
   - Text: its first character.

   The reader's Text, Whitespace and SignificantWhitespace nodes all match a CST text run.

   Exceptions:
   - Attributes with `IsDefault` are skipped. They are DTD defaults that are not in the source.
   - Values that contain a custom entity reference are compared by position only.
4. **Mismatch.**
   - A reader node positioned inside the DOCTYPE's span, after the DOCTYPE node itself, means an entity with markup was
     expanded. The error is "entities that contain markup are not supported", at the first reference.
   - Any other mismatch is `CfgeditException("internal parse mismatch: <what> (please report this)")` at the CST
     position.

   Both errors make the file open in text mode (M5b).

### CST

Immutable classes with spans into SourceText.Text (start inclusive, end exclusive):

```csharp
public sealed class XmlDocumentSyntax
{
    public SourceText Source { get; }
    public XmlDeclarationSyntax? Declaration { get; }
    public IReadOnlyList<XmlNodeSyntax> Prolog { get; }   // comments, PIs, the doctype, whitespace text, in order (not the declaration)
    public XmlElementSyntax Root { get; }
    public IReadOnlyList<XmlNodeSyntax> Epilog { get; }   // comments, PIs, whitespace text after the root
    public static XmlDocumentSyntax Parse(SourceText source);
}

public abstract class XmlNodeSyntax { public int Start { get; } public int End { get; } }

public sealed class XmlDeclarationSyntax : XmlNodeSyntax
{
    public string Version { get; }
    public string? Encoding { get; }
    public string? Standalone { get; }
}

public sealed class XmlDoctypeSyntax : XmlNodeSyntax
{
    public string Name { get; }
    public string? InternalSubset { get; }   // raw text between [ and ]
}

public sealed class XmlElementSyntax : XmlNodeSyntax
{
    public int NameStart { get; }
    public string Name { get; }
    public IReadOnlyList<XmlAttributeSyntax> Attributes { get; }
    public int StartTagEnd { get; }                  // offset after '>' or '/>'
    public bool IsEmpty { get; }                     // written as <a/>
    public IReadOnlyList<XmlNodeSyntax> Children { get; }
    public XmlEndTagSyntax? EndTag { get; }          // null when IsEmpty
}

public sealed class XmlEndTagSyntax : XmlNodeSyntax  // "</name  >"
{
    public int NameStart { get; }
    public string Name { get; }
}

public sealed class XmlAttributeSyntax : XmlNodeSyntax   // Start = name start, End = after the closing quote
{
    public string Name { get; }
    public int ValueStart { get; }                   // the opening quote
    public char Quote { get; }
    public string RawValue { get; }                  // between the quotes, exactly as written
    public string Value { get; }                     // decoded and normalised as XmlReader does; equal to RawValue for a raw-mode value
    public bool IsRaw { get; }                       // holds a reference to an entity other than the five predefined ones
}

public sealed class XmlTextSyntax : XmlNodeSyntax    // a maximal run of character data and references between markup
{
    public string Raw { get; }
    public string Value { get; }                     // decoded and normalised (CRLF and CR become LF; references expanded)
    public bool IsWhitespace { get; }                // only XML whitespace (space, tab, CR, LF), and no references
    public bool IsRaw { get; }
}

public sealed class XmlCDataSyntax : XmlNodeSyntax { public string Content { get; } }      // Content normalised like text
public sealed class XmlCommentSyntax : XmlNodeSyntax { public string Content { get; } }
public sealed class XmlProcessingInstructionSyntax : XmlNodeSyntax { public string Target { get; } public string Data { get; } }
```

**Scanner rules**

- **Tag interior.** A name runs up to whitespace, `=`, `/`, `>` or a quote. Between names, `=` and quotes there may be
  whitespace (`S` = space, tab, CR, LF). An attribute value runs from its quote to the next identical quote.
- **Text** runs up to the next `<`.
- **Comments** run `<!--` … `-->`, **CDATA** `<![CDATA[` … `]]>`, and **PIs** `<?target data?>`. In a PI, data starts
  after the whitespace that follows the target.
- **DOCTYPE.** It starts with `<!DOCTYPE`, then the name and an optional external ID with quoted literals. An optional
  `[` internal subset `]` follows, then `>`. Inside the subset the scanner skips quoted strings, comments and PIs while
  looking for the closing `]`.
- **Decoding.** Values decode `&lt; &gt; &amp; &apos; &quot;`, `&#NNN;` and `&#xHHH;`. Any other `&name;` marks the
  node as raw.
- **Normalisation.**
  - Text and CDATA: CRLF and lone CR become LF. Character references are not normalised.
  - Attribute values: literal tab, LF, CR and CRLF each become one space. Character references are not normalised.

### Encoders and validators

`XmlSyntax`:

- **`int FindInvalidChar(string value)`** returns the index of the first character not allowed in XML 1.0, or -1. That
  means C0 controls other than tab, LF and CR, U+FFFE, U+FFFF and lone surrogates. Every encoder requires -1 and throws
  `ArgumentException` otherwise; the editor (M3) reports it.
- **`string EncodeAttributeValue(string value, char quote)`** returns the content between the quotes:

  | Character | Written as |
  |---|---|
  | `&` | `&amp;` |
  | `<` | `&lt;` |
  | the quote in use | `&quot;` or `&apos;` |
  | tab | `&#9;` |
  | LF | `&#10;` |
  | CR | `&#13;` |
  | everything else | literal |

- **`string EncodeText(string value, string newline)`** escapes `&` → `&amp;`, `<` → `&lt;`, and `>` → `&gt;` only
  directly after `]]`. LF is written as `newline` (the document's), and CR as `&#13;`.
- **`string? EncodeCData(string value, string newline)`** returns the content of one or more CDATA sections. Each `]]>`
  is split as `]]]]><![CDATA[>`, and LF is written as `newline`. It returns null when the value contains a CR, which
  CDATA cannot hold; the editor reports it.
- **`string? EncodeComment(string value)`** returns the value unchanged, or null when it contains `--` or ends with `-`.
- **`bool IsValidName(string name)`** is the XML Name production (`XmlConvert.VerifyName`).
  **`bool IsValidQName(string name)`** is NCName or NCName:NCName.

**Property tests.** For NastyStrings filtered by FindInvalidChar, plus seeded random valid strings, and for both newline
styles:

- `<r a=Q…Q/>` with each quote, parsed with `XmlDocumentSyntax.Parse`, gives an attribute Value equal to the input.
- `<r>…</r>` gives the same for the text value. An empty value makes an empty element, which is checked separately.
- The CDATA encoding, joined into sections, gives the same for the concatenated CDATA Content. CR inputs are excluded
  there and test the null case instead.

All of these go through the full pipeline, including the cross-check.

### Tests and fixtures

Synthetic fixtures in `tests/Cfgedit.Core.Tests/Fixtures/xml/`:

- a web.config-style file (appSettings with `add key value`, connectionStrings, system.web, nested sections, comments)
- an App.config-style file
- a log4net-style file (appenders with nested elements and attributes)
- an SDK-style .csproj
- a file with mixed content, CDATA, PIs, namespaces and prefixes, `xml:space="preserve"`
- a DOCTYPE with an internal subset and a custom text entity (a raw-mode part)
- a DOCTYPE with an entity that contains markup (rejected)
- a DOCTYPE with an ATTLIST default (skipped attribute)
- whitespace inside tags and multi-line attributes, single and double quotes
- `<a/>`, `<a />` and `<a></a>`

Generated in test code:

- UTF-8 with and without a BOM
- UTF-16 LE and BE with a BOM
- UTF-16 without a BOM by pattern
- windows-1252 with bytes 0x80 and 0xE9
- encoding contradictions
- an undefined windows-1252 byte (0x81)

Tests check:

- the byte round trip and the CST shape (names, spans, quotes, raw and decoded values)
- reader error positions for the probe's malformed inputs (mismatched tag, duplicate attribute, unterminated value, `<`
  in a value, U+0001, an undeclared entity, two roots, an undeclared prefix), each with the exact line and column
- the mismatch path, forced through an internal test hook that corrupts one CST position and must produce the "internal
  parse mismatch" error

## Model and edits (M3)

Namespace `Cfgedit.Core.Model`. Everything here is format-neutral, apart from the two format implementations.

### Formats and documents

```csharp
public interface IConfigFormat
{
    string Name { get; }                              // "json", "jsonc", "json5", "xml"
    ConfigDocument Parse(SourceText source);          // CfgeditException on a parse error
    IReadOnlyList<TextEdit> Plan(ConfigDocument document, EditOp op);   // CfgeditException when the op is invalid
    LogicalNode ToLogical(ConfigDocument document);   // see Verification
}

public readonly record struct TextEdit(int Start, int End, string NewText);   // replaces [Start, End) of the old text

public sealed class ConfigDocument
{
    public IConfigFormat Format { get; }
    public SourceText Source { get; }
    public DocNode Root { get; }                      // a Document node
    public object Syntax { get; }                     // the format's CST (JsonDocument or XmlDocumentSyntax)
    public EditResult Apply(EditOp op);               // plan, splice, reparse, verify; CfgeditException on failure
}

public sealed record EditResult(ConfigDocument Document, NodePath? Focus);
```

### DocTree

```csharp
public enum NodeKind
{
    Document,
    JsonObject, JsonArray, JsonScalar, JsonComment,                 // a member or an item, by its value's kind
    XmlElement, XmlText, XmlCData, XmlComment, XmlProcessingInstruction, XmlDeclaration, XmlDoctype,
}

public enum PartKind
{
    Key, Index, Value, Summary,                                      // JSON
    ElementName, EndName, AttributeName, AttributeValue,             // XML
    Text, Comment, PITarget, PIData, ReadOnly,
}

public sealed class DocNode
{
    public NodeKind Kind { get; }
    public NodePath Path { get; }
    public DocNode? Parent { get; }
    public IReadOnlyList<DocNode> Children { get; }
    public IReadOnlyList<DocPart> Parts { get; }      // the node's row
    public IReadOnlyList<DocPart> EndParts { get; }   // the XML end row; empty when there is none
    public int Start { get; }                         // source span, for text-mode cursor mapping
    public int End { get; }
    public JsonValueKind? JsonKind { get; }           // String, Number, True, False, Null, Object, Array (JSON nodes only)
}

public sealed class DocPart
{
    public PartKind Kind { get; }
    public string Value { get; }                      // logical value: decoded, unescaped; for Summary e.g. "3" (the count)
    public bool Editable { get; }
    public bool IsRaw { get; }                        // XML value containing a custom entity: Value is the raw source
    public int Start { get; }                         // source span of the token(s) the part edits
    public int End { get; }
    public int AttributeIndex { get; }                // for AttributeName/AttributeValue; -1 otherwise
}
```

**Parts on a node's row**

| Node | Parts |
|---|---|
| JSON member | `Key` (editable), then `Value` (scalar, editable) or `Summary` (container, not editable) |
| JSON item | `Index` (not editable), then `Value` or `Summary` |
| JSON root value | `Value` or `Summary` only |
| JSON comment | `Comment` (editable) |
| XML element | `ElementName`, then `AttributeName`/`AttributeValue` pairs, then `Text` if the element is text-only (no element, comment, CDATA or PI children and exactly one non-empty text child, or `<a></a>`) |
| XML element with children shown | `EndParts` = [`EndName`] |
| XML text/CDATA | `Text`, but whitespace-only text nodes are not DocNodes |
| XML comment | `Comment` |
| XML PI | `PITarget`, `PIData` |
| XML declaration, doctype | one `ReadOnly` part with the source text |

### NodePath

`NodePath` is an immutable list of steps from the Document node. It has value equality and a readable `ToString` for
the detail line and tests.

- JSON: `Member(key, ordinal)`, where ordinal counts members with the same key before it. `Item(index)` counts items
  only. `Comment(index)` counts comments only.
- XML: `Element(name, ordinal)`, where ordinal counts elements with the same name before it. `Text(index)` counts
  non-whitespace text and CDATA. `Comment(index)`, `PI(index)`, plus `Declaration` and `Doctype`.

`ConfigDocument.Resolve(NodePath) → DocNode?` returns the exact node, or null. `ResolveNearest` returns the deepest
existing ancestor, then the nearest sibling by index.

### Edit operations

```csharp
public abstract record EditOp;
public sealed record SetPart(NodePath Node, PartKind Part, int AttributeIndex, string Value) : EditOp;
public sealed record InsertNode(NodePath Parent, int Index, NewNode Node) : EditOp;          // Index among DocNode children
public sealed record InsertAttribute(NodePath Element, int Index, string Name, string Value) : EditOp;
public sealed record DeleteNode(NodePath Node) : EditOp;
public sealed record DeleteAttribute(NodePath Element, int Index) : EditOp;
public sealed record MoveNode(NodePath Node, int NewIndex) : EditOp;                         // among its parent's children
public sealed record MoveAttribute(NodePath Element, int Index, int NewIndex) : EditOp;
public sealed record ChangeJsonType(NodePath Node, JsonValueKind Kind, string? Value) : EditOp;  // Value for scalars
public sealed record DuplicateNode(NodePath Node) : EditOp;                                  // inserted right after

public abstract record NewNode;
public sealed record NewJsonValue(string? Key, JsonValueKind Kind, string? Value) : NewNode;   // Key for object parents
public sealed record NewJsonComment(string Text, bool Block) : NewNode;
public sealed record NewXmlElement(string Name, IReadOnlyList<(string Name, string Value)> Attributes, string? Text) : NewNode;
public sealed record NewXmlText(string Text, bool CData) : NewNode;
public sealed record NewXmlComment(string Text) : NewNode;
```

**Validation.** It runs in Plan and throws `CfgeditException` with a user message. Each check names what is wrong:

- a duplicate key or attribute (renaming to the same name is a no-op, not an error)
- an invalid number
- an invalid XML name or QName
- a character XML cannot hold
- a CR in CDATA, or `--` in a comment
- a character the file's encoding cannot represent (Plan encodes the new text with `Source.Encoding`)
- an undeclared namespace prefix, reported by the reparse
- an attempt to edit a non-editable part

A `SetPart` whose value equals the current one produces no edits. Apply then returns the same document, and no undo
entry is recorded.

### Formatting inference (planning)

Planning works on the CST and the source text. New text copies the surrounding style. The **indent unit** is inferred
once per document: the most common positive difference in leading whitespace between a line and the previous less-indented
line, tabs kept as tabs. The default is 2 spaces.

**JSON**

- **Multi-line containers.** A container is multi-line when a line break separates its opening bracket from its first
  entry. A new entry goes on its own line:
  - Indentation is that of the previous sibling entry, else the next one, else the container's indentation plus one
    unit.
  - The separator between key and value is copied from a sibling member (e.g. `": "` or `":"`). The default is `": "`.
- **Single-line containers** stay single-line. The separator after a comma is copied from the existing separators
  (`", "` or `","`).
- **Empty containers.** `{}` or `[]` becomes multi-line when the document has more than one line, else single-line.
- **Commas.**
  - Inserting after the last entry adds a comma to the previous last entry.
  - When the container has a trailing comma, the new last entry gets one too.
  - Deleting the last entry removes the previous entry's comma, unless the container uses a trailing comma.
- **Keys.** The quote comes from the previous sibling key, else the next, else the document's first key, else `"`. In
  Json5 a key is bare when the sibling keys are bare and the name is an identifier. String values use the same quote
  rule.
- **Attached comments.**
  - **Leading.** The comments directly above an entry are attached to it. They must form a run where each starts its own
    line, with no blank line between them or before the entry.
  - **Trailing.** A comment that starts on the line where an entry (or its comma) ends is attached to that entry.

  Attached comments are deleted, moved and duplicated with their entry.
- **Deleting a multi-line entry** removes its whole lines, attached comments included, so no blank or whitespace-only
  line is left behind.

**XML**

- **Child indentation** comes from the whitespace text before the previous sibling element, else the next, else the
  parent's indentation plus one unit. The newline is the document's.
- **Elements without children.**
  - Inserting a child into `<a/>` rewrites the start tag as `<a>` and adds the child on its own line, followed by
    `</a>` on its own line at the parent's indentation.
  - The same applies to `<a></a>`.
  - A text-only element `<a>x</a>` with a new element child keeps its text as the first child.
- **Self-closing style.** It is copied from a sibling: `<add ... />` vs `<add .../>`. The default is `<name />`.
- **New attributes.**
  - When the existing attributes are on separate lines, the new one goes on a new line aligned with the previous
    attribute.
  - Otherwise it is separated by one space.
  - The quote comes from the element's other attributes, then the document's majority quote, then `"`.
- **Text values.** `SetPart(Text)` on a `<a/>` element rewrites it as `<a>text</a>`. Setting the text of a text-only
  element to empty gives `<a></a>`, not `<a/>`.
- **Renaming** rewrites both the start name and the end name in one edit. `EndName` edits do the same.
- **Deleting an element** removes its whole lines when it occupies them, together with attached comments (the same
  rules as JSON, using XML comments).

### Verification

`ConfigDocument.Apply` performs these steps:

1. Plan the op.
2. Apply the TextEdits to the text. They must not overlap; they are applied from the end backwards.
3. Encode the new text with the document's encoding. This catches unrepresentable characters.
4. Reparse with the same format.
5. Build `ToLogical` of the new document and compare it with `ToLogical` of the old document after a **logical
   application** of the op.

A `LogicalNode` is the format-neutral content with no formatting: kind, key/name, scalar kind and value, attributes in
order, and children, comments included. The logical application is a small, independent tree transformation, so the
comparison checks that the edit changed exactly what it should and nothing else.

Any failure in steps 2–5 is `CfgeditException("internal edit error: … (please report this)")`, and the document stays
unchanged.

`Focus` in the result is the path of the inserted, renamed, moved or edited node. After a delete it is the next sibling,
else the previous one, else the parent.

### Undo

`EditHistory` holds the current `ConfigDocument`, an undo stack and a redo stack. Each entry is (SourceText before,
SourceText after, focus before, focus after). Undo and redo reparse the stored text. New edits clear redo, and a reload
clears both stacks.

### Tables

`TableModel.TryCreate(ConfigDocument document, NodePath node) → TableModel?`. `node` may be the array or element, or one
of its rows. The rules are the design's "When it is offered".

- `Rows` are the row NodePaths.
- `Columns` are `(PartKind source, string name)`: a JSON key, an XML attribute name, an XML child element name, or the
  text column.
- `Cell(row, column) → (DocNode? node, DocPart? part, bool missing, bool nested)`.
- The editor (M7) turns a cell edit into an EditOp. Editing a missing cell becomes an `InsertAttribute`, or an
  `InsertNode` at the column-ordered position.
