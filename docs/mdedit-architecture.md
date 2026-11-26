# mdedit architecture notes

Implementation notes for [mdedit-design.md](mdedit-design.md), written per milestone. The design says what mdedit does;
this file fixes the types and rules the code and its tests follow.

## Render core (M2a, Mdedit.Core)

Namespace `Mdedit.Core.Render`. No Terminal.Gui types. Everything here is pure and immutable once built.

### Overview

```
text ──MdPipeline.Parse──▶ MarkdownDocument ──Lowering──▶ RenderDocument (leaf blocks of runs, offsets relative to the block)
RenderDocument + width + fold state ──DocumentLayout──▶ display lines of cells, with the source map
```

### Cells and styles

- `CellStyle` is a readonly record struct: `Role` (`StyleRole`), `Inline` (`InlineStyle` flags) and `HeadingLevel` (0 when
  not a heading).
  - `StyleRole`: `Body`, `Heading`, `CodeBlock`, `CodeHeader`, `QuoteBar`, `ListMarker`, `TaskBox`, `ThematicBreak`,
    `Html`, `ImageMarker`, `AlertTitle`, `TableBorder`, `WrapMarker`, `FoldGlyph`, `FoldSummary`.
  - `InlineStyle` flags: `Emphasis`, `Strong`, `Strikethrough`, `Code`, `Link`, `Image`.
  - Code colouring (M2a part 2) adds a `CodeToken` value to the style; it is `None` until then.
- `Cell` is a readonly record struct: `Text` (one grapheme cluster), `Width` (its `DisplayWidth`, 0, 1 or 2), `Style`,
  `Offset` (a source offset, or -1 for a synthetic cell) and `LinkId` (-1 when not in a link).
- A **run** is a sequence of cells with no line structure; a **display line** is the cells of one screen row plus:
  - `Anchor`: the source offset the cursor goes to on a line with no mapped cell, or after its last mapped cell;
  - `IsContinuation`: the line continues the previous one (wrapping, not a source line break).

### Lowering (AST → leaf blocks)

`RenderDocument Lower(string text, MarkdownDocument ast)` gives the leaf blocks in document order. A leaf block is a
paragraph, a heading, a code block (fenced or indented), a table (part 2), a thematic break or an HTML block. Container
blocks (quotes, alerts, lists, list items) do not become blocks of their own: each leaf records its **container chain**,
from which the layout builds its first-line prefix and its continuation prefix.

Each `RenderBlock` has: `Kind`, `Start` and `End` (its source span, `End` exclusive), `Containers` (the chain, outermost
first), `Runs` (the content cells, with offsets **relative to `Start`**), `HeadingLevel`, `Language` (code), `Tight` (whether
it sits in a tight list) and `IsFirstInItem` (whether it is the first leaf of its list item, which gets the marker).

Relative offsets let a block's lowering and layout be reused after an edit elsewhere (see Cache).

**Inline mapping.** Markdig's inline `Span.End` is inclusive. Per inline:

