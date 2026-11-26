# mdedit design (proposal)

mdedit is a .NET 10 terminal viewer and editor for Markdown files on Windows and Linux. It is built next to cmdr, logvu and
cfgedit, uses the repository's Terminal.Gui fork, and follows cfgedit's patterns: the recent-files picker, Ctrl+O, exact
saving, the external-change check, the unsaved-changes flow, the hints line and the message line.

A file opens **rendered** (view mode). Ctrl+T switches to the **source** (edit mode), a text editor with Markdown syntax
colouring, and back, keeping the cursor on the same character. Saving writes the source exactly as edited.

Decisions (user, 2026-11-26): the shared file layer moves to Shared.Core; Ctrl+T toggling is enough for now, but the
cursor position is kept across the switch; editing directly in the rendered view is to be investigated (see Rendered-view
editing); search is in the first version, links and the outline come later; every feature has full mouse support.

## Guiding rule: the source is the document

Markdown files are reviewed in diffs, so mdedit never rewrites a file it did not need to touch.

- Opening a file and saving it without edits writes the same bytes: encoding, BOM, line endings (mixed ones too), the
  final newline, trailing spaces (hard line breaks) and tabs.
- mdedit never reformats Markdown. Edits are the user's typing in the source; the rendered view is derived from it and is
  never written back.
- Saving goes through `AtomicFile.WriteExact`, and the bytes on disk are read back and must equal what was written.

## What is reused

Building blocks already in the repository, used unchanged:

| From | Used for |
|---|---|
| Markdig 1.4.2 (the version the fork uses) | Parsing, with block and inline source spans |
| Terminal.Gui fork `TextMateSyntaxHighlighter` (public `ISyntaxHighlighter`) | Colouring fenced code blocks in view mode |
| `Terminal.Gui.Editor` (via Shared.Tui `TextArea`) | Edit mode. Its built-in `MarkDown-Mode.xshd` highlighting definition colours the source; `WordWrap` is available |
| Shared.Tui | `RecentPicker` (with the Open file dialog and its key hints), `Confirm`, `HintsLine`/`KeyHints`, `SearchBar`, `Osc52`, `DisplayWidthSetup`, `BarColors` |
| Shared.Core | `AtomicFile.WriteExact`, `FileIdentity`, `RecentFiles` (store `mdedit/recent.json`), `SearchPattern`/`SearchHistory` |

### Sharing the file layer with cfgedit

cfgedit's `SourceText` (strict decoding with BOM, exact byte round trip, line positions) and the disk half of `DocumentFile`
(snapshot, `PollDisk`, `ChangedOnDisk`, `AcceptDiskState`, `MarkMissing`, exact save with postcondition, new files) are
exactly what mdedit needs, but they live in Cfgedit.Core and are tied to `IConfigFormat` and `CfgeditException`.

Proposal: move them to Shared.Core, format-neutral.

- `Shared.Core.Text.SourceText` (moved as is). Its decoding errors become a new `Shared.Core.TextFileException` (path, line,
  column), and `CfgeditException` wraps it where cfgedit catches it, so cfgedit's messages stay the same.
- `Shared.Core.Files.TextFile`: the path, `Exists`, `Text` (SourceText), the snapshot and stat, `PollDisk`, `ChangedOnDisk`,
  `AcceptDiskState`, `MarkMissing`, `Save(SourceText)`, `Reload()`, `CreateNew(path, initialText)`. No parsing.
- cfgedit's `DocumentFile` keeps its API and delegates the disk work to a `TextFile`, adding the parse. All existing
  cfgedit tests must stay green with no change other than namespaces; this step is done and reviewed on its own before
  any mdedit code.

The alternative, mdedit referencing Cfgedit.Core, would drag the config formats, Tomlyn and YamlDotNet into mdedit and
couple the two apps; duplicating the code would split a hardened piece in two.

## Projects

