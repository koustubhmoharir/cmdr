# cfgedit design (proposal)

cfgedit is a .NET 10 terminal editor for config files on Windows and Linux. It is built next to cmdr and logvu and uses the
repository's Terminal.Gui fork. It shows a file as a tree and edits it one structural part at a time: a key, a value, an
element or attribute name, or a text node. The user types the logical value and never deals with escaping.

Phase 1: XML, JSON, JSONC (JSON with comments and trailing commas) and JSON5. Phase 2: .env, TOML and YAML.

## Guiding rule: lossless round trip

Config files are reviewed in diffs, so cfgedit must never reformat a file it did not need to touch.

- Opening a file and saving it without edits produces the same bytes. That covers the encoding, BOM, line endings, the
  final newline, comments, blank lines, quote style, attribute order, whitespace inside tags, number spelling (`1.0`, `0x1F`,
  `1e3`), and escapes in values that were not edited.
- An edit rewrites only the source span of the part it changes. A value edit replaces only the value token. A rename
  replaces only the name token(s).
- An inserted node copies formatting from its neighbours: indentation, newline style, separator spacing (`"a": 1` vs
  `"a":1`), quote character, and whether siblings use trailing commas.
- Every edit is checked. The new text is reparsed and the edited part must read back as the value the user typed. If the
  reparse fails or the value differs, the edit is rejected with an internal-error message and the document stays unchanged.
  A bug in cfgedit can therefore fail an edit, but it cannot corrupt the file.

This rule rules out System.Text.Json (it drops comments, has no JSON5 support, and changes number spelling) and
saving through XmlDocument/XDocument/XmlWriter (they drop attribute quote style and whitespace inside tags, and expand
entities). cfgedit has its own lossless JSON parser. For XML, .NET's XmlReader decides what is well-formed and what the
values decode to, and cfgedit's own scanner only finds where each part's text starts and ends (see XML below).

## Projects

Same layout as logvu:

| Project | Contents |
|---|---|
| `src/Cfgedit.Core` | No Terminal.Gui. Source text and encodings, JSON and XML lossless parsers and encoders, the format-neutral document model, edit operations, undo, table detection. |
| `src/Cfgedit` | The `cfgedit` executable: Program, host, main window, tree and table views, inline editor, dialogs. |
| `tests/Cfgedit.Core.Tests`, `tests/Cfgedit.Tests` | Unit tests, a round-trip corpus, key-handling tests with the fake driver. |

Reused unchanged: Shared.Core (AtomicFile, FileIdentity, RecentFiles, DisplayWidth, SearchPattern/SearchHistory) and
Shared.Tui (Confirm, HintsLine/KeyHints, SearchBar, RecentPicker, Osc52, TextWidth, DisplayWidthSetup). Changes: add the
projects to `Cmdr.slnx`, add `cfgedit` to `publish.ps1`, and link to `docs/cfgedit.md` from the README.

## Command line

```
cfgedit <file> [--format xml|json|jsonc|json5|env]
cfgedit                      # recent-files picker with an Open file action
cfgedit --version
```

The format comes from the extension: `.json5` → JSON5. `.json` → JSONC, which accepts comments and trailing commas when
the file has them (appsettings.json and tsconfig.json do) and never adds them. `.xml .config .csproj .props .targets .xaml
.resx .nuspec .xsd .xslt .svg` → XML. Other extensions are sniffed by the first non-blank character (`<` → XML, `{`/`[` →
JSON5 parser). `--format` overrides detection. `--format json` is strict and rejects comments.

**New files.** `cfgedit <path>` for a path that does not exist asks for confirmation (Create is the default), then opens
an unsaved document. The file is written on the first Ctrl+S, and the folder must already exist. The format comes from
the extension or `--format`. If neither identifies one, a choice dialog asks. Initial content: `{}` for JSON, JSONC and
JSON5. For XML it is `<?xml version="1.0" encoding="utf-8"?>` plus an empty root element whose name is asked for. New
files are UTF-8 without a BOM, with the platform's newline. The recent-files picker also has a **New file** action, which
asks for a path.

**Files that fail to parse open in text mode.** The text mode is a plain multi-line editor (Terminal.Gui TextView) on the
source. It puts the cursor at the error position and shows `line:col: message` in the detail line. F5 (and Ctrl+S, before
writing) reparses. When the text parses, the tree view returns, and the text edits become one undo step. Saving text
that does not parse needs a Confirm (Save is the default). Ctrl+T switches between tree and text at any time on a file
that parses, so the source can be viewed or hand-edited. The cursor maps to the source offset of the focused part and
back. On the command line, `--check` only prints `path:line:col: message` and exits 1, so the parse can be run from
scripts.

## Architecture (Cfgedit.Core)

```
bytes ──decode──▶ SourceText ──parse──▶ CST (format-specific, immutable, token spans + trivia)
                                          │ project
                                          ▼
                                     DocTree (format-neutral rows/parts, what the UI sees)
EditOp ──format.Plan(CST)──▶ TextEdit[] ──splice──▶ new SourceText ──reparse/verify──▶ new CST
```

- **SourceText** holds the decoded string plus the encoding (detected BOM; for XML, the declared `encoding`, with
  code-page encodings available through `CodePagesEncodingProvider`), the dominant newline and the final-newline flag.
  Saving re-encodes with the same encoding.
- **CST per format.** Each token keeps its exact source span. Whitespace and comments are trivia attached to tokens. The
  tree is immutable, and offsets refer to the current SourceText.