| Inline | Cells | Offsets |
|---|---|---|
| `LiteralInline` | its content, per grapheme | aligned to the span's source: walk the source and the content together; a `\` before ASCII punctuation in the source is skipped (unmapped) |
| `HtmlEntityInline` | the transcoded text | every cell gets the entity's first offset (`&`) |
| `LineBreakInline`, soft | one space | the offset of the line break's first character |
| `LineBreakInline`, hard | ends the display line | the next line's `Anchor` is the break's offset; trailing spaces and the `\` before it are unmapped |
| `CodeInline` | its content, style `Code` | aligned to the source between the delimiters (`DelimiterCount` backticks each side), skipping the one stripped space at each end when present; a line break inside becomes a space mapped to the break's first character |
| `EmphasisInline` | its children with the style flag (`*`/`_` count 1 → `Emphasis`, 2 → `Strong`, `~` 2 → `Strikethrough`) | delimiters unmapped |
| `LinkInline` (not image) | its children with `Link` and a new `LinkId` | brackets and target unmapped |
| `LinkInline` (image) | synthetic `[image] ` (`ImageMarker`), then the alt children with `Image` | the marker is synthetic |
| `AutolinkInline` | the URL, `Link`, new `LinkId` | `Start + 1 + i` |
| `HtmlInline` | the tag as written, role `Html` | 1:1 |
| `TaskList` | `[ ]` or `[x]` as written, role `TaskBox`, then a space | 1:1, the space synthetic |

Anything not listed renders its source span as plain text, 1:1.

Decisions made while building it (2026-11-26):

- **Hard breaks** split a block's content into runs: each run is one stretch between hard breaks, and the next run's `Start`
  is the break's offset (the newline for a two-space break, the `\` for a backslash break). The break itself has no cell.
- **Link ids** are unique within a block, not the document, so that a block's lowering stays reusable; a link is identified
  by (block, `LinkId`).
- **Headings:** only `Body` cells take the `Heading` role; inline HTML, image markers and task boxes keep their own.
- **Task boxes:** the space after `]` is a synthetic `Body` cell; the source space is not a cell.
- **NUL** stays NUL in the cell (Markdig reads it as U+FFFD), so the cell matches the source.
- **Unknown alert kinds** (`> [!FOO]`) lower as a quote whose first line is the marker text as written.
- **Blocks with no rendering** (link reference definitions, anything not in the block list) are `BlockKind.Raw`: source
  lines 1:1, role `Html` (dim).
- **Empty list items and empty alerts** get an empty paragraph, so the marker or title has a line to draw on.

**Block prefixes.** Synthetic, dim, built from the container chain:

- Quote and alert: `│ ` per level (`QuoteBar`). An alert's first line is its title (`Note`, `Tip`, `Important`, `Warning`,
  `Caution`) in `AlertTitle`, synthetic, anchored at the alert's start.
- Bullet list item: `• `, `◦ `, `▪ ` by bullet depth (cycling), on the item's first leaf; its continuation lines and later
  leaves get spaces of the same width.
- Ordered list item: the number as written in the source plus its delimiter (`3.`, `3)`) and a space, mapped 1:1 to the
  source digits and delimiter; continuation indent of the same width.
- Heading: a synthetic fold glyph `▾ ` (`FoldGlyph`) before the text. (The design's "only on hover" is replaced by this:
  a stable column is simpler to click and keeps text from shifting when folding.)

**Blocks.**

- Paragraph: its runs, wrapped.
- Heading (ATX and setext): fold glyph, then its inline runs with `Role = Heading` and `HeadingLevel`.
- Code block: a header line `▾ <language>` (or `▾ code` when there is none), role `CodeHeader`, synthetic, anchored at
  the block's start; then one line per source code line, cells 1:1 (`StringLine` slices, already without the indentation
  of an indented block), role `CodeBlock`. An empty code line has its line start as `Anchor`. Code lines wrap at the
  content width at grapheme boundaries; each continuation starts with a dim `↪` (`WrapMarker`, synthetic).
- Thematic break: one line of `─` across the content width, synthetic, anchored at the break's start.
- HTML block: its source lines 1:1, role `Html`, wrapped as code is but without markers.

**Blank lines.** One empty display line separates consecutive blocks, anchored at the end of the block before it, except
between blocks of a tight list (no blank line).

### Layout of one block

`BlockLayout LayoutBlock(RenderBlock block, int width)` gives its display lines (offsets still relative). `width` is the
whole screen width; the content width is `width` minus the prefix width, at least 1.

**Word wrap** (paragraphs, headings, list and quote text): a line takes whole words while they fit. Spaces after the last
word of a line stay on it if they fit; spaces that do not fit are not shown, and their offsets map to the start of the
next line. A word wider than the content width is broken between graphemes. A grapheme is never split. Widths come from
`Shared.Core.DisplayWidth`.

### Document layout and the source map

`DocumentLayout` is built from a `RenderDocument`, a width and the fold state (part 2; nothing folded in part 1). It holds
the display lines of every visible block, in order, with absolute offsets, and a line index (block → first line, line →
block, by binary search).

- `int OffsetAt(int line, int column)`: the offset of the cell at that column. On a synthetic cell, or past the line's last
  cell, the next mapped cell to the right on the same line; if there is none, the line's `Anchor`.
- `(int Line, int Column) PositionOf(int offset)`: the cell whose offset is `offset`; else the first cell whose offset is
  greater (the next mapped character, so the cursor lands on visible text when the caret was on markup); else the end of
  the last line.
- `string TextOf(int line)`: the line's cell texts joined, for tests and search.

### Layout decisions (2026-11-26)

- An alert containing a list: the title line carries the list marker; quotes nested inside the alert stay off it. The
  heading fold glyph goes on the first content line, after a title.
- Separator lines keep only the quote and alert bars, with the blanks needed before a bar to keep it in its column, and no
  trailing blanks. Two separate quotes with a blank line between them get a bar on their separator, because containers
  carry no spans to tell them apart.
- A line's Anchor is the first mapped cell after the last cell it shows, so a space dropped at a wrap counts; the end of
  a line maps to that space, which `PositionOf` places at the start of the next line.
- A word wider than the content width starts a new line before it is broken between graphemes. A single cell wider than
  the content width goes on its own line and overflows, so wrapping always progresses.
- A code header is not wrapped.
- Synthetic cells have the offset `Cell.Synthetic` (`int.MinValue`); relative offsets can be negative (ordered markers lie
  before their block), so no small number can mean "synthetic".
- `PositionOf` searches blocks by their lowest mapped offset (`BlockLayout.MinOffset`), since markers precede the block's
  start.

### Cache

`LayoutCache` keeps `BlockLayout`s keyed by (the block's source text, its kind, heading level and language, its container
chain, its `Tight` and `IsFirstInItem`, the document's `ReferenceKey`, the width). The reference key (the sorted labels of
the link reference definitions) is there because a paragraph's lowering depends on definitions elsewhere: `[x]` is a link
only when `[x]: url` exists. A resize computes each block once; an edit changes the text of a few blocks, and every other
block's layout is reused (its offsets are relative, so they are still right after shifting by the block's new `Start`).
The cache is bounded (least recently used, 4,096 entries).

### Tables (M2a part 2)

**Lowering.** A pipe table becomes a `Table` block with a `TableModel`: the column alignments (from the delimiter row:
left, center, right, or none = left) and the rows in source order, each with `IsHeader`, its source line start (relative)
and its cells. A cell is a run lowered from the cell's inlines with the usual mapping (Markdig parses pipe-table cells in
the document, so their spans are document offsets; a cell's span includes its padding spaces, which the alignment skips).
A row with fewer cells than columns gets empty cells; cells past the column count are dropped by the layout (Markdig keeps
them). The delimiter row has no cells. The block's `Runs` are its cells in row order, so the property tests and the source
map cover them. Link ids are unique across the table's cells.

**Column widths.** Per column: *natural* = the widest cell's display width (a cell's runs joined, as one line); *minimum* =
its longest word, capped at 24, and at least 1. Borders and padding take `3 × columns + 1` columns (`│ a │ b │`).

1. If the natural widths plus borders fit the content width, use them.
2. Else narrow one column at a time by one, always the widest column still above its minimum (the leftmost on a tie),
   until the table fits or every column is at its minimum.
3. If the table still does not fit, it keeps the minimum widths and is wider than the content width: it overflows.

**Drawing.** Box-drawing borders, synthetic, role `TableBorder`:

```
┌─────┬──────┐
│ a   │ b    │
├─────┼──────┤
│ 1   │ 2    │
└─────┴──────┘
```

A column's natural width is at least its minimum (an all-empty column is 1 wide). Each cell is word-wrapped
to its column width with the paragraph rules, and padded by one space each side; a row is as
tall as its tallest cell; a cell's lines are aligned left, centred (half the free space on the left, rounded down, the rest on the right) or right. Header cells keep
their inline styles and add `Strong`. The header separator `├─┼─┤` follows the header row; there is no separator between
body rows. Anchors: a row's lines anchor at the row's source line start; the top border and the header separator at the
next row's start (the block's end when there is none); the bottom border at the block's end. A row's second and later
lines are continuation lines; borders are not.

**Overflow.** `DisplayLine` gets `PrefixCells`: the number of cells at the start of the line that belong to the container
prefix (for every line, not only tables). `BlockLayout` gets `ScrollWidth`: the width of its widest line minus the prefix,
when that is more than the content width, else 0. The view scrolls a block with a non-zero `ScrollWidth` by shifting only
the cells after the prefix; the layout itself never cuts a line.

### Code colouring (M2a part 2)

- `CodeToken` gains: `Keyword`, `String`, `Number`, `Comment`, `Type`, `Function`, `Operator`, `Punctuation`,
  `Preprocessor`, `Constant`, `Attribute`, `Tag`, `Identifier`.
- `ICodeColorizer` (Mdedit.Core, no Terminal.Gui): `IReadOnlyList<CodeSpan>? Colorize(string code, string language)`,
  where `CodeSpan(int Start, int Length, CodeToken Token)` covers UTF-16 ranges of `code` (the block's lines joined with
  `\n`), and null means "no colouring for this language". Uncovered characters are `None`.
- `CodeLanguages.Normalize(string? info)` maps the first word of the info string, case-insensitively, to a canonical id:
  `cs`, `c#`, `csharp` → `csharp`; `go`, `golang` → `go`; `py`, `python` → `python`; `ts`, `tsx`, `typescript` →
  `typescript`; `js`, `jsx`, `mjs`, `cjs`, `javascript` → `javascript`; `html`, `htm` → `html`; `css` → `css`; `xml`,
  `xaml`, `csproj`, `svg` → `xml`; `json`, `jsonc`, `json5` → `json`; `sh`, `bash`, `shell`, `zsh` → `shell`; `ps1`,
  `powershell`, `pwsh` → `powershell`; `yaml`, `yml` → `yaml`; `sql`; `md`, `markdown` → `markdown`. Anything else is
  returned lower-cased as written; empty or missing gives null.
