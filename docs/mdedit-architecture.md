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
- Fold gutter: every line with cells starts with two gutter columns, before its containers and part of its prefix
  (`PrefixCells`, so a scrolled table keeps it): the synthetic fold glyph `▾ ` / `▸ ` (`FoldGlyph`) on the first line of a
  heading or a code block, blanks elsewhere. The glyph is in screen column 0 whatever the nesting, so it never lines up with
  text, and heading text starts at the content column like the text under it. (The design's "only on hover" is replaced by
  this: a stable column is simpler to click and keeps text from shifting when folding.) Lines with no cells (an empty code
  line, a separator without bars) stay empty.

**Blocks.**

- Paragraph: its runs, wrapped.
- Heading (ATX and setext): its inline runs with `Role = Heading` and `HeadingLevel`.
- Code block: a header line, the fold glyph in the gutter and the label `<language>` (or `code` when there is none), role
  `CodeHeader`, anchored at the block's start. The label's first cell maps offset 0 (the fence, or an indented block's
  indentation) when no code cell does, which is always for a folded block, so the header is a caret stop and Space there
  folds or unfolds it; End on it stays on the label; then one line per source code line, cells 1:1 (`StringLine` slices, already without the indentation
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
  heading's fold glyph goes in the gutter of its first content line, after a title.
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
hidden blocks leave none; `PositionOf` into a folded code block gives its header's label (2026-11-26, revised: the header was synthetic and the caret
skipped a folded code block); `RenderBlock.Language` is the
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

- **`SourceEditor`**: a Shared.Tui `TextArea` with `TabInserts`, the fold gutter (`GutterOptions.Folding`, two columns
  like the view's, so source and rendered text both start at column 2), Alt+F for the editor's fold toggle (its default,
  Ctrl+M, arrives as Enter from a terminal), `LineBreak` = the file's `SourceText.Newline`, the editor's
  built-in Markdown highlighting (`MarkDown-Mode.xshd` through `HighlightingManager`), and `WordWrap` on; Alt+Z toggles
  word wrap. Its `TextDocument` is **the source buffer** for both modes: the window never keeps another copy of the text.
  The editor's undo stack is the one history; Ctrl+Z / Ctrl+Y in view mode undo and redo on it and re-render.
- **Rendering from the buffer**: the window keeps the buffer's version (a counter bumped on every `TextDocument` change)
  at its last render. Entering view mode (and undo/redo in view mode) re-parses the buffer's text when the version moved,
  and gives the new `RenderDocument` to `RenderedView.SetDocument`, which keeps the caret, selection, folds and table
  scroll offsets by source offset (see Folds), recomputes the search matches, and keeps the top line's anchor on its row
  as a reflow does.
- **Ctrl+T, view → edit**: the editor's caret goes to `RenderedView.CursorOffset`, and a selection becomes the editor's
  selection over the same source range; the editor keeps the caret visible (it has no public way to read or set its
  scroll row, so the screen row is not kept across the switch). **Edit → view** (Ctrl+T or Esc): re-render, then the view's caret goes to `PositionOf(caret offset)`
  (units hiding it are unfolded first) and the editor's selection becomes the view's selection; the view keeps the caret visible. The edit-mode message line shows the caret's `line:col`.
- **Title**: the file name, ` *` when dirty, ` [edit]` in edit mode. **Hints** per mode: view (`^T Edit` added first),
  edit (`^T View`, `^S Save`, `^F Find`, `Alt+Z Wrap`, `Alt+F Fold`, `^Q Quit`).
- **Dirty**: the buffer's text differs from `TextFile.Text.Text` (checked when the version moves, not on every draw).
- **Large files**: a file over 2 MB (its length in bytes on disk) opens in edit mode with the message `Large file: opened as text`; Ctrl+T to view mode then asks "Render <n> MB?" (Render /
  Cancel, Render the default).

### Folds across edits

The view's fold keys become `TextAnchor`s in the buffer (created at the unit's start, moving with insertions before it).
On each render, a unit is folded when an anchor sits at its start; anchors whose unit is gone are dropped. The table scroll
offsets are kept the same way. Edit mode shows the same units as editor folds (`FoldingManager`), built from the current
`RenderDocument.FoldUnits` on entering edit mode with the folded ones collapsed; folding or unfolding in the editor updates
the anchors, so the state is shared both ways.

