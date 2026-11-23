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

   The reader is walked to the end, and each node is compared as it is read: kind, name, value, depth, IsEmptyElement,
   line and column, and for elements their attributes (name, value, `QuoteChar`, line, column, IsDefault). Neither
   side's nodes are materialised as a list. The scanner runs first, but its failure is
   held back, so a well-formedness error from the reader always wins. Unexpected exceptions in the scanner become the
   internal mismatch error.

   Element nesting is capped at `XmlDocumentSyntax.MaxDepth` = 256 (the root is level 1), as for JSON. Deeper input
   is a `CfgeditException` "nesting is deeper than 256 levels" at the name of the first element beyond the cap.

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
    string Name { get; }                              // "json", "jsonc", "json5", "xml", "env"
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
| XML element | `ElementName`, then `AttributeName`/`AttributeValue` pairs, then `Text` if the element is text-only (no element, comment, CDATA or PI children, and either no text or one text child that is not whitespace containing a line break; `<a></a>` is text-only, `<a>\n</a>` is an empty container with an end row, `<a/>` has neither) |
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
public sealed record MoveNodeTo(NodePath Node, NodePath NewParent, int NewIndex, string? NewKey) : EditOp;  // into another container

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

### `.env` format (M9)

Syntax in `Cfgedit.Core.Env`, the tree and edits in `Cfgedit.Core.Model`. `EnvFormat` is chosen by name (`env`) or by
`Formats.ForPath` for `.env`, `.env.*` and `*.env`.

- **Syntax.** `EnvDocument` keeps the source and its items in file order: `EnvBlankLine`, `EnvCommentLine` and `EnvEntry`
  (key and span, value, `EnvQuote` style, trailing `EnvComment`). `EnvParser` is line based; a quoted value can span lines,
  and a bad line throws a `CfgeditException` with the line and column. `EnvEncoder` writes a value token: `Encode` in the
  plainest form, `EncodeKeeping` in a given style when it can hold the value, `NeedsLoaderWarning` and `StyleOf` for the note.
- **Tree.** `EnvModelBuilder.BuildTree` makes one `EnvGroup` per `__` segment, where its first entry is, and `EnvEntry` and
  `EnvComment` leaves. Groups have a `Key` and `Summary` part, entries a `Key` and `Value` part, and a comment a `Comment`
  part; the root is an `EnvGroup` with a `Root` part (`$`). Steps are `MemberStep` (with an ordinal for duplicate keys) and
  `CommentStep`. `ItemsOf(document, node)` returns the items a node covers: a group its entries and comments, and a trailing
  comment the line of its entry.
- **Logical tree.** `EnvModelBuilder.BuildLogical` is flat and in file order: a string scalar per entry, named by its full
  key, and a comment per comment line. It is what `Verification` compares, so formatting is not in it.
- **Edits.** `EnvEditPlanner` plans every operation: set part, insert (`NewEnvEntry`, `NewEnvComment`), delete, move,
  duplicate. It refuses change type and attributes. `EnvLayout.Block` decides which children move, copy or delete together
  (an entry with its attached comments); `EditLayout.Group` delegates to it for a group parent. `EnvLogicalEdit` applies
  each operation to the logical tree, and its `FocusAfter` is the hook `ConfigDocument` calls for .env, because groups are
  virtual and a node's place can move with its key.
- **TUI.** The only format-specific code is by node kind: `RowLayout.EnvItems` (group, entry and comment rows),
  `PathText.EnvPath` (the full key, `$` for the root, comments as in JSON), `AddKinds` (entry and comment in a group),
  `FirstValue` (an entry's value opens after an add), and `MainWindow` (the loader note after a value is written, and the
  refusals of `t` and `T`).

### TOML format (M10)

Syntax in `Cfgedit.Core.Toml`, the merged model, tree and edits in `Cfgedit.Core.Model`. `TomlFormat` is chosen by name (`toml`)
or by `Formats.ForPath` for `.toml`.