- `RenderDocument.Lower(text, ast, colorizer)` takes an optional colorizer and sets `CellStyle.Token` on code cells. The
  colorizer is fixed for the life of the app, so the layout cache key does not include it.

### Folding (M2a part 2)

- **Fold units** (`FoldUnit`): a *section* is a heading block with no containers and the blocks after it up to (not
  including) the next container-free heading of the same or a higher level (a smaller or equal level number), or the end;
  a *code unit* is one code block. `RenderDocument.FoldUnits` lists them in document order with `Kind`, `Start` (the head
  block's absolute start: the unit's key), `HeadBlock` and `EndBlock` (block indexes, end exclusive) and `HiddenLines`
  (source lines hidden when folded: from the line after the head block's last line to the unit's last block's last line,
  for a section; the code lines, for a code unit).
- **Fold state** is a set of unit keys (`IReadOnlySet<int>` of absolute starts) passed to `DocumentLayout.Build`.
  Folding a unit hides blocks `HeadBlock + 1 .. EndBlock - 1` and, for a code unit, its code lines. Units inside a folded
  unit are hidden with it, whatever their own state.
- A folded head shows `▸` instead of `▾`, and ` … n lines` (role `FoldSummary`, synthetic; `1 line` for one) after its
  last word, wrapped with the heading's words. A folded code block is its header line plus the summary. The cache key
  includes whether the head is folded.