Decisions (2026-11-26): `MarkdownFoldingStrategy` makes the editor's foldings (a section from the end of its heading's
first line to the end of its last block, a code unit from the end of its fence line); they are brought up to date 250 ms
after a batch of changes that is not only letters, and always before Ctrl+T shows the view. A gutter click or Alt+F changes the editor's foldings, and the change flows back to
the view. The editor's own fold marker (`FoldingTransformer`) misses a folding that starts at the end of a line, so
`FoldSummaryTransformer`, one of the editor's line transformers, draws ` … n lines` after a folded head line (on the last
segment of a wrapped line), with the view's count (`FoldUnit.HiddenLines`, which the strategy records on each update; the
editor's own count would include a closing fence) and the view's `FoldSummary` colour. (Revised 2026-11-26: the gutter was
off at first, and a folded unit showed nothing at all in edit mode.)

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

## Navigation (M4)

Links and the outline. The design's Keys and Mouse sections hold the user-facing rules; the user decided on 2026-11-26 that
a plain click places the caret (also inside a link) and Ctrl+click follows a link, that Backspace is left for rendered-view
editing (M6) so Alt+Left alone goes back, and that the outline is also in edit mode, on Alt+O (no F2: to most users F2
means "edit").

### Core (Mdedit.Core)

- **`LinkInfo(int Id, string Url, int Start, int End, bool IsAutolink)`**: one per link of a block, in
  `RenderBlock.Links`, indexed by the cells' `LinkId`. `Url` is Markdig's resolved destination (a reference link gives its
  definition's URL; an autolink its address as written, with `mailto:` in front of an email address so that it is external);
  `Start`/`End` the link's absolute source span. Images are not links.
- **`LinkTarget`** and **`LinkTargets.Classify(string url)`** (pure): an `Anchor(fragment)` when the URL starts with `#`;
  an `External(url)` when it has a URI scheme (`^[A-Za-z][A-Za-z0-9+.-]+:`, two characters or more, so that a Windows
  drive `C:` is not a scheme) or starts with `//`; otherwise a `File(path, fragment)`: the part before `?`/`#`,
  percent-decoded, with `/` as written (both separators are accepted on Windows), and the fragment after `#` or null. An
  empty URL is `External("")`, reported as "Empty link".
- **`HeadingAnchors.Of(doc)`**: the GitHub anchor of every heading, in document order: the heading's plain text (as the
  outline has it) lower-cased; characters that are not letters, digits, spaces, `-` or `_` removed; each space replaced by
  `-`; a repeated anchor gets `-1`, `-2`, … in document order. `Find(fragment)` percent-decodes the fragment and compares it
  case-insensitively, giving the heading's start or null.
- `RenderDocument` exposes `Links` through its blocks only; the view looks links up by the caret's cell (`LinkId` and the
  block).

### View and window (Mdedit)

- **Caret on a link**: the message line's left part shows `→ <url>` instead of the heading path while the caret is on a
  link cell.
- **Tab / Shift+Tab**: the caret goes to the first cell of the next / previous link in document order, skipping links that
  folds hide, wrapping around at the document's ends (message "Wrapped to the first link" / "… last link"); no links:
  "No links". A link in a scrolled table scrolls it as any caret move does.
- **Following** (Enter on a link cell, Ctrl+click on one): by `LinkTargets.Classify`.
  - Anchor: `HeadingAnchors.Find`; the units hiding the heading are unfolded, the caret goes to the heading's first text
    cell, and the heading's line becomes the top line (as far as the end of the document allows). Not found: "No heading
    #<fragment>" (error style).
  - File: resolved against the current file's directory. Not found or a directory: "Not found: <path>" (error style). Else
    the window loads the file through the host's loader (a load error is shown on the message line and the document stays),
    then runs the unsaved-changes flow of Ctrl+O (Save / Discard / Cancel; loading first means Discard never loses the
    edits to a file that then fails to load), and closes with `SwitchTo` set and a `BackEntry`
    for itself; the new window goes to the fragment's heading, or says "No heading #…" when there is none. The host owns one
    `BackStack` per run of documents and shares it with each window, which pushes, peeks and pops it.
  - External: copied with OSC 52, as Copy is; message "Copied link: <url>".