- **IConfigFormat** is the extension point that phase 2 formats implement: `Parse(SourceText) → CST`,
  `Project(CST) → DocTree`, and `Plan(CST, EditOp) → TextEdit[]` (or a validation error). It also provides the
  encode/decode and validation of each part kind.
- **Edits are text splices followed by a full reparse.** Config files are small, and reparsing even 1 MB takes
  milliseconds. Incremental tree updates are therefore unnecessary, and the reparse doubles as the verification step.
- **Undo/redo** keeps a stack of (text before, text after, cursor path) entries. It is unlimited and cleared on reload.
- **NodePath** identifies a node across reparses: the chain of (key or element name, ordinal among same-named siblings).
  For an array item it is the index. The UI keeps its cursor and the collapsed set as NodePaths, so both survive edits and
  undo.

### DocTree

A `DocNode` has a kind, children, a `Collapsible` flag and an ordered list of **parts**. A part has a kind, a display
string, a logical value, an `Editable` flag, a style and a source span. One row can have several editable parts.

| Format | Node | Parts on its row |
|---|---|---|
| JSON | member of object | key, value (scalar) or summary `{3}` / `[5]` (container) |
| JSON | array item | index (not editable), value or summary |
| JSON | comment | comment text |
| XML | element | start name, then name + value for each attribute, then text content if the element only contains text |
| XML | end tag row | end name (only when the element has element or mixed children) |
| XML | text / CDATA | text |
| XML | comment / PI | comment text / target + data |
| XML | declaration, DOCTYPE | shown read-only |

## JSON, JSONC and JSON5

- **Dialect flags** on one lexer: comments, trailing commas, single-quoted strings, unquoted identifier keys, JSON5
  numbers (hex, leading/trailing dot, `+`, `Infinity`, `NaN`), and escaped line continuations. JSON enables none of them,
  JSONC enables comments and trailing commas, and JSON5 enables all.
- **Value types.** A scalar's type is kept while its value is edited. A string edit takes any text, including newlines;
  a number edit is validated against the dialect's number grammar; a bool or null edit is a choice. Changing the type is a
  separate command (`t`: string / number / true / false / null / object / array). Changing to array wraps the value as
  the array's only item (`1` becomes `[1]`; a multi-line container goes on its own lines one unit deeper); a null becomes
  `[]`. Converting a container to anything else needs a Confirm when the container is not empty.
- **Strings** are encoded minimally for the dialect when edited: `\"` or `\'` matching the original quote character,
  `\\`, control characters as `\n \t \r \b \f` or `\uXXXX`. Everything else, non-ASCII included, is written literally.
  Unedited strings keep their original escapes.
- **Keys.** In JSON5, a key that was unquoted stays unquoted after a rename if the new name is a valid ES5 identifier.
  Otherwise the key is quoted with the file's dominant quote character. A key that duplicates a sibling is rejected.
- **Comments** are attached as leading trivia to the following member or item, or as trailing trivia when on the same
  line. They are shown as dim rows, can be edited and deleted, and move and delete together with their node.
- **Numbers** keep their spelling unless the user edits them. cfgedit never converts the spelling of a number.

## XML

### Parsing: XmlReader is the authority, the scanner finds spans

Probe findings (net10, recorded here because the design depends on them):

- **Positions.** `IXmlLineInfo` positions are 1-based line and UTF-16 column. A tab counts as 1 column and a surrogate pair
  as 2. CRLF, a lone CR and mixed endings each count as one line break.
- **Where each node is reported.**
  - Element, EndElement, Attribute, PI, XmlDeclaration and DocumentType: the start of the name.
  - Comment and CDATA: the first character of the content.
  - Text: its first character, or the `&` of a leading reference.
  - No end positions are reported.
- **Normalisation.** Values are normalised: CRLF and CR in text become LF, and tab/newline in attribute values become a
  space.
- **Errors.** `XmlException.LineNumber/LinePosition` point at the offending character.
- **String input.** Fed a decoded string through `StringReader`, XmlReader gives the same positions and values as for the
  byte stream. It ignores the `encoding` declaration, and it rejects a leading U+FEFF, so the BOM must be stripped.
- **Entities.** `XmlReader.Create` expands custom entities and merges them into the surrounding Text node. An entity whose
  replacement contains markup produces nodes positioned inside the DOCTYPE. An undeclared entity or namespace prefix is
  an error.

Pipeline:

1. **Decode.** cfgedit decodes the bytes itself, following XML 1.0 Appendix F: BOM, then a UTF-16 pattern without a BOM,
   then the `encoding` declaration (code pages through `CodePagesEncodingProvider`), then UTF-8. The BOM is stripped and
   remembered for saving. As a cross-check, a stream `XmlTextReader` reads the first node, and its `Encoding` must agree
   with cfgedit's detection.
2. **Validate.** `XmlReader.Create` runs over a `StringReader` of the decoded text with `DtdProcessing.Parse`,
   `XmlResolver = null` (never fetches anything), a `MaxCharactersFromEntities` cap and `CheckCharacters`. It checks
   well-formedness, namespaces and entities and decodes every value. Its errors carry the positions that text mode shows.
3. **Scan.** cfgedit's scanner tokenises the same text into the CST. It never decides validity, only where things are:
   - tag internals: name, whitespace, `=`, quoted value, `>` or `/>`
   - text up to the next `<`
   - comment, CDATA and PI ends
   - the end of the DOCTYPE, skipping quoted strings and the bracketed internal subset

   Inside values it decodes only the five predefined entities and character references, and applies the same
   normalisation as XmlReader.
