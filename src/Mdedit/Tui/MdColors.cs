using Mdedit.Core.Render;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Mdedit.Tui;

/// <summary>
/// The look of the rendered text: each cell's style as a Terminal.Gui attribute. Headings are bold, coloured by level; emphasis,
/// strong and strikethrough are styles; code is on a dark background; links are underlined; quote bars, borders, fold glyphs,
/// wrap markers, rules and raw HTML are dim. Pure: the view's normal attribute is passed in, and only its foreground is replaced.
/// </summary>
/// <remarks>
/// The colours are for dark terminal backgrounds. A code block's cells take the colour of their token (see <see cref="ForToken"/>); the
/// cells without a token, and the inline code spans, have the code colour.
/// </remarks>
internal static class MdColors
{
    /// <summary>The colour of a level 1 heading.</summary>
    public static readonly Color HeadingOne = new(0xE5, 0xC0, 0x7B);

    /// <summary>The colour of a level 2 heading.</summary>
    public static readonly Color HeadingTwo = new(0x61, 0xAF, 0xEF);

    /// <summary>The colour of a level 3 heading.</summary>
    public static readonly Color HeadingThree = new(0x98, 0xC3, 0x79);

    /// <summary>The colour of a level 4 to 6 heading.</summary>
    public static readonly Color HeadingOther = new(0xAB, 0xB2, 0xBF);

    /// <summary>The background of code spans and code blocks.</summary>
    public static readonly Color CodeBackground = new(0x2C, 0x31, 0x3C);

    /// <summary>The text colour of code spans and code blocks.</summary>
    public static readonly Color CodeText = new(0xD7, 0xDA, 0xE0);

    /// <summary>The colour of link text.</summary>
    public static readonly Color Link = new(0x61, 0xAF, 0xEF);

    /// <summary>The colour of an image's alt text.</summary>
    public static readonly Color Image = new(0xC6, 0x78, 0xDD);

    /// <summary>The accent colour: list markers, task boxes and the image marker.</summary>
    public static readonly Color Accent = new(0x56, 0xB6, 0xC2);

    /// <summary>The dimmed colour: quote bars, table borders, fold glyphs, wrap markers, rules, raw HTML and fold summaries.</summary>
    public static readonly Color Dim = new(0x5C, 0x63, 0x70);

    /// <summary>The colour of a NOTE alert title.</summary>
    public static readonly Color AlertNote = new(0x61, 0xAF, 0xEF);

    /// <summary>The colour of a TIP alert title.</summary>
    public static readonly Color AlertTip = new(0x98, 0xC3, 0x79);

    /// <summary>The colour of an IMPORTANT alert title.</summary>
    public static readonly Color AlertImportant = new(0xC6, 0x78, 0xDD);

    /// <summary>The colour of a WARNING alert title.</summary>
    public static readonly Color AlertWarning = new(0xE5, 0xC0, 0x7B);

    /// <summary>The colour of a CAUTION alert title.</summary>
    public static readonly Color AlertCaution = new(0xE0, 0x6C, 0x75);

    /// <summary>The colour of a keyword token.</summary>
    public static readonly Color TokenKeyword = new(0xC6, 0x78, 0xDD);

    /// <summary>The colour of a string token.</summary>
    public static readonly Color TokenString = new(0x98, 0xC3, 0x79);

    /// <summary>The colour of a number token.</summary>
    public static readonly Color TokenNumber = new(0xD1, 0x9A, 0x66);

    /// <summary>The colour of a comment token, which is also italic.</summary>
    public static readonly Color TokenComment = new(0x7F, 0x86, 0x93);

    /// <summary>The colour of a type token.</summary>
    public static readonly Color TokenType = new(0xE5, 0xC0, 0x7B);

    /// <summary>The colour of a function name token.</summary>
    public static readonly Color TokenFunction = new(0x61, 0xAF, 0xEF);

    /// <summary>The colour of an operator token.</summary>
    public static readonly Color TokenOperator = new(0x56, 0xB6, 0xC2);

    /// <summary>The colour of a punctuation token.</summary>
    public static readonly Color TokenPunctuation = new(0xAB, 0xB2, 0xBF);

    /// <summary>The colour of a preprocessor directive token.</summary>
    public static readonly Color TokenPreprocessor = new(0xE0, 0x6C, 0x75);

    /// <summary>The colour of a constant token (literals such as <c>true</c> and <c>null</c>).</summary>
    public static readonly Color TokenConstant = new(0xD1, 0x9A, 0x66);

    /// <summary>The colour of an attribute or markup tag token.</summary>
    public static readonly Color TokenAttribute = new(0x7F, 0xD1, 0xAE);