- **Back** (Alt+Left in view mode): the host keeps a stack of `BackEntry(string Path, int CaretOffset, int TopOffset)`, one
  pushed per followed file link. Alt+Left with an empty stack: "No previous file". Else the unsaved-changes flow, then the
  previous file is loaded again (from disk) and its window opens with the caret and top line at the saved offsets (clamped).
  A load error: the message line, and the entry is dropped. Ctrl+O and the picker clear the stack.
- **Outline dialog** (Alt+O in both modes): a dialog with a filter field over a list of `Outline.Of`
  entries, two spaces of indent per level below 1. Typing filters (case-insensitive substring of the text; the indent is
  kept); Up/Down/PgUp/PgDn move in the list while the field has the focus; Enter or a double-click goes to the selected
  heading; Esc cancels. The initial selection is the heading of the section the caret is in. Going to a heading: in view
  mode as an anchor link does; in edit mode the editor's caret goes to the heading's start and the foldings hiding it are
  unfolded.
- The window's navigation code lives in `MainWindow.Navigation.cs` (partial) and `OutlineDialog.cs`, to keep
  `MainWindow.cs` from growing.

## Table editing (M5)

The design's "Table editing (M5)" holds the rules. Keys follow the direction the user chose on 2026-11-26 for M6 (the
view becomes editable, so commands take modifier keys and no letters): F2 edits the cell at the caret, and Enter does too
when the caret is not on a link; Alt+Up/Down move the row, Alt+Left/Right move the column; Alt+T opens the table menu at
the caret, and the right-click menu over a table has the same items. (Alt+Left in a table moves the column; outside a
table it is Back, M4.)

### Core (Mdedit.Core, no Terminal.Gui)

- **`TableSource.Parse(string text, RenderBlock table)`**: the table as written: per source line (header, delimiter,
  body rows) its line span, its container prefix (the text from the line's start to the row's first character: `> `,
  list indentation), whether it has a leading and a trailing pipe, and each cell's span (between unescaped pipes; GFM
  splits on every unescaped `|`, code spans included) with its content span (trimmed of spaces). Markdig's row and cell
  spans locate the rows; the pipes are found by scanning the line. Also `IsAligned`: every line has its pipes at the same
  display columns (`DisplayWidth`), the delimiter row included.
