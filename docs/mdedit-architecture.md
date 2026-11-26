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

### Cache

`LayoutCache` keeps `BlockLayout`s keyed by (the block's source text, its container chain, its `Tight` and
`IsFirstInItem`, the width). A resize computes each block once; an edit changes the text of a few blocks, and every other
block's layout is reused (its offsets are relative, so they are still right after shifting by the block's new `Start`).
The cache is bounded (least recently used, 4,096 entries).

### Tests

Exact expected display lines (`TextOf`) and exact offsets for every row of the inline table and every block kind; the
source map both ways, including each unmapped case; wrapping with CJK (width 2), combining marks, ZWJ emoji, a word longer
than the line and a width smaller than the prefix; CRLF sources; a property test over the nasty-input corpus and the
repository's own Markdown files: every mapped cell's offset holds that cell's first character in the source, or the `&` of
an entity, and `PositionOf(OffsetAt(l, c))` returns a cell on line `l` for every mapped cell.