4. **Cross-check.** Walk the reader and the CST in step. Every node's kind, name and start position, every attribute and
   its decoded value, and every text and CDATA value must match. Text is compared after merging adjacent text the way
   XmlReader does. A mismatch is a cfgedit bug: the file opens in text mode with an "internal parse mismatch" message,
   never as a possibly wrong tree. The edit check (reparse and verify) runs the same pipeline, so a successful edit is
   always accepted by .NET's parser.

Limitations accepted for phase 1:

- An entity whose replacement text contains `<` makes the reader report positions inside the DOCTYPE. Such files open in
  text mode only, with a message saying why.
- The internal DOCTYPE subset is not parsed by cfgedit. It is kept as raw text and shown read-only.
- There is no DTD or schema validation beyond what XmlReader does for well-formedness.

### Display and editing
- **Whitespace-only text** between elements counts as formatting. It is preserved but not shown, and it is the source of
  indentation when nodes are inserted. Non-whitespace text in mixed content gets its own rows.
- **Text-only elements** (`<add>value</add>`) show their text as an inline part on the element row, with no end row. An
  element with child elements gets an end row (`</name>`) once expanded. A collapsed element shows `<name …>…</name>` on
  one row.
- **Renaming.** Editing the start name or the end name renames both. Names are validated as XML QNames. Namespace
  prefixes stay as written, and `xmlns:*` attributes are ordinary attributes. XmlReader treats an undeclared prefix as
  an error, so a rename to one, or deleting an `xmlns:p` that is still in use, is rejected with that message.
- **Values.** The predefined entities and character references are decoded for display and editing. On an edited value,
  attribute values escape `&`, `<` and the quote character in use (and newline/tab as `&#10;`/`&#9;` so they survive
  attribute-value normalisation). Text escapes `&`, `<` and `>` after `]]`. A CDATA edit containing `]]>` is split into
  two CDATA sections.
- **Custom entity references** (`&foo;` declared in a DOCTYPE) are not decoded by the scanner. A part that contains one
  is shown in **raw mode**: the escaped source with a `raw` marker. It is edited as source text and checked by the
  reparse. For such parts the cross-check compares positions only, not values.
- **Attribute wrapping.** Attributes follow the element name on its row. An attribute that does not fit in the remaining
  width wraps to the next display line, aligned under the first attribute. If that alignment leaves less than 24 columns,
  the wrap indent is the element's indent + 4 instead. An attribute is never split between lines unless it is wider than
  a whole line on its own. In that case its value is truncated with `…`, and the full value appears in the detail line
  and in the editor.

## Screen

```
╭┤web.config├──────────────────────────────────────────────────────────────╮
│▾ <configuration>                                                         │
│    ▾ <appSettings>                                                       │
│        <add key="Environment" value="Production" />                      │
│        <add key="ConnectionTimeout" value="30" />                        │
│      </appSettings>                                                      │
│    ▸ <system.web targetFramework="4.8" debug="false" …>…</system.web>    │
│    ▾ <log4net debug="false" threshold="ALL"                              │
│               update="Merge">                                            │
│  </configuration>                                                        │
╰──────────────────────────────────────────────────────────────────────────╯
/configuration/appSettings/add[2]/@value   string   "30"
a/i Add  d Dup  t Type  / Find  y Copy  ^T Text  Tab Next  ^O Open  ^Z Undo  ^S Save  Del Delete  Enter Edit  ^Q Quit
```

JSON rows: `▾ servers: {4}`, `port: 8080`, `name: web`. **Strings are shown without quotes** and are told apart by colour.
An empty string shows as a dim `""`. Newlines and tabs inside values show as dim `⏎` / `→`. JSON has no closing-bracket
rows, because indentation already shows the nesting.

Colours separate keys/names, strings, numbers, bool/null, punctuation (dim) and comments (dim). The focused part is drawn
in reverse video, and the rest of its row is lightly highlighted. The detail line shows the path of the focused part
(XPath-like for XML, `$.a.b[2]` for JSON), its type, and its full value when truncated. The hints line comes from Shared.Tui.

## Keys (tree)

The cursor is a (row, part) pair. Printable shortcuts apply only in the tree, never inside an editor.

| Key | Behaviour |
|---|---|
| Up/Down, k/j | Previous / next row. The part index is kept where the new row has one, otherwise the nearest part. |
| Left/Right, h/l | Previous / next part on the row. Left on the first part goes to the parent row. |
| Home/End, 0/$ | First / last part of the row |
| PgUp/PgDn, g/G | Page / top / bottom |
| Space | Expand / collapse the row's node. On a leaf, toggles its parent. |
| `*` / `-` | Expand the whole subtree / collapse to the parent |
| Enter, F2 | Edit the focused part (inline). Values with newlines, or Ctrl+Enter, open the multi-line editor. |
| a / i | Add after / before the focused node. On a container's summary (`{n}`, `[n]`, and `{0}` on an empty XML element) add inside it: `a` last, `i` first. On the JSON root marker `$`, add a comment beside the root. On an attribute part this adds an attribute, otherwise a sibling node. |
| A / I | Add a child last / first, from any part of an XML element or a JSON object or array |
| Delete | Delete the focused node or attribute (undo restores it; no confirmation) |
| d | Duplicate the focused node: the copy goes right after it, with its attached comments, and the cursor moves to the copy. A copied key gets the next number (`port` gives `port2`, `server1` gives `server2`), then the next free one |
| Alt+Up/Down, K/J | Move the node among its siblings. Alt+Left/Right moves an attribute. |
| Alt+Shift+Up/Down | Nest: move the node into the container just above (as its last child) / below (as its first child); at the first / last position, move it out of its parent, before / after it; next to a non-container, an ordinary one-step move. Keys: a member keeps its key, numbered when taken (`port2`); an item entering a keyed container asks for a key; a member entering an array drops its key. The root's own entries do not move out (`already first` / `already last`). (`ConfigDocument.NestBy`, `MoveNodeTo`) |
| t | Change the JSON value type / convert an XML text-only element to element content |
| T | Open the table editor for the focused array/element (or its parent) |
| y / Y | Copy the focused value / path (OSC 52) |
| / or Ctrl+F, n/N, F3 | Search keys, names and values. Matches inside collapsed nodes expand them. |
| Ctrl+Z / Ctrl+Y (u / Ctrl+R) | Undo / redo |
| Ctrl+S | Save |
| Ctrl+Q | Quit. With unsaved changes, Confirm offers Save / Discard / Cancel, with Save as the default. |