- **`TableEdit`** (pure): each operation takes the source text and the table block and returns
  `TableEditResult` = one replacement `(Start, Length, Text)` plus the target cell `(Row, Column)` for the caret, or a
  refusal message. Operations: `SetCell(row, column, text)`, `InsertRow(at)`, `DeleteRow(row)`, `DuplicateRow(row)`,
  `MoveRow(row, delta)`, `InsertColumn(at)`, `DeleteColumn(column)`, `MoveColumn(column, delta)`,
  `SetAlignment(column, alignment)`. Rows are numbered as the `TableModel` has them (0 = header; the delimiter row is not a
  row).
  - Cell text: the inline field shows the cell's source content with `\|` shown as `|`; on commit every `|` is written
    `\|`, and a line break is refused ("A table cell is one line"). Leading and trailing spaces are dropped.
  - Refusals: deleting the header row ("The header row cannot be deleted"), deleting the last column, moving the header
    row or a row onto it ("The header row stays first"), moving past either end ("Already first" / "Already last").
  - **Aligned tables** (`IsAligned`) are rewritten whole: each column as wide as its widest cell (at least 3, for the
    delimiter's dashes), one space of padding on each side, cells left-justified, centred or right-justified by the
    column's alignment, the delimiter row as dashes with the alignment's colons, the outer-pipe style and each line's
    container prefix kept.
  - **Other tables** are edited minimally: only the cells and lines that change. A new row copies the outer-pipe style of
    the row it is inserted next to, with empty cells (`|  |  |`); a new column adds ` |` cells at the same position on every
    line (` --- |` on the delimiter); a moved row swaps lines; a moved column swaps the cells' source on every line.
  - The several changes of one operation are one replacement (from the first changed offset to the last), so the
    operation is one undo step and anchors outside it are untouched.
  - **Verification** (as cfgedit's): the edited text is reparsed; the table that starts at the same offset must have the
    intended number of rows and columns, the alignments, and every cell's plain text equal to the intended one (the
    others unchanged). Otherwise the result is a refusal "Could not edit the table safely", and nothing changes.

Decisions (2026-11-26): the code is in `Mdedit.Core.Tables` (source model and scanner, the edit grid, an aligned and a
minimal writer, the verifier). Extra refusals: "The header row cannot be duplicated", "The last column cannot be deleted".
`SetAlignment` takes null for none (`---`). A new row continues the prefix of the row above, with list markers turned into
spaces; blank cells are two spaces; a new column's delimiter is ` --- `. A row without outer pipes whose first or last
cell becomes empty gets that pipe (GFM would otherwise drop or merge the cell); in an aligned rewrite every line gets it.
Cells beyond the header's column count (GFM ignores them) are dropped from the rows an edit rewrites. A cell text that ends
in a backslash before a typed `|` (`a\|b`) is refused by the verification, since its meaning is ambiguous.

### View and window (Mdedit)

- `RenderedView.TableCellAtCaret()`: the table block and `(Row, Column)` of the caret's cell, from the cell's offset and the
  `TableModel`; null outside a table. After an operation, the window applies the replacement to the buffer (one undo
  step), re-renders, and puts the caret on the first mapped cell of the target cell (or the cell's position when it is
  empty).
- **Inline field**: a `TextField` over the cell's first screen line, from the cell's column to the view's right edge (at
  least 20 columns; the table scrolls to show the cell first). Enter commits, Esc cancels, Tab commits and edits the next
  cell (the next row's first after the last column; after the last row it commits and closes), Shift+Tab the previous. A
  refusal shows on the message line and the field stays open with the text. While the field is open, the external-change
  check waits, as it does for dialogs.
- **Table menu** (Alt+T, and the right-click menu over a table): Edit cell, Insert row above / below, Duplicate row,
  Delete row, Move row up / down, a **Column** submenu (insert left / right, delete, move left / right) and an **Align
  column** submenu (left / centre / right / none), so that both menus fit an 80x24 terminal. Items that would be refused
  are still shown and give their message.
- **Empty cells are caret stops**: the layout maps one blank of an empty cell to the cell's position in the source (just
  before its closing pipe, recorded by the lowering in `TableRowModel.Positions`; Markdig gives an empty cell no span).
  The runs are unchanged, so copy and search do not see it. A row shorter than the header has no stops for its missing
  cells.
- The verification also reparses the source: every row an edit wrote must have the header's number of cells as written
  (GFM drops extra cells silently, so `a\|b` in the last column would lose `b`).
- Edit mode has no table operations (the source is edited directly).
- Window code in `MainWindow.Tables.cs` (partial) and `TableCellField.cs`.

## Rendered-view editing (M6)

The design's "Rendered-view editing" holds the user's decisions (2026-11-26): the view is always editable, its commands
take modifiers, Enter edits and Ctrl+Enter / Ctrl+click follow a link, typing at the end of a style continues it (not a
link), and paste inserts Markdown source. M6 comes in three parts.

- **M6a** Keys and text: the key remap; typing, Backspace and Delete inside a block; replacing or deleting a selection
  inside one block; undo grouping.
- **M6b** Structure: Enter (split or add a block), Backspace at a block's start and Delete at its end (merge or unwrap).
- **M6c** Tables and paste: typing directly in table cells (Tab moves between cells), paste, deleting a selection across
  blocks.

### Edit engine (Mdedit.Core.Editing, no Terminal.Gui)

Every rendered-view edit is a pure function of the `RenderDocument` (its text and blocks) and the caret or selection,
given as source offsets, returning `ViewEditResult` = one replacement `(Start, Length, Text)` and the caret's source offset
after it, or a refusal message. The window applies the replacement to the buffer and re-renders, as table edits do.