- **Syntax.** `TomlDocument.Parse` builds a lossless CST, as the JSON family does: `TomlItem`s (key/value, `TomlTableItem` and
  `TomlArrayTableItem` headers, comments, blank lines), `TomlKey` with its `TomlKeyPart`s, and `TomlValue`s (`TomlString`,
  `TomlInteger`, `TomlFloat`, `TomlBoolean`, `TomlDateTime`, `TomlArray`, `TomlInlineTable`). `TomlParser` reports the line and
  column of a syntax error. `TomlEncoder` writes keys, strings (keeping a string's form when it can hold the value) and numbers;
  `TomlStrings`, `TomlScalars` and `TomlValues` hold the grammar helpers and the canonical values.
- **Merged model.** `TomlMerger` (`TomlMerge.cs`) merges the items into `MergedTable`, `MergedArray`, `MergedScalar` and
  `MergedMember`. Each table keeps every piece of text that defines it, and its `TomlTableState`. The TOML rules about tables and
  keys are checked here, during parse, and so are the two constructs Tomlyn misreads (a `[x.sub]` header, or a `[[x]]` item,
  after another table follows an array of tables): both are refused with a `CfgeditException`, and the file opens in text mode.
- **Tree.** `TomlModelBuilder.Build` makes the document tree: `TomlTable` rows (the root, header, implicit, dotted and inline
  tables, and items of arrays of tables), `TomlArray` rows (static arrays and arrays of tables), `TomlScalar` rows (with
  `DocNode.TomlKind`) and `TomlComment` rows. Steps are `MemberStep`, `ItemStep` and `CommentStep`. `TomlModel` keeps the syntax,
  the merged model, the tree, and the pieces of each node. Its `ShapeOf` gives a node's `TomlContainer` shape (root, header,
  implicit, dotted, inline, array of tables, array), which the planner and `SiblingShape` use to decide what can be added where.
- **Logical tree.** `TomlModel.DataLogical` is the data (tables as objects, arrays as arrays, scalars with kind and canonical
  value). `ToLogical` adds the `#comments` array, every comment in file order. Check 1 of `Verification` compares the logical
  edit with the tree after the edit. `TomlCrossCheck` (Tomlyn, used only as a verifier) compares the data with Tomlyn's parse of
  the new text, after cfgedit's own reparse. A Tomlyn error on text cfgedit accepts is an internal parse mismatch.
- **Edits.** `TomlEditPlanner` plans every operation as text edits: set part (values, keys, comments), insert (`NewTomlValue`,
  `NewTomlComment`, with the placements of each container shape), delete, move, duplicate and `ChangeTomlType`. `SiblingShape`
  builds the shaped inserts (`ConfigDocument.ShapeLike`). `TomlLogicalEdit` applies each operation to the logical tree. `TomlLayout`
  groups the pieces of text that move, copy or delete together (a key/value with its comments, a header section, the pieces of a
  table), as `TomlUnit`s. `TomlLogicalEdit.FocusAfter` is the hook `ConfigDocument` calls for TOML, because a renamed table can
  change place.
- **TUI.** The format-specific code is by node kind and TOML kind: `RowLayout.TomlItems` (rows as JSON's rows are, with the
  scalar styles), `PathText.TomlPath` and `TomlKindName` (dotted keys, quoted when not bare, `[n]` for items, and the TOML kind
  names), `AddKinds` (the TOML kinds, with `Key` for a table's member and `Value` for a date-time), `EditRules` (a TOML key is
  edited only in the single-line field), `MainWindow.ChangeTomlTypeOf` (the type menu, with its date-time text, and the planner's
  refusals shown as information), and `CfgeditHost` (the format choice and `.toml`). `T` on TOML says the table editor is not
  available yet.
- **Tests.** Core, in `tests/Cfgedit.Core.Tests/Toml`: `TomlConformanceTests` (the toml-test cases: valid ones parse and match
  Tomlyn, invalid ones are refused), `TomlParserTests`, `TomlEncoderTests`, `TomlSemanticTests`, `TomlCrossCheckTests` and
  `TomlPropertyTests` (randomised values through the encoder). The TUI is tested in `EditingWindowTests.Toml.cs`,
  `RowLayoutTests`, `PathTextTests`, `EditRulesTests` and `ProgramTests`.

### YAML format (M11)

Syntax in `Cfgedit.Core.Yaml`, the model, tree and edits in `Cfgedit.Core.Model`. `YamlFormat` is chosen by name (`yaml`) or by
`Formats.ForPath` for `.yaml` and `.yml`. The subset is in the design doc (YAML, M11); the user's view is in the user guide.

- **Syntax.** `YamlDocument.Parse` builds a lossless CST: `YamlDocumentNode`s (with their markers and directives, from
  `YamlDocuments`), block and flow collections (`YamlBlockMapping`, `YamlBlockSequence`, `YamlFlowMapping`, `YamlFlowSequence`, with
  their entries and items), `YamlScalar`s (with `YamlScalarStyle`), `YamlAlias`es and `YamlComment`s, and every token keeps its
  span, its indentation and its trivia. `YamlParser` (with `YamlParser.Block`, `.Flow` and `.Scalars`) reports the line and column
  of a syntax error. `YamlEncoder` writes keys, and scalars in the plainest style that holds the value (plain, single quoted, double
  quoted, or a literal block for line breaks), and says which core-schema kind a plain text reads as. `YamlKinds` and `YamlValues`
  give a scalar's `YamlValueKind` (the core schema, with a core tag honoured and checked) and its decoded value; `YamlTags` resolves
  tag handles.
