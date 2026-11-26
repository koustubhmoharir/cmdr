namespace Mdedit.Core.Render;

/// <summary>The kinds of leaf block that <see cref="RenderBlock"/> can be.</summary>
public enum BlockKind
{
    /// <summary>A paragraph: wrapped text, one run per hard-break-separated line.</summary>
    Paragraph,

    /// <summary>An ATX or setext heading. Its runs are its inline content, with the heading role.</summary>
    Heading,

    /// <summary>A fenced or indented code block: one run per source line.</summary>
    Code,

    /// <summary>A table. Lowered as its source lines, like an HTML block, until tables are drawn as grids.</summary>
    Table,

    /// <summary>A thematic break. It has no runs: the layout draws the rule.</summary>
    ThematicBreak,

    /// <summary>An HTML block: one run per source line, shown dim as written.</summary>
    Html,

    /// <summary>
    /// A block mdedit has no rendering for (link reference definitions, for one), shown as its source lines, one run per
    /// line, in the HTML role so that it is drawn dim as written.
    /// </summary>
    Raw,
}