- **Insertion point** of text typed with the caret before cell *c* of a block (or at its end position): with a previous
  mapped cell *p* in the same run, right after *p*'s source (its offset plus its source length, see `RenderSearch`'s
  `SourceLength`), so the text joins *p*'s styles (continuation); except that when *p* is in a link that *c* is not in,
  after the link's end (`LinkInfo.End`), so links never grow at the end. With no *p* (the run's start), at *c*'s offset,
  so the text takes *c*'s styles. In a code span or a code block the text is inserted as is; elsewhere it is escaped:
  `\`, `*`, `_`, `` ` ``, `[`, `]`, `<`, `&` always; `|` in a table; `#`, `>`, `-`, `+`, `=` and `1.`-style digits when
  they would start the block's line.
- **Backspace / Delete** remove the previous / next mapped cell's source in the same block: its source length, an entity
  whole, an escape with its backslash. A style span (`InlineSpan`) left with no text is removed with its delimiters; a
  link left with no text is removed with its destination. At a block's start / end they are M6b's.
- **Selection inside one block**: the source of every selected mapped cell is removed (the markup between cells stays,
  so delimiters stay balanced), then empty spans are removed as above; typing with a selection removes it first and
  inserts at the point where it started.
- The several removals of one edit are written as one replacement covering them (the block's changed middle), so an edit
  is one change of the buffer.
- **Verification**: the edited text is reparsed. The blocks outside the edit must be unchanged (kind, containers, plain
  text), the edited block must have the same kind and containers, and its plain text must equal the expected one (the
  old plain text with the typed text inserted at the caret, or the removed characters gone). Otherwise the edit is
  refused with "Can't edit this here; Ctrl+T edits the source", and nothing changes.
- **Pending insertion**: text whose rendering would not show (a space at a block's end or start, two spaces, a new empty
  block after Enter at a block's end) is kept by the view as a *pending insertion*: a source offset, the source text
  waiting to be inserted, and the synthetic cells the view draws for it at the caret. The next typed character is
  inserted together with the pending text as one edit; moving the caret, a command or a mode switch drops it (nothing was
  written). M6b's new paragraphs and list items start as pending insertions too (a new item shows its marker), so
  Markdown never holds an empty block.

**As built (M6a core).** `ViewEdit.Insert(doc, caret, typed, pending)`, `DeleteBackward`, `DeleteForward` and
`ReplaceSelection(doc, start, end, typed)` return `ViewEditResult.Replacement(Start, Length, Text, Caret)`, `Pending(Offset,
SourceText)` or `Refusal(Message)`. The view keeps a `Pending` and hands it back to the next `Insert` at the same offset;
the engine ignores one held at another offset. The cell source length is `Render.SourceLength` (shared with search).
Decisions taken while building it:

- `~` is escaped everywhere (strikethrough is on), and HTML and raw blocks are written raw like code.
- A removal in a paragraph or heading also removes the spaces it leaves at the block's edges (Markdown strips them, so
  the verification could not tell them apart), unless text is typed in its place.
- A typed space that would not show is held as pending; a space typed over a selection is written.
- A selection across a hard break and deleting a block's only character were refused in M6a; M6b made them edits.
- A typed bare URL becomes an autolink; its plain text is unchanged, so it is allowed.
- Each edit reparses the whole document, as table edits do.

### View and window (Mdedit)

- **Keys (M6a, M6b)**: printable characters insert; Ctrl+C / Alt+C copy plain / Markdown; Alt+F fold; Alt+1–6, Alt+0 levels;
  Ctrl+Home / Ctrl+End; Alt+O outline; Tab / Shift+Tab next / previous cell in a table, else next / previous link; Ctrl+Enter
  and Ctrl+J (a line feed, which many terminals send for Ctrl+Enter) follow a link, and Enter follows none; `*` and `-`
  folding only in the right-click menu, as "Unfold all inside" and "Fold section"; `<` / `>` removed. F2 and Enter edit a
  table cell in its field (M5) until M6c; Enter elsewhere edits the text (M6b). Tab leaves a table after its last cell, for
  the next link, so that the caret is never held in a table.
- **Undo grouping**: a run is a series of typed characters, of Backspaces or of Deletes, each starting where the last one
  left the caret. A typed non-space after a typed space starts a step; a selection replaced starts one; any other key, a
  mouse caret move, a table edit, an undo or a mode switch ends the run. M6b: Enter is a step of its own; a Backspace
  at a display line's first cell, or a Delete at its end position, is a step of its own too, since it may join or unwrap
  blocks (the window decides this from the caret's place, not from the edit, so a Backspace at a wrapped line's start is
  also a step of its own). A replacement that holds a pending block (its `Then`) ends the run. The window decides whether an edit continues the
  run's step. `UndoStack.StartContinuedUndoGroup` does not compare descriptors: it continues the last closed group
  whenever the last action was not an undo or a redo, so a new run starts its step with `StartUndoGroup`, and only a run
  that goes on uses the continued form. A pending space writes no step of its own; it joins the step of the character
  that writes it.
- **Pending insertion**: the view draws an inline one (a held space, or the marker of an empty document) at the caret, with
  the rest of the caret's line moved right and clipped, and the cursor after it; it draws its `DisplayText`, not its source.
  The view drops it on any caret move (keys and presses), on a new document and on a fold change; the window drops it for
  any other command, and Ctrl+Z drops it without an undo. A refusal with an empty message drops nothing: Backspace at the
  document's start neither changes the text nor shows a message, and keeps the pending.
- **Virtual line (M6b)**: a Below or Above pending is drawn on a row of its own, which the layout does not have. The row
  shows the gutter (two blanks, as a content line has), then the pending's `DisplayText`, and the cursor is on it after
  that text. A Below pending's row is the one under the caret's row, and an Above pending's row is the caret's row; the
  caret's line and the lines after (or before) it move down by one. `LineAtRow` maps the screen rows to layout lines, and
  the virtual row maps to the caret's line, so a press on it places the caret on that line (the pending is dropped after
  the press is mapped). The view's row count includes the virtual line, so the top line can scroll to the end of the
  document, and a Below pending whose row is past the bottom scrolls the top line by the rows it is past. The window gives
  `Replacement.Then` to the view as its pending at the caret, after the caret is placed.