**Inline editor.** A text field over the part, extending to the right edge. Enter commits, Esc cancels. Tab commits and
moves to the next editable part (this row first, then later rows), so a run of values can be filled in without leaving
edit mode. Invalid input (bad number, invalid name, duplicate key or attribute) shows its message under the field and
stays in edit mode.

**Add dialog.** It asks only what the context needs. In a JSON object: key, then type. In a JSON array: type. In XML:
element / attribute / text / comment, then the name. A new node copies the shape of the selected sibling when one exists
(`a` on an `<add key value/>` creates `<add key="" value=""/>`, with focus on the first value).

## Table editor

**When it is offered.**

- A JSON array whose items are all objects. The columns are the union of their keys, in first-seen order.
- A JSON array whose items are all scalars, shown as one value column.
- An XML element with at least two child elements of the same name. Only those children become rows. The columns are the
  union of their attributes, then text-only child elements that occur at most once per row, then a text column if they
  have text.

**Layout.** A full-pane view that replaces the tree, titled with the node path. Esc returns to the tree with the cursor
on the node of the last focused cell. The row-index column stays fixed while the other columns scroll horizontally.
Column widths are set from the content up to a cap, and long values are truncated with `…` and shown in full in the
detail line.

**Cells.**

- A scalar cell is edited like a tree part.
- A nested container shows `{…}` / `[…]` / `<…>` and cannot be edited in the table. Enter jumps to that node in the tree.
- A missing cell shows as a dim `·`. Editing it adds the key or attribute at the position matching the column order. A
  missing JSON cell takes the column's most common type.
- Delete on a cell removes the key or attribute. Delete on the index column deletes the row.
- `d` duplicates the row: the copy goes right after it, and the cursor moves to the copy in the same column.

**Rows.** `a`/`i` adds a row after/before, copying the current row's shape with empty values. Alt+Up/Down moves a row.
There is no sorting in phase 1, because sorting reorders the file.

Undo, save, search and copy work as in the tree.

## Saving and external changes

- Saving goes through `AtomicFile.Write`, which keeps symlinks, ownership, permissions and ACLs.
- Before saving, cfgedit compares FileIdentity and the content hash taken at load time. If the file changed on disk,
  Confirm offers Overwrite / Reload / Cancel. Reload drops the undo history after a second Confirm.
- In phase 1, cfgedit does not watch the file while it is open. A later phase adds watching: reload silently when there
  are no unsaved changes, and offer Reload / Keep mine when there are.

## Phase 2

Order: `.env` (M9), TOML (M10), YAML (M11). All three plug in through IConfigFormat. Nothing in the UI is specific to a
format except the part-kind styling, the path text and the add-dialog choices, which follow the node kinds.

Decisions (user, 2026-11-23): one `.env` dialect, dotenv, with `__` as a hierarchy separator (as ASP.NET Core's
environment configuration reads it); TOML and YAML edits are cross-checked by an independent parser (Tomlyn,
YamlDotNet), used only as verifiers, as .NET's XmlReader is for XML; YAML covers the config subset below, not all of
YAML 1.2.

### `.env` (dotenv)

**Detection.** A file named `.env`, `.env.<anything>` (`.env.local`, `.env.production`) or `<anything>.env`, or
`--format env`. A file whose extension is a known one (`.json`, `.json5`, an XML extension) keeps that format, so `.env.json`
is JSONC. A new file starts empty.

**Syntax.** Line based. Each line is one of:

- blank;
- a comment: optional whitespace, `#`, the rest of the line;
- an entry: optional whitespace, optional `export` and whitespace, a key, optional spaces, `=`, optional spaces, a value,
  optional whitespace and a trailing comment (`# …`) after an unquoted value only when preceded by whitespace, and after
  a quoted value.

Keys are `[A-Za-z_][A-Za-z0-9_.-]*`. Values are:

- unquoted: up to a ` #` comment or the end of the line, trailing whitespace not part of the value; may be empty;
- single-quoted: literal, no escapes, may span lines;
- double-quoted: may span lines; the escapes `\n`, `\r`, `\t`, `\"`, `\\` are decoded; any other backslash is kept
  as written.

Anything else (a `KEY: value` line, backtick quotes, an unterminated quote, an invalid key) is a parse error: the file
opens in text mode at that line. `${VAR}` references are shown and kept as written; cfgedit never expands them.
Duplicate keys are allowed and shown as they are (loaders disagree on which one wins).