- **Refusals.** `YamlLimits.Check` refuses, after the parse, the shapes that YamlDotNet reads differently (a tab on a blank line
  between entries, a block scalar whose end is ambiguous at the end of the text): they are refused with a message and the file opens
  in text mode, as the TOML cross-check does. Everything else the parser refuses is a `CfgeditException` with a position.
- **Model.** `YamlModelBuilder.Build` makes the document tree from the CST: a single document's root is the root row (`$`), several
  documents are `YamlDocument` rows under the document node, and an empty stream is an empty mapping. `YamlComments.Assign` decides where
  each comment is shown (with the entry below it, as a trailing row, or in place). Steps are `MemberStep`, `ItemStep` (documents too)
  and `CommentStep`. A mapping member's row has its `Key`, then its anchor and tag (`PartKind.Anchor`, `PartKind.Tag`, read-only), then
  its `Value`, `Alias` or `Summary`. `YamlModel` keeps the syntax, the tree, and the syntax each row was built from (`SyntaxOf`).
- **Logical tree.** `YamlModel.DataLogical` (`YamlLogical.Data`) is the data: mappings with scalar keys, sequences, and scalars as
  text with their plain-or-not form, with anchors, aliases and tags by name. `ToLogical` adds the `#comments` array in file order.
  Check 1 of `Verification` compares the logical edit with the tree after the edit. `YamlCrossCheck` (YamlDotNet's
  `RepresentationModel`, used only as a verifier) is run on opening and on every edit, after cfgedit's own reparse, and its result is
  converted to the same shape. A disagreement on opening opens the file in text mode with a message; after an edit it is an internal
  parse mismatch, which is cfgedit's bug.
- **Logical shape.** `YamlLogicalEdit.Apply` applies each operation to the logical tree (called from `LogicalEdit`), and
  `YamlLogicalEdit.FocusAfter` is the hook `ConfigDocument` calls for YAML to find the focus after an edit.
- **Edits.** `YamlEditPlanner` plans every operation as text edits. Set part: a scalar value keeps its style when the style can hold
  the new text (a plain value keeps plain only when the new text reads as the same core-schema kind); a key is written plain or
  quoted; a folded scalar stays folded only when it folds back to the same value. Insert: into a block mapping or sequence at the
  siblings' indentation and dash style, into flow collections as JSON, into an empty `{}` or `[]` (which turns to block style), and
  a compact `- key: value` item for an empty `{}` that is a sequence item. Delete, duplicate (anchors are removed from the copies) and
  change type (`ChangeYamlType`) follow the same rules. An anchor that an alias still refers to cannot be deleted; an alias can only
  be wrapped in a sequence (change type to sequence wraps a value, a null becomes `[]`); documents can be deleted and moved, but adding a document is refused. `YamlMoves` (the units of text that move
  together, with their comments, and the refusal of a move that would split one) is the move helper that `YamlEditPlanner.PlanMove`
  uses.
- **Shape copies.** `SiblingShape` builds the shaped inserts of YAML sequences of scalars and mappings (`ConfigDocument.ShapeLike`).
- **TUI.** `RowLayout.YamlItems` and `YamlScalarItems` (rows with YAML's `: ` separator; anchors, tags and aliases read-only before the
  value; a null with no text as a dim `null`), `PathText.YamlPath` and `YamlKindName` (`---[n]` for documents, keys quoted through
  `YamlEncoder.EncodeKey`), `AddKinds` (the YAML kinds for a mapping or a sequence; none for a document), `EditRules` (a YAML key is one
  line; anchors, tags, aliases and indexes are not editable), `FirstValue` (a YAML scalar's value editor opens after an add),
  `MainWindow.ChangeYamlTypeOf` (the type menu; an alias opens it with sequence preselected), and `CfgeditHost` (the format choice, `.yaml`). `T`
  on YAML says the table editor is not available yet.
- **Tests.** Core, in `tests/Cfgedit.Core.Tests/Yaml`: `YamlConformanceTests` and `YamlFixtureTests` (the yaml-test-suite, see the
  design doc), `YamlParserTests`, `YamlScalarTests`, `YamlEncoderTests`, `YamlCrossCheckTests`, `YamlRefusalTests`,
  `YamlModelConformanceTests`, and the edit sweeps. The TUI is tested in `EditingWindowTests.Yaml.cs`, `RowLayoutTests`, `PathTextTests`,
  `EditRulesTests` and `ProgramTests`.

## Read-only TUI (M4)

Projects `src/Cfgedit` (namespace `Cfgedit.Tui`) and `tests/Cfgedit.Tests`. Follow logvu's structure (`LogvuHost`,
`MainWindow`, custom `View` drawing as in `DetailView`) and reuse Shared.Tui unchanged: `RecentPicker`, `HintsLine` and
`KeyHints`, `Confirm`, `TextWidth`, `DisplayWidthSetup`, `Osc52` and `SearchBar`. Copy `tests/Logvu.Tests/TerminalHarness.cs`
whole into `tests/Cfgedit.Tests`, changing only the namespace.

