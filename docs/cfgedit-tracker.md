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

- [x] SourceText: BOM detection and stripping (UTF-8 and UTF-16 LE/BE), decoding with the BOM, dominant newline and final
      newline kept, and re-encoding that is byte-identical. (72ffc19)
- [x] JSON lexer with dialect flags (JSON / JSONC / JSON5). Tokens tile the text exactly, so whitespace and comments
      keep their exact spans. (72ffc19)
- [x] Parser to an immutable CST: objects, arrays, members, scalars and comments, with a `MaxDepth` cap. Errors are
      `CfgeditException`s with whole-file line:col. (1720d7b)
- [x] Value decoding (string escapes for each dialect, JSON5 line continuations) and number grammar validation for each
      dialect. (72ffc19)
- [x] Encoders: minimal string escaping that keeps the original quote character, and JSON5 identifier-key detection.
      (72ffc19)
- [x] Synthetic fixtures (JSONC, JSON5 spec example, strict, CRLF, BOM, UTF-16), the nasty-input corpus, encode/parse
      property tests, and error-position tests. (1720d7b)
- [x] Real configs (SheetKraft) checked by a scratchpad probe: decode/encode round trip, parse, CST invariants. All 361
      `.json` files pass (20 need Jsonc). (probe, 2026-11-21)
- [x] Performance: 5 MB/s, now 22–27 MB/s median on object-heavy input and 190 MB/s on string-heavy input, with half
      the allocations (56f369b). The rest is per-node CST allocation. Next steps, if edit latency on large files needs
      them: lazy `StringValue`, folding `JsonKey` into `JsonMember`, exact-size entry arrays.

## M2. XML (Cfgedit.Core)

- [x] Decoding per Appendix F (BOM, UTF-16 without a BOM, the declaration, code pages), cross-checked against
      `XmlTextReader.Encoding`. (9fed479)
- [x] Validation with `XmlReader.Create` over a StringReader, using safe settings. Errors with line:col. (1a279f3)
- [x] Span scanner to the XML CST: declaration, DOCTYPE (raw), elements, attributes, text, CDATA, comments, PIs.
      (1a279f3)
- [x] Cross-check between the reader and the CST. A mismatch is an internal error, and the file opens in text mode.
      Files with markup-bearing entities are detected. (1a279f3)
- [x] Encoders for attribute values (by quote character), text, and CDATA split (9fed479). Raw-mode parts are in the
      CST (1a279f3).
- [x] Synthetic fixtures (web.config-, App.config-, log4net- and csproj-style, mixed content, DOCTYPE cases; generated
      CRLF, BOM, UTF-16, windows-1252) and parse property tests. (1a279f3)
- [x] Real configs (SheetKraft) checked by a scratchpad probe. All 485 files pass (xaml, xml, csproj, config, resx,
      svg, manifest, props, targets, nuspec, xsd; 223 with a BOM; CRLF, LF and mixed). (probe, 2026-11-21)
- [ ] Performance: a 1.5 MB file parses in 0.53 s (about 3 MB/s). Profile the whole pipeline (XmlReader pass, scanner,
      cross-check) before M5 (target: at least 20 MB/s).

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
