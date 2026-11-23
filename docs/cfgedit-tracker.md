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
- [x] Performance: allocations halved, scanner 2–3× faster, total 19–32 MB/s on quiet runs (c7cf9a9). The bare XmlReader
      pass and decoding now set most of the floor.
- [x] Element nesting capped at 256 like JSON, and scanner exceptions wrapped as the internal mismatch error. (c7cf9a9)

## M3. Model and edits (Cfgedit.Core)

- [x] IConfigFormat, DocTree projection and parts for JSON and XML. (edec9b4)
- [x] NodePath, which survives reparse. (edec9b4)
- [x] JSON EditOps: set part, insert (formatting inherited from siblings), delete (with comments), move, duplicate, change
      type. (7f29fcb)
- [x] XML EditOps, including rename (start and end together), mixed content, namespaces, and raw parts. (79e4e1e)
      Known limits: deleting an element that sits between two text nodes in mixed content is refused, because the texts
      would join; text nodes can't be moved or duplicated; document-level inserts are comments only (no `NewXmlPI`).
      For M5: `<a/>` has no Text part, so the UI must offer "add text" as `SetPart(Text)` on it, which the planner
      accepts.
- [x] Verification by reparse and logical comparison. Undo/redo with content-based dirty state. (7f29fcb)
- [x] Table detection and table model (JSON arrays, XML repeated elements), with CompositeEdit for multi-step row
      edits as one undo step. (1874db5)

## M4. Read-only TUI

