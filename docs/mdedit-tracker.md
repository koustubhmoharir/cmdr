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

- [x] Blocks and runs with source offsets, per the Renderer table (escapes, entities, soft and hard breaks, code spans,
      autolinks, inline HTML; prefixes are built by the layout). (62a8036)
- [x] Per-block layout with wrapping by `DisplayWidth`, never splitting a grapheme; the layout cache by (block text, width). (75a88aa)
- [x] Document layout (visible blocks, line index) and the source map (cell → offset, offset → cell or the next mapped one). (75a88aa)
- [x] Table column sizing (natural, minimum capped at 24, shrink widest first, horizontal overflow) and cell wrapping. (530724d)
- [x] Code blocks: `ICodeColorizer`, the info-string alias table, wrapping with `↪` markers. (4d1b230)
- [x] Fold units (sections, code blocks) and folded layout (summary lines). (4d1b230)
- [x] Copy: plain text and Markdown of a source range. (374f68b)
- [x] Search in the rendered text. (374f68b)
- [x] Heading outline. (374f68b)

## M2b. Viewer

- [x] Program and host: picker, Ctrl+O with the file dialog, recent list (`mdedit/recent.json`), load errors. (95f222e)
- [x] Main window: rendered view, title, message line, hints line. (95f222e)
- [x] Cursor, scrolling, reflow on resize keeping the cursor's screen row. (95f222e)
- [x] Selection (Shift+movement, drag, double-click). (1e35cbd)
- [x] Search (SearchBar) in the rendered text. (1e35cbd)
- [x] Copy as plain text and as Markdown (`y`, `Y`, Ctrl+C, right-click menu). (1e35cbd)
- [x] Mouse: wheel, click. (95f222e)
- [x] Mouse: drag, double-click, right-click menu. (1e35cbd)
- [x] `docs/mdedit.md` started; README links it. (1e35cbd)

## M2c. Rich view

- [x] Horizontal scrolling of tables (`<`/`>`, Shift+wheel, edge markers), the cursor's cell kept visible. (b015473)
- [x] TextMate colorizer adapter; plain code without the native library. (b015473)
- [x] Folding: Space, `*`, `-`, `1`–`6`, `0`, glyphs and summaries, mouse. (b015473)

## M3. Editor

- [x] Shared source buffer (`TextDocument`) and one undo history across modes. (7d5da4e)
- [x] Edit mode: `TextArea` with Markdown highlighting, word wrap (Alt+Z), Tab inserts, the file's line ending. (7d5da4e)
- [x] Ctrl+T both ways keeping the cursor; Esc in edit mode. (7d5da4e)
- [x] Search in the source. (7989645)
- [x] Editor folds sharing the view's fold state. (7989645)
- [x] Save, unsaved-changes flow (quit, Ctrl+O), new-file flow (a path that does not exist), external-change check
      (reload, Keep mine, deleted). (3e1d609)
- [x] Large files (over 2 MB) open in edit mode. (7d5da4e)
- [ ] Windows acceptance pass. *(Windows)*

## Later

- M4 Navigation: links, outline dialog.
- M5 Table editing.
- M6 Rendered-view editing.