- `DocumentLayout.FoldUnitAt(line)` gives the unit whose head line(s) contain the line, for the view's Space and clicks;
  `PositionOf` an offset inside a hidden block gives the head's first line (the view unfolds first when it needs the exact
  place).

Decisions (2026-11-26): the cache key carries the head's hidden-line count, not only a folded flag, since the summary
shows it; the summary appears only when lines are hidden; a folded head keeps its separator to the next visible block and
hidden blocks leave none; `PositionOf` into a folded code block gives its header at column 0; `RenderBlock.Language` is the
info string's first word as written and `LanguageId` its normalized id; `FoldUnits` is computed once per document.

### Copy, search and outline (M2a part 2)

All work on the `RenderDocument` and source offsets, not on a layout, so wrapping and folding do not change them; folded
content is included.

- `TextExtraction.PlainText(doc, start, end)`: the rendered text of the mapped cells whose offsets are in `[start, end)`.
  Within a block, cells in order (a soft-break space stays a space; a hard break is `\n`); between blocks `\n\n`, or `\n`
  between two tight blocks; a bullet item's first leaf starts with `- `, an ordered one with its marker and a space (only
  when the selection covers the leaf's first character); quote bars are dropped; a task box stays `[ ]`/`[x]`; table
  cells are joined with `\t` and rows with `\n`; code lines are joined with `\n`, without wrap markers. Lines end in
  `\n` regardless of the source's line endings.