**Writing a value** chooses the plainest form that every common loader reads the same way: unquoted when the value has
no leading or trailing whitespace and none of `#`, `'`, `"`, `\`, `$`, a backtick or a line break; otherwise single
quotes when it has no `'`, no CR, no `\\` pair and no trailing `\` (python-dotenv decodes `\\` and `\'` in single
quotes, Node's dotenv does not); otherwise double quotes, with `"` and `\` escaped, CR as `\r` and line breaks
written as real line breaks. Editing an existing value keeps its quote style when that style can still hold the new
value. Control characters other than tab, LF and CR are refused. A value written in double quotes that contains `$`
or `\` gets a warning on the detail line: loaders may expand `$`, and they disagree on backslash escapes there.

**Hierarchy.** A key is split on `__` into segments: `Logging__LogLevel__Default` is shown as `Logging` › `LogLevel` ›
`Default`. A key with an empty segment (`A____B`, a leading or trailing `__`) or a segment that starts or ends with `_`
(where the split would not round-trip), or more segments than the JSON nesting cap (256), is not split. Group nodes are virtual: a group appears where its first entry
is, and every entry with that prefix, adjacent or not, appears under it in file order. Grouping is case-sensitive, as
dotenv is.

**Tree.** The document has one root node, a group with the `$` marker and a `{n}` count like a JSON root, so an empty
file still has a row to add to. Groups show their segment and `{n}`; entries show their last segment and the value;
comments are rows. A comment is shown with the entry below it (inside that entry's group); a comment followed by a
blank line or the end of the file is a row of the root. A trailing comment (`A=1 # note`) is shown as a comment row right
after its entry. The path text on the detail line is the full key
(`Logging__LogLevel__Default`).

**Edits.**

- Value: rewrites the value (see "Writing a value").
- Key of an entry: renames its last segment. Key of a group: renames that segment in every key under the group, one
  text edit per line, as one undo step. The new segment must be a valid key piece and must not create or remove a
  split (no `__`, no leading or trailing `_`); a rename that would merge with an existing sibling is refused.
- Add (`a`/`i`/`A`/`I`): the dialog offers *entry* (asks the key, value empty) and *comment*. Inside a group the line
  written is `<group prefix>__<key>=`; the key typed may itself contain `__`, which creates nested groups. A new line
  goes after the last line of the sibling it follows (before the first line of the sibling it precedes), and copies
  that line's `export` prefix and spacing around `=`.
- Delete: an entry deletes its line and the comments shown with it; a group deletes all its lines.
- Move (Alt+Up/Down): swaps the lines of two adjacent siblings. A move is refused, with a message, when either
  sibling's lines are not contiguous in the file.
- Nest (Alt+Shift+Up/Down): an entry or group moving into or out of a group is a key rename (`A__x` into `A__B` becomes
  `A__B__x`; a group renames the prefix of every key under it) plus a line move to where an insert would go. Each line
  keeps its `export`, spacing and value text. Refused: non-contiguous group lines, a key collision, a lone comment whose
  entry below would not be in the destination.
- Duplicate: copies the entry's line below it with the last key segment numbered (`port` gives `port2`, `server1` gives `server2`; then the next free number), unique among the siblings.
- Change type: refused ("`.env` values are all strings").
- Table editor: not offered for `.env` in M9.

**Verification.** The logical tree for `.env` is flat and in file order: the document's children are one string scalar per
entry, named by its full key (`Logging__LogLevel__Default`), and one comment per comment line. A trailing comment is a comment
right after its entry's scalar. Blank lines, `export`, quote styles and spacing are formatting and are not in it. There is no independent parser to cross-check `.env`; the parser
gets property tests over randomised values through the encoder (write, parse, compare).

### TOML (M10)

**Version and detection.** TOML 1.0. `.toml`, or `--format toml`. A new file starts empty. A file that is not valid
TOML 1.0 (including TOML 1.1 additions such as line breaks inside inline tables) opens in text mode at the error.

**Syntax.** A lossless CST, as for JSON: every token keeps its span, whitespace and comments are trivia, printing the
CST reproduces the bytes. Keys (bare, basic-quoted, literal-quoted, dotted with optional spaces around the dots),
key/value pairs, `[table]` and `[[array of tables]]` headers, values: the four string forms (basic, literal,
multi-line basic, multi-line literal, including line-ending backslashes), integers (decimal with `_`, `+`/`-`, `0x`,
`0o`, `0b`), floats (fraction, exponent, `inf`, `nan`, signs), booleans, the four date-time forms, arrays (multi-line,
trailing comma, comments inside) and inline tables (one line). Parse errors carry line and column.

**Semantic tree.** The tree shows TOML's data, not its text. The root table has the `$` marker and `{n}`. A table can
be defined in pieces: its own `[a]` header, `[a.b]` headers below it, `a.b.c = 1` dotted keys, and an implicit
parent created by a deeper header. Each table node is the merged table; it remembers every *piece* of text that defines
it (a header section, a dotted-key line, an inline table). Children are in order of first appearance in the file.

- Table: key and `{n}`. Array (inline `[...]`): key and `[n]`. Array of tables (`[[x]]`): key and `[n]`; its items
  are tables, shown with their index and `{n}`. Inline tables are tables. Scalar: `key = value`, with the value shown
  as JSON scalars are (strings without quotes, other kinds as written) and its kind on the detail line: `string`,
  `integer`, `float`, `boolean`, `offset date-time`, `local date-time`, `local date`, `local time`.