### Program and host

- **Arguments.** `cfgedit <file> [--format xml|json|jsonc|json5|env|toml]` and `cfgedit` (picker). Options may come before or
  after the file. `--check` and new files come later (M5b, M5). Until then, a missing file is an error.
- **Format resolution.**
  1. `--format`, else `Formats.ForPath`.
  2. Else sniff the first non-whitespace character of the decoded text (after a BOM): `<` → xml, `{` or `[` → json5.
  3. Else the error "cannot tell the format of <file>; use --format".
- **Load errors** (`CfgeditException`) print `cfgedit: <path>:<line>:<col>: <message>` (or without the position when
  there is none) to stderr and exit 1. Text mode (M5b) replaces this for parse errors later.
- **Recent files** are `RecentFiles.DefaultStorePathFor("cfgedit")`. A file is recorded after it opens.
- **`CfgeditHost.Run(ConfigDocument doc, string path, RecentFiles? recent)`** and **`RunPicker(RecentFiles?)`**, as in
  LogvuHost: `RecentPicker<OpenedFile>` with Open file, and Ctrl+O in the main window shows the picker over the open
  file. The unsaved-changes check comes in M5. `OpenedFile` is a small record (path, ConfigDocument).

### Rows (pure, no Terminal.Gui types)

`DocRows` flattens the DocTree into visible rows, given a collapsed set:

- A row is `(DocNode Node, bool IsEnd)`. The end row of an expanded XML element that has `EndParts` follows its
  children.
- The Document node itself is not a row; its children are top-level rows.
- **The collapsed set** is a `HashSet<NodePath>`. A node is collapsible when it has children. JSON containers with no
  children are not collapsible.
- **Initial state.** Every collapsible node at depth > 3 is collapsed (top-level rows are depth 1), and so is any
  collapsible node with more than 200 children. Everything else is expanded.
- After an edit or undo the collapsed set is kept by NodePath. Paths that no longer resolve are dropped.

`RowLayout` turns a row into display lines for a given width:

- **Line layout:**
  - Each line has an indent (2 columns per depth level) and a fold glyph (`▾`, `▸`, or two spaces), then segments.
  - A segment is `(string Text, StyleKind Style, int PartIndex)`, where PartIndex is -1 for decoration and an index into
    the row's Parts (or EndParts for an end row) otherwise.
  - The result is a list of `DisplayLine(int Indent, IReadOnlyList<Segment> Segments)` plus, for each part, its
    (line, start column, end column) so that the view can draw the cursor and map mouse clicks.
- **JSON:**
  - A member is `key: value`, an item `0: value` (the index dimmed), and a container's value `{3}` / `[5]` (Summary).
  - Strings have no quotes. An empty string is a dimmed `""`.
  - Numbers, bools and null are written as they are, each in its own style.
  - Comments are `// text` or `/* text */`, dimmed.
- **XML:**
  - Element: `<name a="v" b="w">`, followed by the text-only text and `</name>`, or ending in `/>` for `<a/>`.
  - Collapsed element with children: `<name …>…</name>`.
  - End row: `</name>`.
  - Text as it is. CDATA as `<![CDATA[text]]>` with dimmed markers. Comments as `<!-- text -->` and PIs as
    `<?target data?>`.
  - Declaration and doctype as their source text, dimmed.
  - Attribute values are shown in double quotes for readability, whatever the source quote, with the logical value
    inside (no escaping).
- **Visible control characters.** Inside any value, LF shows as `⏎`, TAB as `→`, CR as `␍`, and other C0 controls as
  `·`, each in a glyph style. They are one display cell each.
