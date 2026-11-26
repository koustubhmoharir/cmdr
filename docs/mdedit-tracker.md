# mdedit implementation tracker

This file tracks the work for [mdedit-design.md](mdedit-design.md). Work goes in milestone order, and each milestone ends
with at least one commit prefixed `mdedit:`, or `core:` for shared code.

**When an item is done, tick it and name the commit.**

Items marked *(Windows)* can only be verified by the user on Windows. Every item follows the design's "Engineering rules".
The first version is M0–M3.

## M0. Shared file layer

- [x] `SourceText` moved to Shared.Core; `TextFile` (format-neutral disk layer) and `TextFileException`; cfgedit's
      `DocumentFile` delegates, with its behaviour and messages unchanged. (ef6f97b)

## M1. Scaffolding

- [x] `src/Mdedit.Core`, `src/Mdedit` (`mdedit` executable), `tests/Mdedit.Core.Tests`, `tests/Mdedit.Tests`, all in
      `Cmdr.slnx`, with the Terminal.Gui fork reference set up like cfgedit's. (0fa6de1)
- [x] The Markdig pipeline (`MdPipeline`) with the extensions of the design. (0fa6de1)
- [x] `mdedit --version`, plus usage for bad arguments, with Program tests. (0fa6de1)
- [x] `publish.ps1` publishes and installs `mdedit`. (0fa6de1)

## M2a. Render core (Mdedit.Core)

- [ ] Blocks and runs with source offsets, per the Renderer table (escapes, entities, soft and hard breaks, code spans,
      autolinks, inline HTML, synthetic prefixes).
- [ ] Per-block layout with wrapping by `DisplayWidth`, never splitting a grapheme; the layout cache by (block text, width).
- [ ] Document layout (visible blocks, line index) and the source map (cell → offset, offset → cell or the next mapped one).
- [ ] Table column sizing (natural, minimum capped at 24, shrink widest first, horizontal overflow) and cell wrapping.
- [ ] Code blocks: `ICodeColorizer`, the info-string alias table, wrapping with `↪` markers.
- [ ] Fold units (sections, code blocks) and folded layout (summary lines).
- [ ] Copy: plain text and Markdown of a source range.
- [ ] Search in the rendered text.
- [ ] Heading outline.

## M2b. Viewer

- [ ] Program and host: picker, Ctrl+O with the file dialog, recent list (`mdedit/recent.json`), new-file flow, load errors.
- [ ] Main window: rendered view, title, message line, hints line.
- [ ] Cursor, selection, scrolling, reflow on resize keeping the cursor's screen row.
- [ ] Search (SearchBar) in the rendered text.
- [ ] Copy as plain text and as Markdown (`y`, `Y`, Ctrl+C, right-click menu).
- [ ] Mouse: wheel, click, drag, double-click, hints line, right-click menu.
- [ ] `docs/mdedit.md` started; README links it.

## M2c. Rich view

- [ ] Horizontal scrolling of tables (keys, Shift+wheel, edge markers), the cursor's cell kept visible.
- [ ] TextMate colorizer adapter; plain code without the native library.
- [ ] Folding: Space, `*`, `-`, `1`–`6`, `0`, glyphs and summaries, mouse.

## M3. Editor

- [ ] Shared source buffer (`TextDocument`) and one undo history across modes.
- [ ] Edit mode: `TextArea` with Markdown highlighting, word wrap (Alt+Z), Tab inserts, the file's line ending.
- [ ] Ctrl+T both ways keeping the cursor; Esc in edit mode.
- [ ] Search in the source.
- [ ] Editor folds sharing the view's fold state.
- [ ] Save, unsaved-changes flow (quit, Ctrl+O), new files, external-change check (reload, Keep mine, deleted).
- [ ] Large files (over 2 MB) open in edit mode.
- [ ] Windows acceptance pass. *(Windows)*

## Later

- M4 Navigation: links, outline dialog.
- M5 Table editing.
- M6 Rendered-view editing.