- [x] Program and host: open a file, the recent-files picker (Open file dialog offers cfgedit's extensions), and the
      format choice. (8f35a55)
- [x] RowLayout (pure) with parts, styles, glyphs and XML attribute wrapping; DocRows; detail-line PathText. (4c8c075)
- [x] RowLayout work capped at the width, so a huge value costs O(width) per draw (mutation-checked by allocation).
      (8f35a55)
- [x] Tree view and colours. (8f35a55)
- [x] Navigation, fold, the detail line (path, type, full value) and hints. Checked in a real terminal with the pty
      driver at 120x30, 60x20 and 40x12. (8f35a55)
- [ ] A same-line trailing comment shows as its own row after the entry. Consider showing it on the entry's row in M5
      or later.

## M5. Editing TUI

- [x] Inline editor (Tab flow, validation messages) and multi-line editor; message line, dirty marker, undo/redo keys,
      save and quit/switch prompts. Checked in a real terminal at 80x24 and 40x12. (179696f) *(Windows)*: multi-line
      line breaks are normalised from `Environment.NewLine` to LF, which has a unit test but has not been run on Windows.
- [x] Add (sibling shape, dialog, attribute prompt), delete, moves, type menu (also Enter/F2 on true/false/null),
      duplicate. Checked in a real terminal at 40x12. (c1b23a8)
- [x] Save core: `AtomicFile.WriteExact`, DocumentFile (content-based external-change check, read-back postcondition),
      EditController save/reload/quit flows, MoveBy over comment groups. (202b5c3)
- [x] Save/quit UI wiring (179696f) and new files: Create confirmation, format choice, XML root name, recorded in recent
      files after the first save. (c1b23a8)
- [ ] The picker's New file action needs a RecentPicker extension. Deferred.
- [x] JSON planner: moving a node that shares a line with its neighbour keeps the line breaks where they were (only a
      line comment forces one). (33e1425)
- [ ] The Save postcondition mismatch branch has no test, because there is no seam to inject a mismatch.

## M5b. Text mode

- [x] Parse-failure fallback with the cursor at the error, F5 to reparse, and the Ctrl+T toggle with cursor mapping;
      saving unparseable text after a confirmation; read-only when line endings would not round-trip (mutation-checked).
      Checked in a real terminal on a CRLF file. (3722a07) *(Windows)*: newline mapping through `Environment.NewLine` is
      unit-tested but has not been run on Windows.
- [x] `--check`. (3722a07)

## M6. Search and copy

- [x] Search over keys, names and values, which expands collapsed nodes. Copy of the value and of the path (OSC 52).
      Checked in a real terminal. (2f801e9)
- [ ] Watch: one full Cfgedit.Tests run reportedly hung once during M6 work and did not reproduce in later runs.

## M7. Table editor

- [x] Table view: cells, editing, missing cells, row add/delete/move, and Esc back to the tree. Checked in a real
      terminal. (9935e45)
- [ ] Table view has no mouse support and no search; Tab does nothing there.

## M8. Docs

- [x] `docs/cfgedit.md` and a README section. (3ce2d37)
- [ ] The user's acceptance pass on Windows *(Windows)*: open, edit, save and text mode on real configs; multi-line values
      and text mode on CRLF files; saving keeps the ACL; Ctrl+C/terminal behaviour.

## M9. `.env` (design: "Phase 2" › "`.env` (dotenv)")

- [x] Core syntax: line lexer/parser into a lossless CST (entries, comments, blank lines, `export`, quote styles,
      multi-line quoted values, trailing comments), value decoding and the value encoder; parse errors with line and
      column. Tests: fixtures, round trip (parse then print is byte-identical), encoder property tests. (fc2ff57)
- [x] Core model: `EnvFormat` (`env`), detection by file name, `__` hierarchy with virtual groups and the root node,
      logical tree, edit planner (value, key and group rename, insert, delete, move, duplicate; change type refused).
      Tests: tree dumps and exact-output edit tests like the JSON ones. (f5aeb6b, 71e98fc, d026e9d)
- [ ] Moves are refused when a unit's lines are not contiguous (including a blank line inside a group) and when moving
      a file's last line up over a blank line in a file without a final newline. Revisit if users hit these.
- [x] TUI: rows, path text (the full key), add dialog kinds, styling, new `.env` file, `--format env`; user guide.
      Checked in a real terminal at 80x24: group rename across non-adjacent lines, add inside a group, save. (08de30b)
      *(Windows)*: not run on Windows.

## M10. TOML (design: "Phase 2" › "TOML (M10)")

- [x] Core syntax: lexer/parser into a lossless CST (keys, headers, all value kinds, arrays, inline tables, comments),
      string/key encoders, number and date-time validation. Tests: byte-identical round trip, toml-test valid/invalid
      cases (vendored), encoder property tests. (6415f0a)
- [x] Core model: `TomlFormat` (`toml`), semantic tree (tables merged from pieces, arrays of tables), logical tree,
      Tomlyn cross-check on open and after every edit. (d41de24) Fractional seconds are capped at 7 digits because
      Tomlyn refuses more.
- [x] Core planner: value, key and table rename, add (all placements), arrays and arrays of tables, delete, move,
      duplicate, change type; TOML logical edit. (2a9dd4f, 8c6a9aa, 974d577)
- [x] Two valid constructs that Tomlyn misreads are refused with a message: `[x.sub]` or a new `[[x]]` item after
      another table follows `[[x]]`. (25622cf)
- [ ] Cleanup: the TOML sibling-unit and attachment rules are restated in `TomlLayout` (LogicalEdit/merger keep the
      other copies); unit building is O(n²) for very large inline arrays.
- [x] TUI: rows, path text, add dialog kinds, type menu, new `.toml` file, `--format toml`; user guide. Checked in a
      real terminal at 80x24: table rename across `[server]` and `[server.limits]`, save. (972ea1a) *(Windows)*: not
      run on Windows.
- [x] Table editor for arrays of tables and inline arrays of inline tables or values. Checked in a real terminal:
      cell edit and a shaped row added to `[[backends]]`. (4e701b7)

## M11. YAML (design: "Phase 2" › "YAML (M11)")

- [x] Core syntax: lossless CST and parser for the subset (block and flow collections, five scalar styles, comments,
      documents, directives, anchors, aliases, tags), refusals naming what is unsupported, scalar encoders. Tests:
      byte-identical round trip, yaml-test-suite (vendored): every case refused or read as expected, every error case
      refused. (e674ae1) 402 cases: 94 errors refused; of 308 others, 233 accepted (values match for the 213
      single-document ones), 75 refused with named reasons. Folded scalars are rewritten as literal when edited.
- [x] Core model: `YamlFormat` (`yaml`), tree, logical tree, YamlDotNet cross-check on open and after every edit.
      (9de7ebd)
- [x] Core planner: values (style kept, kind-safe plain), keys, add (block and flow, empty flow to block), delete,
      move, duplicate, change type; anchors/aliases rules; YAML logical edit. (3e895ac, 3051fba, 2c31467)
- [ ] MoveBy can offer a move the planner then refuses (first key of a `- key:` item, a trailing comment); the TUI
      shows the refusal. Consider having MoveBy skip those.
- [x] TUI: rows (anchors, tags, aliases), path text, add dialog kinds, type menu, new file, `--format yaml`; user
      guide. Checked in a real terminal at 80x24: a shaped compact item added to a compose `services` list next to an
      anchor and a merge key, save. (70ce4f7) *(Windows)*: not run on Windows.
- [x] Table editor for sequences of mappings or scalars. Checked in a real terminal on a compose `services` list: the
      `<<` merge-key column refuses new cells. (d1fbc64)
- [ ] A refused missing cell (alias or merge-key column) is refused when the field is committed, not when Enter opens
      it; refuse before opening the field.