- **Raw parts.** A part with `IsRaw` is followed by a dimmed ` raw` marker.
- **Wrapping:**
  - XML element rows wrap attributes as the design says: an attribute (`name="value"`) that doesn't fit goes to the next
    line, aligned under the first attribute. If that alignment would leave fewer than 24 columns, the continuation indent
    is the row indent + 4.
  - A single attribute wider than a whole line has its value truncated with `…`.
  - Every other row is one line, truncated with `…` at the width. The cut happens inside the last part, and decoration
    after it is dropped.
- **Widths.** All measuring uses `TextWidth`/`DisplayWidth`, per grapheme. Truncation never splits a grapheme or a
  surrogate pair.

`StyleKind`: Key, Index, String, Number, Bool, Null, Summary, Punctuation, Comment, ElementName, AttributeName,
AttributeValue, Text, CData, ReadOnly, Glyph, RawMarker, Fold. `TreeColors` maps them to Terminal.Gui attributes, with
the dimmed ones using `TextStyle.Faint`, as logvu does.

### Tree view

`TreeView : View` draws the visible rows from a top row.

- **Cursor.** The cursor is `(row index, part index)`.
  - The focused part is drawn in reverse video, and the rest of its row is lightly highlighted (background tint).
  - Every part is focusable, editable or not.
  - Moving between rows keeps the part index where the new row has one. Otherwise it goes to the nearest one. A
    "preferred part index" is remembered across rows that have fewer parts, as editors do for columns.
- **Keys** (the read-only subset of the design table):
  - Up/Down and k/j move by row. Left/Right and h/l move by part; Left on the first part goes to the parent row.
  - Home/End and 0/$ go to the first/last part. PgUp/PgDn move a page; g/G go to the first/last row.
  - Space folds and unfolds. On a leaf it folds the parent and moves the cursor there.
  - `*` expands the whole subtree. `-` collapses the parent and moves there.
- **Mouse.** A click focuses the part under it, or the row's first part when the click misses every part. A click on
  the fold glyph toggles it. The wheel scrolls 3 rows.
- **Scrolling.** The viewport keeps the cursor row visible (all of its display lines), scrolling by display lines.
- **Events.** `CursorChanged` fires for the detail line.

### Main window

- **Layout.** The title is the file name, plus `[json5]` / `[xml]` and so on. The tree fills the window except the
  bottom two lines.
- **Detail line:**
  - The focused part's path: JSON as `$.a.b[2]`, quoting keys that aren't identifiers as `$["a b"]`. XML as
    `/configuration/appSettings/add[2]/@value`, with `[n]` 1-based and only when same-name siblings exist; text as
    `text()[n]` and comments as `comment()[n]`.
  - Then two spaces and the part's kind (string, number, bool, null, object, array, element, attribute, text, comment,
    CDATA, PI).
  - Then two spaces and the full logical value with the same glyphs, truncated at the width.
- **Hints line:** `HintsLine` with the read-only keys: `Space Fold  ←→ Part  ^O Open  ^Q Quit`.
- **Ctrl+Q** quits.

### Tests (M4)

- **RowLayout:** exact segment lists and part ranges for every node kind and style, plus control-character glyphs, raw
  markers, truncation at graphemes (ZWJ emoji, CJK wide characters, combining marks), and XML attribute wrapping at
  several widths (alignment, the 24-column fallback, an oversize attribute).
- **DocRows:** flattening, end rows, initial collapse rules, and keeping collapse state across an edit (applying an op
  with `ConfigDocument.Apply`).
- **Tree view** through `TerminalHarness`: drawn text and attributes for representative JSON and XML, every key above,
  mouse clicks and the wheel, scrolling with wrapped rows, and the cursor and preferred part.
- **Main window:** layout at 80×24 and 40×12, and the detail line for JSON and XML paths, including quoted keys and
  same-name ordinals.
- **Program:** arguments, format resolution and sniffing, error output, and exit codes.

## Editing TUI (M5)

### Exact saving (Shared.Core)

`AtomicFile.Write` appends a `'\n'` to the content, as cmdr's and logvu's JSON stores want. cfgedit must write exactly
the encoded bytes.

- Add `AtomicFile.WriteExact(string path, ReadOnlySpan<byte> content)`, which takes the same paths: symlink resolution,
  mode, owner and group, the Windows ACL, the in-place fallbacks, and new-file ownership under sudo. The only difference
  is that no `'\n'` is added.