- **Caret stops (M6b)**: a paragraph line that a hard break ends has an end position, after its last mapped cell, as the
  last line of a block has (`IsHardBreakEnd`); Delete there joins the lines. Code lines have no such stop yet.
- **Empty document**: a document with no block has no caret stop, so the view draws an empty line of its own with the
  caret on it (after the gutter, and after the pending's display text when it holds one). The window gives typing the
  empty inline pending at offset 0 (`TypingPending`), so the first character writes a paragraph. Enter, Backspace and
  Delete do nothing in it, without a message.
- Typing on a synthetic position (a fold summary, a code label, a table border) is refused by the engine. Typing on a folded
  unit's head is refused by the window, since the engine does not know folds, with the message "Unfold to edit here;
  Ctrl+T edits the source". Typing in edit mode is the editor's.

### Structure edits (M6b)

M6b adds `ViewEdit.Enter(doc, caret, pending)` and makes Backspace at a block's (or line's) start, Delete at its end, a
selection across lines of one block and the removal of a block's last character into edits. They keep the M6a rules:
pure functions, one replacement, a reparse and a verification, a refusal otherwise.

- **Line breaks** are the document's dominant line ending (the first one in its text, else `\n`). New lines carry the
  container prefixes of the block they continue (`> ` per quote, the item's indentation), and a new block is separated by
  a blank line carrying the quote prefixes, except between items of a tight list.
- **Enter in a block's middle** splits it, both halves keeping the kind and containers: two paragraphs, two headings of
  the same level, two list items (the new item's marker is the old one's bullet, or its number plus one, with `[ ] ` for a
  task item; later numbers are left as written, since the rendering numbers from the list's start). In a code block (and
  any raw block) Enter writes a line break with the container prefixes. With a selection, the selection is removed first
  (one block only, as in M6a). In a table, Enter keeps opening the cell field (M5) until M6c.
- **Enter at a block's end** holds a pending new block below: after a heading or a paragraph, a paragraph with the same
  containers; after a list item's text, a new item. **Enter at a block's start** (a non-empty block) holds a pending new
  block above, of the same shape. Enter again on a pending new item ends the list: the pending becomes a paragraph after
  the list (in the parent item, for a nested list); on a pending paragraph in a quote it becomes a paragraph after the
  quote; on a pending top-level paragraph it changes nothing.
- **Backspace at a block's start**, in order: in a heading, the heading becomes a paragraph (its marker goes); in the
  first leaf of a list item, the item joins the previous item's last text block when there is one, else it becomes a
  paragraph before the list; in the first leaf of a quote with a block before it in the quote, it joins that block, else
  it leaves the quote; otherwise the block joins the previous text block (paragraph or heading) with the same containers,
  its text appended with nothing between, keeping the previous block's kind. A previous thematic break is removed. A
  previous code block, table, HTML block or a different container nesting refuses ("Can't join these blocks; Ctrl+T
  edits the source"). At the document's start nothing happens.
- **Delete at a block's end** is the mirror: the next text block's text is appended (its marker, prefix and kind go),
  a next thematic break is removed, anything else refuses. At the document's end nothing happens.
- **Inside a block**: Backspace at the start of a line after a hard break removes the break (backslash or trailing spaces)
  and joins the lines; Delete at the end of such a line does the same; in a code block they join the lines. A selection
  across lines of one block is removed with the breaks between them.
- **A block's last character**: removing it removes the block with its separator, and holds a pending block of the same
  shape where it was (a heading's marker, an item's marker), so typing writes the block back and a caret move leaves it
  gone. This replaces M6a's refusal.
- **Pending results** gain a placement: `Inline` (M6a's held spaces), `Below` (a new line under the caret's screen line)
  and `Above` (a new line over it), and the text the view draws before the caret on that line: the rendered prefix of the
  new block (quote bars, a bullet or number, the `☐` of a task item, the heading's level mark as the view shows it), built
  by the layout's prefix code. A replacement may carry a pending to hold after it (the removed last character). The
  next typed character writes the pending source with the escaped text, the line-start escapes applying.
- **Verification** compares the whole block list with the expected one: the old blocks with the edit's change (a block
  split in two, two blocks joined into one, a block's kind or containers changed, a block removed or added), each block's
  kind, containers and plain text. Container identity follows the edit (a new item is a new container).

In the view, Enter becomes an edit key (except in a table cell, where it opens the field until M6c), so only Ctrl+Enter,
Ctrl+J and Ctrl+click follow a link. Each Enter, join and unwrap is an undo step of its own and ends a typing run.

**As built (M6b core).** `ViewEdit.Enter(doc, caret, pending)` and `ViewEdit.EnterOver(doc, start, end)` (Enter over a selection: the selection is removed,
then Enter acts where it started, and a pending block rides on the replacement as `Then`). `DeleteBackward` and `DeleteForward` take the
structure branches at a block's edges and at a line break inside a block. `Pending` gained `Placement` (`Inline`, `Below`, `Above`), `Display` (the text
drawn before the caret on the pending's line, built by `LinePrefix.Containers`; null means `SourceText`, which is how inline spaces are drawn), and
`Tail` (the source an Above pending writes after the typed text). `Replacement` gained `Then`. Pending `Offset` is always the caret the view holds,
which for Above is the block's first text; the write for Above is at the block's start (`Block.Start`), before the block's marker, so that the typed
block is a paragraph in front of it. Line endings use `LineEnding.Of` (the first line break of the text, else `\n`), not `SourceText.Newline`, which
takes the most common break. Verification compares the whole block list (`BlockShape`: kind, heading level, containers with identities, rendered text)
with the list the edit expects; identities are canonical, so a renumbered instance is the same one. Tightness is not compared. Decisions where the spec
was silent: a pending below a heading is a paragraph, and a pending above a heading is a paragraph too (the coordinator's answer); a new item is made
only after the last leaf of its innermost item, else a paragraph of the same containers, which makes a tight list loose (a known effect); the number
of a new ordered item is the old number plus one, and an Above item keeps the old number; the bullet character of a new item is copied from its list;
a task item's new item is unchecked (`[ ] `, drawn as `[ ] `); Backspace at a heading's start removes its `#` marker; a hard break or code line join
removes the break and the next line's prefix with nothing between; a soft break removal does the same, which fixes the M6a bug where deleting a soft
break inside a quote or a list item left the next line's prefix behind; a refusal with an empty message means nothing is before or after the block; a
block whose last character is removed goes with its separator, and the pending holds its marker (a heading's `#`, a task box) so that typing writes it
back. A split escapes the new block's first text where it would be block syntax (a list marker, a heading, a quote, a rule; a number's `.` or `)`
after its digits), as typing there would: the backslash goes before that character, and the caret follows the text. The spaces on either side of a split
are not written, since Markdown would strip them at the edges of the two blocks. A typed space into a pending block
returns the same pending: nothing is written and nothing is refused. Removing the only block of a document removes its line and keeps what is outside
it (blank lines, say, since a document with no block has nothing else); the replacement holds an `Inline` pending at the line's start, whose
`SourceText` is the block's line prefix and marker (`# `, `- `, `> `) and whose `Display` is the drawn prefix (`# ` for a heading, which the layout
does not draw); typing into an empty document writes that marker and the escaped text, and the result must be one block showing exactly the typed
text. Known limits: Enter at a line's start or end inside a paragraph (after or before a hard break) is refused; Backspace in a nested first item is
refused (only a top-level item unwraps into a paragraph before its list); Backspace in a quote unwraps only a block whose only container is the quote;
a pending block made from a removed block is verified leniently (the other blocks must be unchanged and the new block must show exactly the typed
text), since its shape is not known from its source at typing time; a multi-line selection removes everything between its first and last cell, so
emphasis that spans the edges may be refused; Enter on a pending paragraph that is not in a quote and not at the top level acts at the caret instead of
ending anything; Enter inside a link's text is refused (the split would cut the link, which would need closing it and opening it again in
the new block); a code line has no caret stop at its end, so Delete cannot join code lines (Backspace at the next line's start does). The view must draw an Above pending above the caret's line and a Below pending below it, use `Display` for both and for the held
marker of an empty document, and pass the pending back on the next insertion at the same `Offset`.

### Tables, paste and cross-block selections (M6c)

M6c keeps the engine's shape (pure functions, one replacement, a reparse and a verification, a refusal otherwise).

- **Typing in a table cell.** A typed character, Backspace or Delete in a cell edits the cell's inline source as typing
  in a paragraph does (the insertion point, continuation, escapes, with `|` escaped as `\|` too), and the new cell source
  is written with `TableEdit`'s cell writer, so an aligned table stays aligned (the whole table is the replacement) and a
  minimal one changes only the cell. The caret goes to the place after the typed text in the rewritten cell, found by
  scanning the new table source. A space that would not show at a cell's edge is held as an `Inline` pending. Backspace
  at a cell's start and Delete at its end do nothing (cells are not joined). A selection inside one cell is replaced; one
  across cells is refused. Verification is `TableEdit`'s plus the M6a check of the cell's plain text.
- **Enter in a table cell** moves the caret to the same column of the next row; on the last row it inserts a row below
  (`TableEdit.InsertRow`) and moves there, as one undo step. F2 still opens the inline field.
- **Paste** inserts Markdown source (the user's decision). `ViewEdit.Paste(doc, start, end, text)` removes the selection
  (as Backspace over it would, across blocks too) and writes the text at the insertion point, unescaped: its line breaks
  become the document's line ending, and each line after the first gets the container continuation of the caret's
  block (quote bars, item indentation), so a paste inside a quote or item stays in it. In a code block the text is raw
  with the same prefixes. In a table cell only a one-line paste is accepted, with `|` escaped; a multi-line one is
  refused. A pending insertion is written in front of it. Verification: the blocks wholly before and wholly after the
  pasted range must be unchanged; what the paste itself renders is not judged.
- **Delete across blocks.** `ReplaceSelection` (and Backspace / Delete with a selection) across blocks removes the
  selected source: blocks wholly inside go with their separators; the first and last blocks keep their text outside the
  selection and are joined into the first block's kind and containers, as Backspace at a block's start joins. A selection
  that ends inside a code block or a table without covering it wholly is refused. Typed text goes where the selection
  started. Verification expects the blocks before, the joined block, and the blocks after.
- **Keys.** Pastes come from the terminal's bracketed paste (the driver's paste event) and from Ctrl+V, which reads the
  application clipboard where the driver has one (Windows; on Linux only when a clipboard tool is present). Ctrl+X copies
  the selection as Markdown and deletes it, as one undo step, so that cut and paste round-trip. Each paste or cut is one
  undo step and ends a typing run.