| Project | Contents |
|---|---|
| `src/Mdedit.Core` | No Terminal.Gui. The Markdig parse, the render model (blocks of styled runs whose graphemes carry source offsets), layout to a width, the source map, the heading outline, search in rendered text. |
| `src/Mdedit` | The `mdedit` executable: Program, host, main window, the rendered view, the source editor, dialogs. |
| `tests/Mdedit.Core.Tests`, `tests/Mdedit.Tests` | Render model, layout and source-map tests with exact expected output; key and mouse handling with the fake driver; host and program tests. |

Changes elsewhere: add
the projects to `Cmdr.slnx`, add `mdedit` to `publish.ps1`, link `docs/mdedit.md` from the README.

## Command line

```
mdedit <file>
mdedit                     # recent-files picker with Ctrl+O Open file
mdedit --version
```

Any file opens; the picker's file dialog lists `.md .markdown .mdown .mkd` first, then any file. A path that does not
exist asks "Create <name>?" (Create is the default) and opens an empty unsaved document in edit mode; the file is written
on the first save, and the folder must exist. New files are UTF-8 without a BOM, with the platform's newline. A file that
cannot be read or decoded is reported on stderr with exit code 1 (from the picker: the message in the picker). Usage
errors exit 2.

## Screen

```
╭┤README.md├──────────────────────────────────────────────────────────╮
│ # cmdr                                                              │
│                                                                     │
│ A cross-platform TUI for managing a set of non-interactive process  │
│ trees.                                                              │
│                                                                     │
│ ## Install                                                          │
│  • .NET 10 SDK                                                      │
│  • the Terminal.Gui submodule                                       │
╰─────────────────────────────────────────────────────────────────────╯
## Install                                                     12%
^T Edit  / Find  o Outline  Tab Link  y Copy  ^O Open  ^Q Quit
```

- **Title:** the file name; `*` when unsaved; `[edit]` in edit mode.
- **Message line:** in view mode, the heading the top of the view is under and the scroll position; in edit mode,
  `line:col`. Messages ("Saved", errors) appear there as in cfgedit.
- **Hints line:** from Shared.Tui, per mode.

## Keys

### View mode

| Key | |
|---|---|
| PgUp/PgDn, g/G, Ctrl+Home/End | Page, first / last line |
| Tab / Shift+Tab | Next / previous link; Enter follows it |
| Enter on a link | `#anchor`: scroll to the heading (the view does this). A relative path to a file: open it in mdedit, with the unsaved-changes flow; Backspace / Alt+Left returns to the previous file and its scroll position. Anything else (http, mailto): copy the URL to the clipboard and say so; mdedit opens nothing outside itself |
| `o` | Outline: a list of the headings, indented by level; Enter scrolls to the heading, typing filters |
| `/`, Ctrl+F; n/N, F3/Shift+F3 | Find in the rendered text (SearchBar); the match is selected and scrolled into view |
| `y`, Ctrl+C / `Y` | Copy the selection as plain text / as Markdown (see Copy); with no selection, the block under the cursor |
| Arrows, Home/End (with Shift to select) | Move the cursor in the rendered text (see Cursor). In a table, Left/Right at a cell's edge go to the next cell |
| Space | Fold or unfold the section or code block under the cursor (see Folding) |
| `*` / `-` | Unfold everything under the cursor's section / fold the section the cursor is in |
| `1`–`6`, `0` | Show headings down to that level, folding the deeper sections / unfold all |
| `<` / `>` in a table | Scroll the table horizontally (the cursor's cell stays visible). Shift+arrows select, everywhere |
| Enter, F2 in a table cell | Edit the cell (M5) |
| Ctrl+Z / Ctrl+Y | Undo / redo: the same history as edit mode (see Source buffer and undo) |
| Ctrl+T | Edit mode, with the caret on the source character under the view's cursor |
| Ctrl+S, Ctrl+O, Ctrl+Q | Save, open another file, quit (unsaved-changes flow) |

### Edit mode

The editor keys of cfgedit's text mode (Shared.Tui `TextArea`, Tab inserts a tab, Enter and paste insert the file's
dominant line ending), plus:

| Key | |
|---|---|
| Ctrl+T, Esc | View mode, with the cursor on the rendered character of the caret's source character (or the nearest one, when the caret is on markup that is not shown). The view re-renders from the current text |
| Ctrl+Z / Ctrl+Y | Undo / redo; one history for both modes, kept while the document is open |
| Alt+Z | Word wrap on / off (on by default) |
| Alt+F | Fold / unfold the section or code block at the caret; the fold gutter's `▾`/`▸` toggle on a click |
| Ctrl+F; F3/Shift+F3 | Find in the source (SearchBar); the search text carries over between the modes |
| Ctrl+S, Ctrl+O, Ctrl+Q | As in view mode |

### Cursor

View mode has a cursor: a caret before a character of the rendered text (or after the last character of a block), drawn
as the terminal cursor, moved with the arrow keys and by a
click. Ctrl+T maps it to the source and back through the source map (see Renderer): view → edit puts the caret on the
cell's source offset; edit → view puts the cursor on the cell of the caret's offset, or of the nearest mapped offset at or
after it when the caret is on markup that is not shown (`**`, `# `, an escape's backslash, the inside of an entity).

### Mouse

Every feature works with the mouse, as in the other applications:

- View: the wheel scrolls; Shift+wheel or the horizontal wheel over a table scrolls the table; a click places the cursor; a
  drag selects (and scrolls past the edges); a double-click selects a word; a click on a fold glyph (`▾`/`▸`) or on a fold
  summary toggles it; a click on a table's `◂`/`▸` edge marker scrolls it; a click on a link follows it (M4); a right-click
  opens a menu: Copy text, Copy Markdown, Fold/Unfold, and for tables the row and column operations (M5).
- Edit: the editor's own click, drag and wheel.
- The hints line is clickable (Shared.Tui `HintsLine`), the search bar takes clicks (`SearchBar`), and the picker, the
  dialogs, the Confirm buttons and the outline list (M4) take clicks and double-clicks.
- Fake-driver tests send mouse events for each of these, as cfgedit's tree tests do.

## Renderer

Decision (2026-11-26, after the investigation below): mdedit renders Markdown itself, on Markdig, rather than through the
fork's `Markdown` view.

**Why.** The fork's view keeps no source position anywhere: `InlineRun`, `IntermediateBlock` and `RenderedLine` are internal
and sealed, `StyledSegment` is public sealed with no offset, wrapping and drawing are internal, and no
`InternalsVisibleTo` reaches this repository. A cursor, the source map, search highlighting, clickable task boxes and
rendered-view editing would each be changes inside the fork's internals (an estimated 0.5–1k lines for the map and cursor
alone), carried against upstream. Its table path re-serialises cells from the AST, so their text is not the document's.

**Pipeline.** `Markdig.Markdown.Parse(text, pipeline)`, with the extensions mdedit renders: pipe tables, task lists,
strikethrough, autolinks, alert blocks (`> [!NOTE]`); everything else in CommonMark. An unsupported construct renders as
its source text, mapped 1:1.