- Comments: a comment line is shown with the key/value or header below it when only comment lines lie between them
  (like `.env`); otherwise as a row at its place in the table whose section it is in. A trailing comment is a row right
  after its key/value or header. Comments inside an inline array are rows of the array.
  Decisions: a comment above a key/value is the row before that key/value, in the table that holds it (for `a.b = 1`,
  inside `a`). A comment above a header belongs to the table that header defines (or to the new item of `[[x]]`), as its
  first child; the header's own trailing comment is its next child. A comment between a value and its comma stays in the
  array, after the item.
- Path text: TOML dotted keys, quoted when not bare, with `[n]` for array items: `servers[1].port`,
  `tool."my.key"`. The root is `$`.

**Edits.**

- Scalar value: validated for its kind (TOML grammar) and written as typed for numbers and date-times. A string keeps
  its form when the form can hold the new value (literal: no `'`, no control characters; multi-line literal: no `'''`;
  multi-line forms for values with line breaks); otherwise a new string is written basic (`"…"`, escaped), or literal
  when it contains `\` or `"` and no `'` or control characters; a value with a line break is written multi-line basic.
- Key rename: written bare when it can be, else basic-quoted. Renaming a table renames that key in every piece that
  names it: its headers, deeper headers (`[a.b.c]`), `[[a.b]]` headers and dotted keys. A rename onto an existing
  sibling key is refused.
- Add into a table defined by a header (or the root): `key = value` after the last key/value of its first section (for
  the root, before the first header). Into a table defined only by dotted keys: a dotted key after its last dotted
  line. Into an implicit table (only deeper headers): a new `[a]` header with the key, placed before its first deeper
  header. Into an inline table: `, key = value` before the `}`. A new *table* in a header-defined table or the root is
  written as a `[parent.key]` header section placed after the last section of the parent's subtree; elsewhere as
  `key = {}`. A new array is `key = []`. The add dialog kinds: string, integer, float, boolean, date-time (asks the
  text), table, array, comment.
- Arrays: inline arrays get items as JSON arrays do (commas, same-line or one-per-line style copied, trailing comma
  kept). An array of tables gets a new item as a `[[x]]` section after the item it follows (or before the one it
  precedes), shaped like the neighbour item (its keys with empty values), as the table editor's add row does.
- Delete: a key/value takes its attached and trailing comments. A table deletes every piece that defines it,
  including its subtables. An array-of-tables item deletes its section and the sections of its subtables.
- Move (Alt+Up/Down): key/values within one section, items of an inline array, items of an array of tables, and
  sibling header sections, when the units are contiguous; otherwise refused with a message (as `.env`).
- Duplicate: as JSON (the copy's key is numbered, `port` gives `port2`); an array-of-tables item is duplicated as a section.
- Change type: scalars change kind through the type menu (string, integer, float, boolean, date-time, table, array),
  as JSON. A scalar becomes `{}`; changing to array wraps it (`a = 1` becomes `a = [1]`, the value text verbatim). A
  header-defined table or an array of tables cannot change type: delete it instead (message).
- Nest (Alt+Shift+Up/Down): key/values into or out of header tables, the root, inline tables, inline arrays and
  array-of-tables items; a key/value goes after the destination's key/values when the index would put it after a subtable
  header. A header table (or an array of tables, or one item) moves as one run of sections with every header renamed to
  the new prefix, placed where a new subtable would go. Refused: dotted or implicit tables (moving or as destination), a
  header table into an inline table, an array or an array-of-tables item, an entry with comments into an inline container,
  and comment lines.
- Table editor: an array of tables, and an inline array of inline tables, open as a table (rows are items).

**Verification.** Two checks for every edit, after cfgedit's own reparse: (1) a TOML logical tree, data (tables as
objects, arrays, scalars with kind and canonical value) plus the comments in file order, is compared with the edit
applied to the logical tree before, by a TOML logical edit (as for `.env`); (2) Tomlyn parses the new text, and its
data, converted to the same logical shape, must equal cfgedit's. Opening a file runs check (2) too: if Tomlyn and
cfgedit disagree, the file opens in text mode with a message. Two constructs that Tomlyn misreads are refused with a message and
open in text mode: a `[x.sub]` header or a new `[[x]]` item that follows another table after `[[x]]`. cfgedit's parser is tested against the toml-test suite
(github.com/toml-lang/toml-test, MIT): every valid TOML 1.0 case must parse and match Tomlyn, every invalid case must
be refused.

The logical tree that check (1) compares is the data (objects for tables, arrays for arrays and arrays of tables, and
scalars with their kind and canonical value) under the document, followed by one array named `#comments` that holds every
comment in file order. Check (2) compares the data alone: tables by name, since TOML tables have no order, and arrays in
order. Date-times are compared as written, with `T` for the separator and `Z` in upper case; a float is compared by its
shortest round-trip form.

A Tomlyn error on text that cfgedit accepts, and any difference, is an internal parse mismatch, as for XML. A syntax or
semantic error of an edited text is the user's (`IsUserError`). Tomlyn's nesting limit is set above cfgedit's cap: cfgedit
caps tables and values at 256 levels each, and Tomlyn counts both in one number.

**Dependency.** `Tomlyn` 2.11.1, referenced by `Cfgedit.Core` only and used only for verification.

### YAML (M11)

**Detection.** `.yaml`, `.yml`, or `--format yaml`. A new file starts empty and is shown as an empty mapping root, so
the first add writes `key: value`.