- Implement both entry points with one internal method and a `trailingNewline` flag. Do not duplicate the code. Existing
  callers and their behaviour are unchanged, and all Shared.Core tests stay green.
- Tests: the exact bytes (with and without a final newline, CRLF content, empty content), plus the main hardened paths
  repeated for WriteExact (symlink, kept mode, in-place under `CMDR_KEEP_OWNER`).

### DocumentFile (Cfgedit.Core)

`DocumentFile` connects a ConfigDocument to its file on disk.

- **Opening.** `DocumentFile.Open(string path, IConfigFormat format)` reads the bytes once and loads the document. It
  records a snapshot of what was read: the bytes' SHA-256, the length, `FileIdentity`, and the last write time.
- **New files.**
  - `DocumentFile.CreateNew(string path, IConfigFormat format, string? xmlRootName)` builds an unsaved document from the
    initial text in the design: `{}` for the JSON family; for XML, the declaration plus `<root />` (the given name).
  - The text is UTF-8 without a BOM, with `Environment.NewLine` and a final newline.
  - `Exists` is false until the first save.
- **External changes.** `bool ChangedOnDisk()` compares the current file with the snapshot. Missing vs present counts,
  as do identity, length and hash. The hash is computed only when the cheap checks pass, so the common case doesn't read
  the file.
- **Saving.** `Save(ConfigDocument doc)`:
  1. Fails with `CfgeditException("the folder … does not exist")` if the parent directory is missing. cfgedit never
     creates folders.
  2. Encodes `doc.Source`.
  3. Writes with `AtomicFile.WriteExact`. `IOException` and `UnauthorizedAccessException` become `CfgeditException`
     with the path.
  4. Re-reads the file, and the bytes must equal what was written. Otherwise it fails with "the saved file does not
     match what was written".
  5. Updates the snapshot and sets `Exists = true`.
- **Reloading.** `Reload()` re-opens the file from disk and returns the new document.

### Move helpers (Cfgedit.Core)

`ConfigDocument.MoveBy(NodePath node, int delta) → MoveNode?` moves a node one sibling group up (−1) or down (+1).

- A group is a node together with its attached comments. A free comment is its own group.
- It returns the MoveNode with the NewIndex convention already applied, or null at the first or last position.
- `MoveAttributeBy(NodePath element, int index, int delta) → MoveAttribute?` does the same for attributes.

### EditController (Cfgedit, no Terminal.Gui types)

The UI-independent state of an open file: the `DocumentFile`, an `EditHistory`, and the last error or info message.

- `Apply(EditOp op, out string? error) → EditResult?` catches `CfgeditException` into `error`.
- It also has `Undo` and `Redo` (each returning the focus path), `IsDirty`, `Save()` (returning an error string or
  null), and `Reload()`.
- A new, unsaved file counts as dirty until its first save.
- The flows below are methods that take the confirmations as delegates. This keeps them testable without dialogs.
- **Save flow.** When `ChangedOnDisk()` holds, ask Overwrite / Reload / Cancel, with Overwrite as the default (the user
  started a save). Reload discards the undo history and asks a second confirmation first, with Reload as the default.
- **Quit and switch flow** (Ctrl+Q, Ctrl+O). When the file is dirty, ask Save / Discard / Cancel, with Save as the
  default. A failed save keeps the window open and shows the error.

### Editing in the tree (Cfgedit)

**Inline editor**

- Enter or F2 on an editable part opens a `TextField` over the part's range on its display line and holds the part's logical value.
  The field is as wide as its text plus two columns, one for the cursor and one spare (at least `InlineEditor.MinWidth`, 10), capped at the
  columns left in the tree or table from its X. Its width is a `Dim.Func`, so it follows typing, moves and resizes.
- **Which editor.** Following cmdr's FieldDraft lesson, a value is edited inline only when a TextField round trip
  reproduces it exactly: set `Text`, read it back, compare. Otherwise:
  - If it has no CR (`EditRules.CanEditMultiline`), F2 and Enter open the multi-line editor instead. The editor keeps
    any text exactly, but a terminal draws a CR as a line break, so a value with a CR is not shown there.
  - Otherwise (e.g. a value with a lone CR), the part is read-only in the tree, and the message says "this value can
    only be edited in text mode (Ctrl+T)".
- **Keys in the field.**
  - Enter commits with `SetPart`. Esc cancels.
  - Tab commits and moves to the next editable part (this row, then later rows), opening the editor there. Shift+Tab
    goes to the previous one.
  - F4 switches to the multi-line editor with the current text.
  - When the text is unchanged, committing just closes the editor.