**Render model (Mdedit.Core).** The AST is lowered to blocks (heading, paragraph, list item, quote, code, table, thematic
break, HTML) of runs. Each grapheme of a run carries its style role and either a source offset or *synthetic* (a bullet,
a quote bar, a heading marker, a table border). Offsets come from the inline spans (Markdig's `End` is inclusive) with
the corrections the investigation found:

| Construct | Display | Mapping |
|---|---|---|
| Literal text, emphasis content, heading text, code lines | as in source | 1:1 |
| Backslash escape `\*` | `*` | the `*`; the backslash is unmapped |
| Entity `&amp;amp;` | `&` | the first character of the entity; the rest is unmapped |
| Soft line break | a space | the line break; the next line's indentation is unmapped |
| Hard break (two spaces, or `\`) | a new display line | the line break |
| Code span, autolink | the content | 1:1 inside the delimiters |
| Pipe-table cell | trimmed content | 1:1, from the cell's document span |
| Inline HTML | shown dim, as written | 1:1 |

**Layout, per block.** Each block is laid out on its own: `BlockLayout Layout(block, width)` gives its display lines of
cells (text, style, offset or synthetic, link id), wrapped by `DisplayWidth` per grapheme, never splitting a grapheme.
Block layouts are cached by (block source text, width), so a resize lays out every block once and an edit re-lays only the
blocks it changed. The document layout is the sequence of visible block layouts (folded ones contribute their summary
line) with a running line index, so line → block and block → first line are a binary search. The source map answers
cell → offset and offset → cell (exact, or the next mapped cell). All of this is pure and tested with exact expected lines.
A 4,500-line file parses in about 20 ms, so the whole text is reparsed after each change; blocks whose text did not change
keep their cached layout.

**Reflow.** When the width changes, the view records the cursor's source offset and its screen row (its display line minus
the top line), lays out again, and scrolls so the cursor's new display line is on the same screen row (clamped at the top
and the end of the document). When the cursor is off screen, the first cell of the top line is the anchor instead. A
selection is kept as source offsets, so it survives a reflow too.

**Code blocks.** Fenced code is coloured by the fork's `TextMateSyntaxHighlighter` (TextMateSharp, already shipped with
every app through the fork): C#, Go, Python, TypeScript, JavaScript, HTML, CSS, XML and JSON, plus whatever else its
grammars cover; the info string picks the grammar (`cs`, `csharp`, `ts`, `py`, … through an alias table). Mdedit.Core
stays free of Terminal.Gui: it asks an `ICodeColorizer` for style roles per character of a code block, and the app adapts
the TextMate highlighter to it. Without the native Oniguruma library (the highlighter reports it), code is shown plain.
A code line longer than the block's width wraps at the column limit (never inside a grapheme, and keeping the colouring
across the break). Each continuation row starts with a dim `↪` wrap marker, a synthetic cell with no source offset, so the
cursor skips it and copies leave it out: a copied wrapped line is one line again. Code blocks never scroll horizontally.

## Tables

A pipe table is laid out as a grid with box-drawing borders, sized by its columns:

1. Each column's *natural* width is its widest cell; its *minimum* is its longest word, capped at 24 columns (a longer
   word breaks between graphemes).
2. When the natural widths and borders fit the available width, they are used.
3. Otherwise the columns wider than an equal share are narrowed toward their minimum, widest first, until the table fits;
   cells wrap within their column. Column alignment (`:--`, `:-:`, `--:`) applies within the cell.
4. When even the minimum widths do not fit, the table keeps them and scrolls horizontally on its own: only its lines
   shift, and the rest of the document stays put. A dim `◂` / `▸` at the row edges shows hidden columns on that side.

Each table has its own horizontal offset in the view, keyed like fold state (below). Moving the cursor
into a hidden column scrolls the table to show it; `<`/`>`, Shift+wheel and the edge markers scroll it.
A table that fits has no offset.

### Table editing (M5)

Done in view mode on the source, through `TableEdit` in Mdedit.Core (pure, with cfgedit's verification: the edited source
is reparsed and the table must hold exactly the intended cells, or the edit is refused):

- **Cell text:** Enter or F2 opens an inline field over the cell (as cfgedit's table editor does); typing in the cell
  directly comes with rendered-view editing. A `|` typed is written `\|`; line breaks are refused (pipe-table cells are one
  line).
- **Rows:** add after / before, delete, duplicate, move up / down. **Columns:** add left / right, delete, move left / right,
  change alignment. Keys follow cfgedit's table editor (`a`/`i`, Delete on the row marker, Alt+arrows), with the column
  operations on Ctrl+ variants and in the right-click menu.
- **Source formatting:** a table whose rows are all aligned in the source (every `|` of a column at the same display
  column) is rewritten aligned after an edit, keeping its style (outer pipes or not, padding, the delimiter row's dashes);
  any other table is edited minimally (only the cells and lines that change), so its diff stays small.

## Folding

The fold units are sections (a heading and everything up to the next heading of the same or a higher level) and code blocks.

- The view has a two-column fold gutter: every heading has a dim fold glyph there, `▾` unfolded and `▸` folded, and a
  folded section adds a dim `… n lines`. A code block starts with a header line, the glyph and its language, and folds to
  that line plus `… n lines`. The glyph is in column 0, so it never lines up with the text under it, folding does not shift
  the text, and the glyph is an easy click target. Edit mode has the editor's fold gutter, also two columns, and shows the
  same summary after a folded line.
- Folding moves a cursor inside the unit to its heading or fence line. Search, Ctrl+T from edit mode, link anchors (M4)
  and the outline (M4) unfold what they land in.
- Fold state is a set of folded units identified by their start in the source, held as anchors that move with edits, so
  folds survive typing in either mode. After a reload from disk a fold is kept when a unit with the same heading text and
  level (or the same fence line) is found at the same ordinal; otherwise it is dropped.
- Edit mode shows the same units as editor folds (the editor's `FoldingManager`), sharing the state (M3).

## Copy

The selection is held as a source range. Copy works the same with the keyboard (`y` / `Y`) and the right-click menu.

- **Plain text** is the rendered text of the selection: wrapped lines joined, soft breaks as spaces, hard breaks and block
  boundaries as line breaks, bullets as `-` and numbers as written, task boxes as `[ ]`/`[x]`, tables as tab-separated rows,
  code as written. Folded content inside the selection is included in full.
- **Markdown** is the source slice of the selection, made valid on its own: an inline container (emphasis, link, code
  span) cut by either end gets its delimiters (and a link its target) around the part copied; a selection that starts
  inside a list item or a quote gets that line's prefix; a selection inside a table gives the selected rows with the
  header and delimiter rows (all columns: a selection is a source range, so it has no column shape).
- In edit mode the editor's own copy copies the source.

## Source buffer and undo

One text buffer holds the source in both modes: the editor's `TextDocument`. Edits made in view mode (table operations,
later rendered-view typing) are applied to it as replacements, so they join the editor's undo history. Ctrl+Z / Ctrl+Y
undo and redo in either mode, and an operation that changes several places (a re-aligned table) is one undo step. After
each change the view reparses the buffer's text (see Layout). A reload from disk replaces the buffer's text as one undo
step.

## Rendered-view editing (later, M6)

The investigation found these edits translate exactly: typing and deleting inside a mapped run (inserting at the source
offset, escaping `*`, `_`, `` ` ``, `[`, `#`, `|`, `<`, `&` as needed so the structure does not change), and Enter in a
plain paragraph (a blank line in the source). The rules for the rest are to be designed before M6: the boundary between a
styled and a plain run, Backspace across invisible delimiters, a caret inside an entity or escape, Enter in a list item, a
quote or a heading, and typing directly in table cells. Every edit is checked as cfgedit's are: the source is reparsed and the rendered text
must show exactly the intended change, or the edit is refused.

## Saving and external changes

As in cfgedit (see `cfgedit-design.md`, Saving and external changes), on the shared `TextFile`:

- Ctrl+S writes in place through `AtomicFile.WriteExact`, keeping permissions, owner and ACL. If the file changed on disk
  since it was read, Confirm offers Overwrite / Reload / Cancel.
- A one-second check while the window is idle (no dialog, picker or save/close flow): a clean file reloads (in edit mode
  the editor's text is replaced, keeping the caret's line where it can, and the reload is one editor undo step); a dirty
  one asks Keep mine / Reload, Keep mine by default; a deleted one asks Keep / Close.
- Dirty means the current text differs from the text last loaded or saved.
- Quitting, Ctrl+O and following a link to another file with unsaved changes ask Save / Discard / Cancel, Save by default.

## Engineering rules

cfgedit's engineering rules apply unchanged (`cfgedit-design.md`, Engineering rules): an `MdeditException`-style error
boundary (here `TextFileException` from Shared.Core plus host messages), untouched bytes, widths through `DisplayWidth`
(except inside the editor and the Markdown view, which measure their own text), saving only through `AtomicFile`,
single-threaded UI, Confirm defaults, tests with independent oracles and visible platform skips.

Large files: view mode re-renders the whole text on each switch. A file over 2 MB opens in edit mode
with a message, and Ctrl+T to view mode asks first.

## Testing

- **File layer (Shared.Core.Tests):** the cfgedit `SourceText` and `DocumentFile` tests that cover the moved behaviour move
  with it; `TextFile` gets the round-trip corpus (CRLF, lone CR, mixed endings, BOM, UTF-16, NUL, surrogate pairs, no final
  newline) and the external-change cases.
- **Mdedit.Core.Tests:** the render model and layout against exact expected display lines for each construct in the
  Renderer table; the source map both ways for every mapped and unmapped case (escapes, entities, soft and hard breaks,
  delimiters, prefixes, table cells); wrapping with wide and combining graphemes; a property test that every mapped cell's
  offset holds the cell's character (or the start of its escape or entity); outline extraction (ATX and setext headings,
  headings inside fences ignored); search in rendered text.