**Subset.** YAML 1.2, UTF-8 (a BOM is kept). Supported: block mappings and sequences (including sequences not
indented under their key), flow mappings and sequences (one line or several), the five scalar styles (plain, single
quoted, double quoted, literal `|`, folded `>`, with chomping and indentation indicators), comments, `---` and `...`
markers, several documents, `%YAML`/`%TAG` directives, anchors, aliases, tags and merge keys (`<<: *base`). Outside the
subset, the file opens in text mode with a message naming what is not supported: complex keys (`? `), keys that are
collections, aliases or block scalars, keys spanning lines, and anything YAML itself rejects. The safety property:
for any input, cfgedit either refuses it or reads exactly the data YAML defines; it never misreads.

**Syntax.** A lossless CST, as for the other formats: every token keeps its span, indentation, comments and line
breaks are trivia, printing it reproduces the bytes. Parse errors carry line and column.

**Tree.** The root is the stream. A file with one document shows that document's root as the root row (`$` marker,
like JSON). A file with several documents shows them as rows `---[0]`, `---[1]`… under the root. Mappings are
`key: {n}`, sequences `key: [n]`, items `[i]`, scalars `key: value` (values shown decoded, without quotes, as JSON's).
Anchors (`&name`), tags (`!tag`) and aliases (`*name`) are read-only parts on the row; an alias shows `*name` as its
value and is not expanded; a merge key is an ordinary member whose value is an alias or a sequence of aliases.
Comments attach as for `.env` and TOML: a comment line above an entry (no blank line between) is shown with it,
trailing comments are rows after their entry, others are rows in place. Path text: `servers[1].port`, keys quoted
when not plain-safe, documents as `---[1]`. The detail line shows a scalar's kind: plain scalars by the YAML 1.2 core
schema (`null`, `boolean`, `integer`, `float`, `string`), quoted and block scalars as `string`.

**Edits.**

- Scalar value: the style is kept when it can hold the value and, for plain scalars, when the new text resolves to
  the same core-schema kind (so editing a string to `true` writes `'true'`), and an empty quoted string (`''`, as an
add writes it) does not keep its quotes; otherwise the plainest safe style:
  plain, else single quoted, else double quoted; a value with line breaks becomes a literal block scalar (`|`, with
  `|-` or `|+` chosen to keep the trailing line breaks exact). A folded scalar is kept folded only if the new value
  folds back to itself; otherwise it becomes literal.
- Key rename: written plain when safe, else quoted; refused onto an existing sibling key.
- Add: into a block mapping, `key: value` at the siblings' indentation, after the group of the entry it follows; into
  a block sequence, `- value`; into flow collections, as JSON (commas, one-line or one-per-line style). New
  containers are written `{}` / `[]`; adding the first item to an empty `{}` or `[]` that is the value of a block
  mapping entry or block sequence item turns it into block style. An empty `{}` that is a sequence item becomes a compact block mapping on the dash line (`- key: value`). Add dialog kinds: string, number, boolean, null,
  mapping, sequence, comment.
- Delete, move (Alt+Up/Down), duplicate (numbered keys, `port` gives `port2`): as JSON and `.env`, with comment groups; moves refused when
  units are not contiguous. A deleted or moved node keeps its anchor and tag; deleting an anchored node that an alias
  still refers to is refused ("*name refers to it").
- Anchors, aliases, tags: not editable; an alias node can be deleted or moved.
- Change type: string, number, boolean, null, mapping, sequence, as JSON; a container becomes `{}`. Changing to sequence
  wraps the value: a block sequence with one item in block context (a block collection re-indented as a compact `- a: 1`
  item), `[v]` in flow context; anchors and tags stay on the item; a null becomes `[]`; an alias can be wrapped but not
  changed otherwise. Refused: a block scalar with an indentation indicator, a comment between key and value.
- Nest (Alt+Shift+Up/Down): within one document. Member ↔ item conversion as the wrap does (`k: v` becomes `- v`; a
  compact item becomes `key:` with its collection below). Refused: a block value or multi-line value into a flow
  collection, a plain scalar or key that is not valid plain in flow context, a `<<` member into a sequence, a block scalar
  with an indentation indicator, and any move after which an alias would precede its anchor or resolve to a different one.
- Several documents: documents can be deleted; adding a document is not supported in M11.
- Table editor: a sequence of mappings or of scalars opens as a table, as JSON.

**Verification.** After cfgedit's own reparse: (1) a YAML logical tree, data (mappings with scalar keys, sequences,
scalars as text plus plain-or-not, anchors, aliases and tags by name) plus comments in file order, is compared with
the edit applied to the logical tree before, by a YAML logical edit; (2) YamlDotNet parses the new text
(RepresentationModel), converted to the same shape, and must match. Opening a file runs check (2) too; a disagreement
opens it in text mode with a message. cfgedit's parser is tested against the yaml-test-suite
(github.com/yaml/yaml-test-suite, MIT): for every case, cfgedit either refuses it or produces the expected data;
every case marked as an error is refused. Valid inputs that YamlDotNet misreads are refused with a message ("cfgedit
cannot verify …; YamlDotNet reads it differently"): a tab-only line between block entries, block scalars whose
content ends in spaces or blank lines without a final line break, and any block scalar without `-` chomping that ends
the file with no final line break (the message says to add one). A core tag on a scalar that does not read as that
type (`!!int foo`) is refused. A stream whose only content is a bare `...` has no document.

**Dependency.** `YamlDotNet` 18.1.0, referenced by `Cfgedit.Core` only and used only for verification.

## Engineering rules (from cmdr's review rounds)

These rules come from `review/COMPLETED-FIXES.md` and the practices in `review/ROUND3-REPORT.md`. They apply from the
first commit, not after a review.

**Error boundary**

- Every expected failure surfaces as a `CfgeditException` with the path and a message. That includes invalid paths,
  missing vs inaccessible files, unreadable or undecodable bytes, parse errors and save failures. Callers never see a raw
  `IOException`, `UnauthorizedAccessException` or `ArgumentException` for a user's input.
- Parse errors give whole-file `line:col`. The column counts UTF-16 units, as XmlReader does, and the TUI converts it to
  a display column.

**Untouched data**

- Bytes that the user did not edit are never changed. That rules out trimming, newline normalisation and re-escaping.
- A value that an editor field cannot represent exactly is read-only in that field, with a hint. For example, the
  single-line inline editor shows a value with line breaks read-only and offers the multi-line editor instead. Values
  are never silently altered.
- A NUL character in a value can be shown, edited and saved where the format allows it. Inputs that a format can't
  represent are rejected with a message, never dropped.

**Limits**

- Parsers are iterative or have a nesting cap (`MaxDepth` = 256). Deeper input is a parse error, never a stack overflow.
- Entity expansion is capped. Nothing is resolved externally.

**Text and display**

- All widths go through Shared.Core `DisplayWidth`, which works per grapheme cluster. Zero-width graphemes take no
  column but stay in copies. In cmdr's output pane a tab pads to the next 8 columns, as terminals do.
- The exception is the text editors (the Shared.Tui `TextArea`, a `Terminal.Gui.Editor.Editor`): the editor package
  measures and draws its own text, per grapheme cluster, with a tab 4 columns wide.
- Wrapping and truncation never split a surrogate pair or a grapheme.

**Saving**

- Saving goes only through `AtomicFile`. That keeps symlinks resolved strictly, mode, owner and group, the Windows ACL,
  and the ownership of files created under sudo. The guarantee is crash atomicity, not power-loss durability.
- After a save, cfgedit checks the postcondition: the bytes on disk are re-read and must equal what was written.

**Threading**

- cfgedit is single-threaded: all document state is UI-thread only. Any later background work (file watching, phase 2)
  posts to the UI thread and carries the document version it was started for. Stale results are dropped.

**TUI**

- Confirm defaults follow Confirm.Ask/Choose: the default button is the action the user started.
- Dialogs size themselves from the screen at every layout and scroll to the focused field.
- A transient status message invalidates the cached hints, so the hints come back when the message expires.
- Layout is checked in a real terminal with the pty driver, because Terminal.Gui reserves bottom rows in dialogs.

**Tests**

- Tests use independent oracles: exact expected output text, driver cells for layout, and an inode check for
  replace-versus-in-place saves. They never re-implement the code under test.
- Critical regressions are mutation-checked: remove the guard locally and confirm the test fails.
- There is a shared corpus of nasty inputs: CR/LF/lone CR, BOM, NUL, empty strings, surrogate pairs, ZWJ emoji, `]]>`,
  quotes of both kinds, `&` and `<`, very long lines, and deep nesting.
- Platform-specific tests are visible skips, never early returns. Race tests use gates, never sleeps.

## Testing

- **Round-trip corpus.** Real configs from the SheetKraft repo (web.config, App.config, log4net XML, appsettings.json,
  tsconfig.json, package.json) and the JSON5 spec examples must parse and re-serialise byte-identically. Tests also cover
  CRLF, BOM, UTF-16 and windows-1252 XML.
- **Encoder properties.** For every part kind and dialect, randomised strings (quotes, `]]>`, control characters,
  surrogates, `&`, `<`) must satisfy encode → splice → reparse → decode = input.
- **Edit operations.** Each operation is checked against the exact expected output text, including formatting inherited
  by inserted nodes and comments that move with their node.
- **Parse errors** are reported with exact line:col positions.
- **TUI.** RowLayout (rows → display lines with part column ranges, attribute wrapping) is a pure class with its own
  tests. Key handling is tested with the fake driver, as in Logvu.Tests. Layout is checked with the pty driver.

## Milestones

1. **M0 Scaffolding.** Projects, solution, `cfgedit --version`, publish.ps1.
2. **M1 JSON family.** Lexer/parser with dialect flags, CST, encoders, round-trip corpus, parse errors.
3. **M2 XML.** Decoding (Appendix F), XmlReader validation, span scanner, CST, cross-check, encoders, raw-mode parts, round-trip corpus.
4. **M3 Model and edits.** IConfigFormat, DocTree projection, EditOps (set part, insert, delete, move, change type),
   verification, undo/redo, NodePath.
5. **M4 Read-only TUI.** Picker, tree view, RowLayout with wrapping, colours, navigation, fold, detail line.
6. **M5 Editing TUI.** Inline/multi-line editor, Tab flow, add dialog, delete/move/type, save, quit flow, external-change
   check, new files.
7. **M5b Text mode.** Parse-failure fallback, Ctrl+T toggle, cursor mapping, `--check`.
8. **M6 Search and copy.**
9. **M7 Table editor.**
10. **M8 Docs.** `docs/cfgedit.md`, README section, acceptance pass on Windows (by the user).

A tracker (`docs/cfgedit-tracker.md`) is created at M0, in the same style as logvu's.
