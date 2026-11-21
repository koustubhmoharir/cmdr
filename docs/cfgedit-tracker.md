# cfgedit implementation tracker

This file tracks the remaining work for [cfgedit-design.md](cfgedit-design.md). Work goes in milestone order, and each
milestone ends with at least one commit prefixed `cfgedit:`, or `core:` for shared code.

**When an item is done, tick it and name the commit.**

Items marked *(Windows)* can only be verified by the user on Windows. Every item follows the design's "Engineering rules".

## M0. Scaffolding

- [x] `src/Cfgedit.Core`, `src/Cfgedit` (`cfgedit` executable), `tests/Cfgedit.Core.Tests`, `tests/Cfgedit.Tests`, all
      in `Cmdr.slnx`, with the Terminal.Gui fork reference set up like logvu's.
- [x] `cfgedit --version`, plus usage for bad arguments, with Program tests like logvu's.
- [x] `publish.ps1` publishes and installs `cfgedit`. (575ff85)

## M1. JSON family (Cfgedit.Core)

- [ ] SourceText: BOM detection and stripping (UTF-8 and UTF-16 LE/BE), decoding with the BOM, dominant newline and final
      newline kept, and re-encoding that is byte-identical.
- [ ] JSON lexer with dialect flags (JSON / JSONC / JSON5). Tokens keep exact spans, and whitespace and comments are kept
      as trivia.
- [ ] Parser to an immutable CST: objects, arrays, members, scalars and comments, with a `MaxDepth` cap. Errors are
      `CfgeditException`s with whole-file line:col.
- [ ] Value decoding (string escapes for each dialect, JSON5 line continuations) and number grammar validation for each
      dialect.
- [ ] Encoders: minimal string escaping that keeps the original quote character, and JSON5 identifier-key detection.
- [ ] Round-trip corpus (real configs from SheetKraft, the JSON5 spec examples, the nasty-input corpus): parse, then print
      the CST, must give identical text. Error-position tests.

## M2. XML (Cfgedit.Core)

- [ ] Decoding per Appendix F (BOM, UTF-16 without a BOM, the declaration, code pages), cross-checked against
      `XmlTextReader.Encoding`.
- [ ] Validation with `XmlReader.Create` over a StringReader, using safe settings. Errors with line:col.
- [ ] Span scanner to the XML CST: declaration, DOCTYPE (raw), elements, attributes, text, CDATA, comments, PIs.
- [ ] Cross-check between the reader and the CST. A mismatch is an internal error, and the file opens in text mode.
      Files with markup-bearing entities are detected.
- [ ] Encoders for attribute values (by quote character), text, and CDATA split. Raw-mode parts.
- [ ] Round-trip corpus (SheetKraft web.config, App.config, log4net XML, .csproj; CRLF, BOM, UTF-16, windows-1252).

## M3. Model and edits (Cfgedit.Core)

- [ ] IConfigFormat, DocTree projection and parts for JSON and XML.
- [ ] NodePath, which survives reparse.
- [ ] EditOps: set part, insert (formatting inherited from siblings), delete (with comments), move, change type, and XML
      rename (start and end together).
- [ ] Verification by reparse and readback. Undo/redo.
- [ ] Table detection and table model (JSON arrays, XML repeated elements).

## M4. Read-only TUI

- [ ] Program and host: open a file, the recent-files picker, and the format choice.
- [ ] Tree view, plus RowLayout (pure) with parts, colours and XML attribute wrapping.
- [ ] Navigation, fold, the detail line (path, type, full value) and hints.

## M5. Editing TUI

- [ ] Inline editor (Tab flow, validation messages) and multi-line editor.
- [ ] Add dialog; delete, move and change type; undo/redo keys.
- [ ] Save (AtomicFile, external-change check, postcondition), quit flow and new files.

## M5b. Text mode

- [ ] Parse-failure fallback with the cursor at the error, F5 to reparse, and the Ctrl+T toggle with cursor mapping.
- [ ] `--check`.

## M6. Search and copy

- [ ] Search over keys, names and values, which expands collapsed nodes. Copy of the value and of the path (OSC 52).

## M7. Table editor

- [ ] Table view: cells, editing, missing cells, row add/delete/move, and Esc back to the tree.

## M8. Docs

- [ ] `docs/cfgedit.md`, a README section, and the user's acceptance pass on Windows *(Windows)*.