- **Mdedit.Tests:** the fake-driver key and mouse tests per mode, cursor kept across Ctrl+T, the unsaved-changes and
  external-change flows (gates, not sleeps), Program argument handling; link classification (M4).
- **Layout** is checked with the pty driver in a real terminal, as for cfgedit.

## Milestones

0. **Investigation** (done, 2026-11-26): see Renderer.
1. **M0 Shared file layer.** Move `SourceText` and the disk half of `DocumentFile` to Shared.Core; cfgedit delegates;
   all tests green. Reviewed and committed on its own.
2. **M1 Scaffolding.** `src/Mdedit`, `src/Mdedit.Core`, both test projects, solution, `publish.ps1`, `mdedit --version`, usage.
3. **M2a Render core** (Mdedit.Core). Markdig parse; blocks and runs with source offsets (the Renderer table); per-block
   layout with the cache; the table column sizing; code blocks through `ICodeColorizer`, with wrap markers; fold units; the source map;
   plain-text and Markdown extraction of a source range (Copy); search in the rendered text.
4. **M2b Viewer.** Program and host (picker, Ctrl+O, recent list); main window with the rendered view, title, message and
   hints lines; cursor, selection, scrolling, reflow on resize, search, copy as plain text and as Markdown, mouse.
5. **M2c Rich view.** Horizontal scrolling of tables, the TextMate colorizer adapter, folding (keys, glyphs,
   summaries, mouse).
6. **M3 Editor.** The shared source buffer and undo; edit mode with highlighting and word wrap; Ctrl+T keeping the cursor;
   search in the source; editor folds sharing the view's state; save, unsaved-changes flow, new files, external-change check.
7. **M4 Navigation.** Links (anchor, relative file with back stack, external copy), outline dialog.
8. **M5 Table editing.** Cell editing in an inline field; row and column operations; alignment; the aligned/minimal rule.
9. **M6 Rendered-view editing**, once its rules are designed.

`docs/mdedit.md` (the user guide) is written with each milestone from M2b, not at the end, and the README links it from
M2b. The architecture notes (`docs/mdedit-architecture.md`) are added per milestone as cfgedit's were. The Windows
acceptance pass (by the user) follows M3 and each later milestone.

First version: M0–M3 (user, 2026-11-26). The table source rule (aligned tables stay aligned, others are edited
minimally) was chosen by the user the same day.

A tracker (`docs/mdedit-tracker.md`) is created at M1, in the same style as cfgedit's.

## Open questions

- Whether the outline should also be available in edit mode (jump the caret to a heading).