    /// <summary>The colour of a message that reports an error.</summary>
    public static readonly Color Error = new(0xD7, 0x5F, 0x5F);

    /// <summary>
    /// The attribute of a cell with the given style, on the view's normal background. The foreground is the style's colour, or the
    /// normal foreground for the styles that have none.
    /// </summary>
    /// <param name="style">The cell's style.</param>
    /// <param name="normal">The view's normal attribute.</param>
    public static Attribute For(CellStyle style, Attribute normal)
    {
        Color foreground = normal.Foreground;
        Color background = normal.Background;
        TextStyle text = TextStyle.None;

        switch (style.Role)
        {
            case StyleRole.CodeBlock or StyleRole.CodeHeader:
                background = CodeBackground;
                foreground = CodeText;
                if (style.Role == StyleRole.CodeBlock && ForToken(style.Token) is { } token)
                {
                    foreground = token;
                    if (style.Token == CodeToken.Comment)
                    {
                        text |= TextStyle.Italic;
                    }
                }

                break;
            case StyleRole.QuoteBar or StyleRole.TableBorder or StyleRole.FoldGlyph or StyleRole.WrapMarker
                or StyleRole.ThematicBreak or StyleRole.Html or StyleRole.FoldSummary:
                foreground = Dim;
                text |= TextStyle.Faint;
                break;
            case StyleRole.ListMarker or StyleRole.TaskBox or StyleRole.ImageMarker:
                foreground = Accent;
                break;
        }

        if (style.HeadingLevel > 0)
        {
            foreground = HeadingColor(style.HeadingLevel);
            text |= TextStyle.Bold;
        }

        if ((style.Inline & InlineStyle.Code) != 0)
        {
            background = CodeBackground;
            foreground = CodeText;
        }

        if ((style.Inline & InlineStyle.Link) != 0)
        {
            foreground = Link;
            text |= TextStyle.Underline;
        }

        if ((style.Inline & InlineStyle.Image) != 0)
        {
            foreground = Image;
            text |= TextStyle.Italic;
        }

        if ((style.Inline & InlineStyle.Emphasis) != 0)
        {
            text |= TextStyle.Italic;
        }

        if ((style.Inline & InlineStyle.Strong) != 0)
        {
            text |= TextStyle.Bold;
        }

        if ((style.Inline & InlineStyle.Strikethrough) != 0)
        {
            text |= TextStyle.Strikethrough;
        }

        return new Attribute(foreground, background, text);
    }

    /// <summary>
    /// The colour of a code token, or null for a token that has none of its own (<see cref="CodeToken.None"/> and the identifiers, which keep
    /// the code colour).
    /// </summary>
    /// <param name="token">The token of a code cell.</param>
    public static Color? ForToken(CodeToken token) => token switch
    {
        CodeToken.Keyword => TokenKeyword,
        CodeToken.String => TokenString,
        CodeToken.Number => TokenNumber,
        CodeToken.Comment => TokenComment,
        CodeToken.Type => TokenType,
        CodeToken.Function => TokenFunction,
        CodeToken.Operator => TokenOperator,
        CodeToken.Punctuation => TokenPunctuation,
        CodeToken.Preprocessor => TokenPreprocessor,
        CodeToken.Constant => TokenConstant,
        CodeToken.Attribute or CodeToken.Tag => TokenAttribute,
        _ => null,
    };

    /// <summary>
    /// The attribute of an alert title, bold, in the colour of its kind. The title is the text of the alert's title cells (<c>Note</c>,
    /// <c>Tip</c>, <c>Important</c>, <c>Warning</c> or <c>Caution</c>); any other text takes the normal foreground.
    /// </summary>
    /// <param name="title">The title text.</param>
    /// <param name="normal">The view's normal attribute.</param>
    public static Attribute ForAlert(string title, Attribute normal)
    {
        Color foreground = title switch
        {
            "Note" => AlertNote,
            "Tip" => AlertTip,
            "Important" => AlertImportant,
            "Warning" => AlertWarning,
            "Caution" => AlertCaution,
            _ => normal.Foreground,
        };
        return new Attribute(foreground, normal.Background, TextStyle.Bold);
    }

    /// <summary>The colour of a heading of a level: level 1 and 2 are the strongest, level 4 to 6 share one colour.</summary>
    /// <param name="level">The level, 1 to 6.</param>
    public static Color HeadingColor(int level) => level switch
    {
        1 => HeadingOne,
        2 => HeadingTwo,
        3 => HeadingThree,
        _ => HeadingOther,
    };
}
