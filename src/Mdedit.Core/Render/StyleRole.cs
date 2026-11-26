namespace Mdedit.Core.Render;

/// <summary>
/// What a cell is for, which decides how the layout draws it. <see cref="Body"/> is ordinary text. The others are either
/// content with its own look (headings, code, HTML) or a synthetic cell that only the layout emits (bars, markers, glyphs,
/// borders), so that the theme can dim or colour each kind without looking at the text.
/// </summary>
public enum StyleRole
{
    /// <summary>Ordinary text of a paragraph, list item, quote or table cell.</summary>
    Body,

    /// <summary>Text of a heading. The heading level is in <see cref="CellStyle.HeadingLevel"/>.</summary>
    Heading,

    /// <summary>A source character of a code block.</summary>
    CodeBlock,

    /// <summary>The synthetic header line of a code block, which shows its language. The layout draws it.</summary>
    CodeHeader,

    /// <summary>The synthetic bar that a quote or alert puts before each of its lines.</summary>
    QuoteBar,

    /// <summary>The synthetic bullet or the source number and delimiter of a list item.</summary>
    ListMarker,

    /// <summary>The <c>[ ]</c> or <c>[x]</c> of a task list item, as written.</summary>
    TaskBox,

    /// <summary>The synthetic rule that a thematic break is drawn with.</summary>
    ThematicBreak,

    /// <summary>Inline or block HTML, as written. Shown dim, so that it does not read as text.</summary>
    Html,

    /// <summary>The synthetic <c>[image] </c> that stands for an image before its alt text.</summary>
    ImageMarker,

    /// <summary>The synthetic title line of an alert (its kind).</summary>
    AlertTitle,

    /// <summary>The synthetic borders of a table grid: its rules, corners and the bars between cells.</summary>
    TableBorder,

    /// <summary>The synthetic dim arrow that starts a continuation row of a wrapped code line.</summary>
    WrapMarker,

    /// <summary>The synthetic glyph before a heading's text that folds it.</summary>
    FoldGlyph,

    /// <summary>The synthetic line that stands for a folded block.</summary>
    FoldSummary,
}