- `TextExtraction.Markdown(doc, start, end)`: `text[start..end]` made valid on its own. The lowering records each inline
  container (emphasis, strong, strikethrough, link, image, code span) as an `InlineSpan` on its block: outer and inner
  ranges, opener and closer text (`**`, `` ` ``, `[` … `](url)`). A container cut by `start` (start inside it, after its
  opener) gets its opener prepended; one cut by `end` gets its closer appended; nested ones in the right order. When
  `start` is not at the start of its source line and the block has containers, the source text from that line's start to
  the block content's start on that line (`> - `) is prepended. A range inside a table becomes whole rows: the header row,
  the delimiter row, and the rows the range touches, as written in the source.
- `RenderSearch.Find(doc, SearchPattern)`: the matches in rendered text, as source ranges. Each block's text is its cells'
  text in order (code: per line; table: per cell; prefixes excluded), searched with `SearchPattern.Matches`; a match maps
  to `[offset of its first cell, offset of its last cell + that cell's source length)`, where a cell's source length is the
  length of the grapheme at its offset, or the whole entity (`&amp;`) for an entity's cell. (The distance to the next mapped
  offset would swallow closing delimiters: `*em*` would end after the `*`.) Matches never span blocks. `TimedOut` is
  carried over from the pattern.
- `Outline.Of(doc)`: the headings in order: level, plain text, block start, and whether it has containers.

Decisions (2026-11-26): in plain text, a list leaf gets, per enclosing list item, the item's marker when it starts the
item and spaces as wide as the marker otherwise, so nesting and later paragraphs are indented as on screen; a Markdown
range that starts mid-line gets the source from the line start to the line's first mapped character; table rows are copied
as written from column 0 (a table in a quote keeps its `> `), without a final line break; the outline's `Start` is the
heading block's start (the `#`).

### Tests

Exact expected display lines (`TextOf`) and exact offsets for every row of the inline table and every block kind; the
source map both ways, including each unmapped case; wrapping with CJK (width 2), combining marks, ZWJ emoji, a word longer
than the line and a width smaller than the prefix; CRLF sources; a property test over the nasty-input corpus and the
repository's own Markdown files: every mapped cell's offset holds that cell's first character in the source, or the `&` of
an entity, and `PositionOf(OffsetAt(l, c))` returns a cell on line `l` for every mapped cell.

## Viewer (M2b, Mdedit)

The `mdedit` executable, built like cfgedit's (`src/Cfgedit/Program.cs`, `Tui/CfgeditHost.cs`, `Tui/MainWindow.cs`). UI logic
that does not need a terminal stays in pure classes; views draw and route input.

### Program and host

- `Program`: `mdedit <file>`, `mdedit` (picker), `mdedit --version`; usage errors exit 2. A file that cannot be opened
  prints `mdedit: <path>: <message>` (or `<path>:<line>:<col>: <message>` for a decoding error) and exits 1. A path that
  does not exist is reported as such until M3 adds the new-file flow.
- `OpenedFile(string Path, TextFile File)`: a file opened with `TextFile.Open(path, SourceText.Decode)`.
- `MdeditHost`: `Run`, `RunPicker`, `TryLoad`, `Load`, `RecordRecent`, `RunDocuments`, the picker over a running document
  (Ctrl+O) and at startup, as in `CfgeditHost`, without formats. Recent list: `RecentFiles.DefaultStorePathFor("mdedit")`.
  The file dialog's types: `Markdown files` (`.md .markdown .mdown .mkd`), then any file.
- `MdeditHints`: the hint sets (view mode, picker, picker over a document), in `CfgeditHints`' style.

### Main window

Like cfgedit's: no outer border; a rounded `FrameView` titled with the file name (`*` when unsaved, from M3) holding the
`RenderedView`; the search bar above the message line while open; the message line; the hints line. Esc is swallowed.
Ctrl+O shows the picker over the document; Ctrl+Q quits. Messages expire after the same delay as cfgedit's, through an
injected scheduler so tests drive it.

**Message line.** Without a message it shows, on the left, the path of headings above the top line (`Install › Linux`,
from `Outline.Of`) and, on the right, the scroll position: `Top`, `Bot`, `All` or a percentage.

### RenderedView

A `View` over a `RenderDocument`, a `DocumentLayout` (rebuilt when the width or the document changes, through one
`LayoutCache` kept for the window's life) and view state:

- **Cursor**: a layout position (line, column), kept also as the source offset of its cell; plus a goal column for
  Up/Down. The cursor rests on mapped cells and on block end positions (below): Left/Right move to the previous/next mapped cell in document order
  (crossing lines, skipping synthetic cells, so in a table they move from cell to cell), Home/End to the first/last mapped
  cell of the display line, Up/Down to the mapped cell at or nearest the goal column on the previous/next line that has
  one (lines without mapped cells are skipped), PgUp/PgDn by a page, g/G and Ctrl+Home/End to the first/last mapped cell.
  The terminal cursor is shown at the cursor cell.
- **Scrolling**: a top line; the cursor is kept visible (scroll by the least lines). The wheel scrolls 3 lines without
  moving the cursor.
- **Reflow**: on a width change, the anchor is the cursor's source offset when the cursor is visible, else the first
  mapped cell of the top line; its screen row is recorded; after the new layout, the top line is set so the anchor's line
  is on that row (clamped to the document).
- **End positions**: the caret may also rest just after the last cell of a block's last display line (offset: the last
  mapped cell's offset plus its source length), so a block's last character can be selected and Ctrl+T can put the source
  caret at the end of a paragraph.
- **Selection** (M2b part 2): an anchor offset and the cursor offset, exclusive (`[min, max)`), since the caret sits before a
  character; Shift+movement extends it; a drag extends it and
  scrolls when the mouse is above or below the view; a double-click selects the word (the run of non-space mapped cells)
  under the mouse; a click clears it and moves the cursor. Drawn reversed.
- **Search highlighting** (part 2): every match's cells drawn in the search colours, the active one in the active colour.
- **Colours**: `MdColors` maps a `CellStyle` to an attribute: headings bold, with a colour per level (1–2 strongest); emphasis
  italic; strong bold; strikethrough; inline code and code blocks on a dark background; links underlined; quote bars, table
  borders, fold glyphs, wrap markers, thematic breaks, HTML and raw blocks dim; list markers and task boxes in an accent
  colour; alert titles bold in a colour per kind; code tokens through a token palette (M2c). The palette sits next to
  `BarColors`/`SearchColors` in style.

### Copy, search and menu (M2b part 2)

- `y` and Ctrl+C copy the selection as plain text, `Y` as Markdown (`TextExtraction`); with no selection, the block under
  the cursor (`[Start, End)` of its block). The text goes out through OSC 52 (`Osc52`, `WriteRaw`, as cfgedit's copy), with
  the messages `Copied text`, `Copied Markdown` or `Too large to copy`.
- Search: `RenderSearch` matches over the document, navigated as cfgedit's `TreeSearch` does (`/`, Ctrl+F, n/N, F3/Shift+F3,
  Enter, Esc), with the shared `SearchBar` and `ISearchTarget`. The active match is selected, and its first cell gets the
  cursor and is scrolled into view. The matches are recomputed when the document changes.
- Right-click opens a menu at the mouse: Copy text, Copy Markdown (Fold / Unfold from M2c). The menu works with the mouse
  and the keyboard.

## Editor (M3, Mdedit)

Patterned on cfgedit's text mode and its save and external-change flows (`src/Cfgedit/Tui/EditController.cs`,
`MainWindow.cs`: `Save`, `CheckExternalChange`, `InFlow`, `AskUnsaved`, `CloseOrKeep`), with the file layer of
`Shared.Core.TextFile`.

### Source buffer and modes

- **`SourceEditor`**: a Shared.Tui `TextArea` with `TabInserts`, `LineBreak` = the file's `SourceText.Newline`, the editor's
  built-in Markdown highlighting (`MarkDown-Mode.xshd` through `HighlightingManager`), and `WordWrap` on; Alt+Z toggles
  word wrap. Its `TextDocument` is **the source buffer** for both modes: the window never keeps another copy of the text.
  The editor's undo stack is the one history; Ctrl+Z / Ctrl+Y in view mode undo and redo on it and re-render.
- **Rendering from the buffer**: the window keeps the buffer's version (a counter bumped on every `TextDocument` change)
  at its last render. Entering view mode (and undo/redo in view mode) re-parses the buffer's text when the version moved,
  and gives the new `RenderDocument` to `RenderedView.SetDocument`, which keeps the caret, selection, folds and table
  scroll offsets by source offset (see Folds), recomputes the search matches, and keeps the top line's anchor on its row
  as a reflow does.
- **Ctrl+T, view → edit**: the editor's caret goes to `RenderedView.CursorOffset`, and a selection becomes the editor's
  selection over the same source range; the editor scrolls so the caret's line is on the same screen row as the view's
  caret when it can. **Edit → view** (Ctrl+T or Esc): re-render, then the view's caret goes to `PositionOf(caret offset)`
  (units hiding it are unfolded first) and the editor's selection becomes the view's selection; same screen-row rule.
- **Title**: the file name, ` *` when dirty, ` [edit]` in edit mode. **Hints** per mode: view (`^T Edit` added first),
  edit (`^T View`, `^S Save`, `^F Find`, `Alt+Z Wrap`, `^Q Quit`).
- **Dirty**: the buffer's text differs from `TextFile.Text.Text` (checked when the version moves, not on every draw).
- **Large files**: a file over 2 MB (its length in bytes on disk) opens in edit mode with the message `Large file: opened as text`; Ctrl+T to view mode then asks "Render <n> MB?" (Render /
  Cancel, Render the default).

### Folds across edits

The view's fold keys become `TextAnchor`s in the buffer (created at the unit's start, moving with insertions before it).
On each render, a unit is folded when an anchor sits at its start; anchors whose unit is gone are dropped. The table scroll
offsets are kept the same way. Edit mode shows the same units as editor folds (`FoldingManager`), built from the current
`RenderDocument.FoldUnits` on entering edit mode with the folded ones collapsed; folding or unfolding in the editor updates
the anchors, so the state is shared both ways.