- **Errors.** An Apply error is shown in the message line in the error style, and the field stays open with the text as
  typed.
- **Bool and null values.** Enter or F2 on true, false or null opens the type menu with the current type selected. Enter
  never changes a value by itself: every edit goes through an editor or menu and an explicit commit (user decision,
  2026-11-22).

**Multi-line editor.** A dialog with a Shared.Tui `TextArea` (`TabInserts = false`, `LineBreak = "\n"`, the same
control as cmdr's `FieldDraft.Area`). Enter and paste add only LF, so the text is applied as it is read.

- Buttons Cancel and OK; OK is last, so it is the default.
- Ctrl+Enter or F2 commits, Esc cancels.
- It is sized from the screen at every layout (the 06-03 lesson). Check it with the pty driver, since dialogs have hidden
  their bottom rows before.

**Structure keys** (design table):

- **`a` / `i`** add after / before the focused node, using `InsertionIndexAfter/Before`. On a container's summary part
  (`{n}`, `[n]`, and the `{0}` of an empty XML element, which `XmlModelBuilder` gives a zero-width `PartKind.Summary` part) they add
  inside it: `a` at the end, `i` at the start. On the JSON root's `PartKind.Root` marker (`$`) they
  add beside the root value, in the document.
  - On an attribute part they add an attribute: a dialog asks for the name, then the value is edited inline.
  - XML element, JSON object item, or JSON scalar item with a sibling: no dialog. The new node copies the sibling's shape
    with empty values, as TableModel.AddRow does (reuse its logic), and the first empty value is opened in the inline
    editor.
  - JSON member: a dialog asks for the key and type, and the value editor opens next for string and number.
  - Anything else: a dialog with the kinds allowed there.
- **`A` / `I`** add a child as the last / first child of an element or container, from any part of the node, so `<a/>` can
  get one too.
  **`+`** adds an attribute to the focused element.
- **Delete** deletes the focused attribute when an attribute part is focused, otherwise the node, with no
  confirmation.
- **Alt+Up/Down and `K`/`J`** use `MoveBy`. **Alt+Left/Right** use `MoveAttributeBy`.
- **Alt+Shift+Up/Down** use `NestBy`, which returns a `NestStep` (a `MoveNode` next to a non-container, else a
  `MoveNodeTo`; null at the root's edge). When `NeedsKey` is set (an item entering a keyed container) `TextPromptDialog`
  asks for the key and the op is applied with it. `MoveNodeTo.NewIndex` is an index among the destination's children
  before the edit; each planner removes the group as delete does and writes it where an insert at that index goes,
  re-indented with `EditLayout.IndentedLines` (lines that begin inside comments or multi-line strings keep their text).
  `ConfigDocument.MovedNodePath` gives the focus for JSON, XML and YAML; TOML and `.env` find the moved node themselves.
- **`t`** opens the JSON type menu.
  - To Number: the current text if it is a valid number, else "0". To String: the current raw text of a scalar, else "".
  - Container → scalar or object with children asks first, with Change as the default. To array wraps the value and asks nothing.
  - On XML: "types apply to JSON only".
- **`d`** duplicates the focused node (`DuplicateNode`), with the cursor on the copy. In the table editor `d` duplicates the row
  (`TableModel.DuplicateRow`), and Delete clears the cell or deletes the row on the index column.
- **Ctrl+Z / `u`** undo. **Ctrl+Y / Ctrl+R** redo.
- **Ctrl+S** saves.

**Other changes**

- **Focus.** After every op, the tree's `SetDocument` places the cursor on the result's Focus. After an insert, the editor
  opens on the new node's first value or name, as described above.
- **Messages.** One message line, the detail line's row, shows a transient error or info message ("Saved", "Nothing to
  undo") for 4 seconds or until the next key. It invalidates the cached hints so that they come back (the 06-06 lesson).
- **Title.** It shows `*` after the file name while dirty.

**New files (Program and host)**

- `cfgedit <path>` for a missing path asks "Create <name>?" with Create as the default.
- The format comes from `--format`, the extension, or a choice dialog.
- XML asks for the root element name (validated with `IsValidQName`).
- The window opens dirty and unsaved, and the first Ctrl+S writes the file.
- The picker's New file action needs a RecentPicker extension and is deferred.

### Tests (M5)

- **Core:** AtomicFile.WriteExact, DocumentFile (open, new, changed on disk in every way, save postcondition, missing
  folder, reload), and MoveBy/MoveAttributeBy with comment groups.
- **EditController** flows with fake confirmations: save with an external change (each answer), quit and switch when
  dirty (each answer, plus a failing save), the dirty state of new files, and undo/redo focus.
- **TerminalHarness:**
  - the inline editor opening at the right position (including a wrapped attribute value), committing, cancelling, Tab
    and Shift+Tab, errors keeping the field, the read-only fallback, F4
  - the multi-line editor
  - every structure key, with the resulting document text asserted
  - the message line and title marker
- **pty check** (by the reviewer) of the inline editor, the dialogs and the quit confirmation at 80×24 and 40×12.

## Text mode (M5b)

### Opening files that do not parse

- **DocumentFile.** `DocumentFile.Open` no longer throws for parse errors. It returns a file whose `Document` is null and
  whose `Text` (SourceText) and `ParseError` (CfgeditException, with line and column) are set.
- **What still throws.** Decoding errors (invalid bytes, unsupported encodings) still throw, since there is no text to
  show. So do missing files and access errors.
- **Program.** A file that does not parse opens the TUI in text mode instead of printing the error. `--check` (below)
  keeps the command-line behaviour.

### The text view

- **Control.** `TextModeView` is a Shared.Tui `TextArea` (a `Terminal.Gui.Editor.Editor`, which replaces the obsolete
  `TextView`) with `TabInserts = true`, filling the tree's place in the window. The title gets a `[text]` tag.
- **Exact text.** The editor keeps its text as given, so the view holds `SourceText.Text` verbatim and
  `EditController.SetText(text, caretOffset)` takes it back unchanged, with the caret as an offset into it. No line
  break is converted in either direction, and a file with mixed line endings is editable. `ShowText` sets the view's
  `LineBreak` to `SourceText.Newline` (the dominant style), so Enter and each line break of a paste insert it.
- **Leaving with no changes.** If the text read back equals the text that went in, the switch back changes nothing and
  records no undo step.
- **Errors.** An open parse error places the cursor at its line and column and shows `line:col: message` in the message
  line, in the error style.

### Keys in text mode

- **F5** reparses.
  - Success: switch to the tree. The edit is one undo step (`EditHistory.RecordExternal`), or, when the file never
    parsed, the history starts from the new document.
  - Failure: the cursor moves to the error, and the message shows it.
- **Ctrl+T** switches between tree and text at any time.
  - Tree → text: the caret goes to the focused part's source offset (`DocPart.Start`).
  - Text → tree: reparse first, as F5 does. On success the tree focuses the deepest node whose span contains the
    cursor's offset.
- **Ctrl+S** in text mode reparses first. If the text parses, the save is the normal one. If it doesn't, the user is
  asked "<line:col: message>. Save anyway?" with Cancel and Save, Save being the default. The raw text is then saved with
  `DocumentFile.SaveText(SourceText)`, through the same exact-write path and postcondition as `Save`.
- **Ctrl+Q and Ctrl+O** use the unsaved-changes flow. Dirty means the current text differs from the bytes last loaded or
  saved, in text mode as in tree mode.
- **Ctrl+Z / Ctrl+Y** in text mode are the editor's own undo and redo of typing. Document-level undo applies in tree
  mode.
- **Esc** does nothing in text mode, so it can't lose edits by accident.

### `--check`

- `cfgedit --check <file> [--format …]` parses the file without starting the TUI.
  - Success: prints nothing and exits 0.
  - Failure: prints `path:line:col: message` (or `path: message` when there is no position) to stderr and exits 1.
- Usage errors exit 2.
- Format resolution is the same as for opening.

### Tests (M5b)

- **DocumentFile:** Open on a parse error gives the text and the error; on a decoding error it throws. SaveText writes
  exact bytes.
- **Exact text:** mixed endings editable and saved byte for byte; Enter and paste insert the file's line break (LF and
  CRLF); single-line pastes drawn at once; caret offsets across CRLF and multi-unit graphemes. (Originally a round-trip
  rule for the TextView, which normalised line breaks; replaced with the move to `TextArea`, 2026-11-23.)
- **TerminalHarness:**
  - opening a broken file: text mode, cursor at the error, the message
  - F5 failing, then succeeding: the tree and the undo step
  - Ctrl+T both ways with cursor mapping (JSON and XML)
  - Ctrl+S on unparseable text (both answers)
  - Ctrl+Q dirty in text mode
  - the mixed-endings file edited and saved with its other line breaks unchanged
- **Program:** `--check` exit codes and output, and a broken file now opening the TUI (through the host delegate).
- **pty check** by the reviewer.