### Saving and closing

- **Ctrl+S** in either mode: if `TextFile.ChangedOnDisk()`, Confirm "Changed on disk" Overwrite / Reload / Cancel (Overwrite
  the default, as cfgedit). Then `TextFile.Save(TextFile.Text.WithText(buffer text))`; `Saved` on success, the
  `TextFileException`'s message (the error style) on failure. After the first save of a new file the host records it in the
  recent list.
- **Unsaved changes**: Ctrl+Q, Ctrl+O (before switching) and Close after a deletion ask "Save changes to <name>?" Save /
  Discard / Cancel, Save the default; a failed save cancels the action.
- **New files**: `mdedit <path>` for a path that does not exist asks "Create <name>?" (Create / Cancel); the window opens on
  `TextFile.CreateNew(path, SourceText.Create("", UTF-8 without BOM, no BOM))` in edit mode, with the platform newline for
  Enter. Nothing is written before the first save; the folder must exist (the save reports it otherwise).

### External changes

As cfgedit's: a 1 s timer from the window's start to its stop calls `CheckExternalChange` when the window is idle (no
dialog, popover menu, picker or save/close flow; a deferred check is not queued). `TextFile.PollDisk()`:

- `Changed`, buffer clean: the buffer's whole text is replaced by the file's as one undo step (`TextDocument.Replace` in an
  undo group); message `Reloaded` unless the undo history was empty. Caret, selection and folds are kept by offset where
  they still fit.
- `Changed`, buffer dirty: "<name> changed on disk" Keep mine / Reload, Keep mine the default; Reload as above (the edits
  stay one Ctrl+Z away); Keep mine calls `TextFile.AcceptDiskState()`, so the next save does not ask and the document is
  dirty while it differs from the disk.
- `Deleted`: "<name> was deleted or moved" Keep / Close, Keep the default; Keep calls `MarkMissing()` (the next save
  creates the file); Close discards and returns to the recent-files picker.
- A failed poll shows its message once per state.

### Search in edit mode

The same `SearchBar` and query as view mode. In edit mode the matches are `SearchPattern.Matches(buffer text)` (source
text), recomputed when the buffer changes; the active match is selected in the editor and the caret goes to its start. The
query carries over between the modes; the matches do not (view mode searches rendered text).
