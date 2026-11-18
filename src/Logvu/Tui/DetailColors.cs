using Logvu.Core.Config;
using Logvu.Core.Detail;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Logvu.Tui;

/// <summary>
/// The attributes of the styled spans of a detail: foregrounds on the normal background, so they read on dark and light terminals,
/// and the highlight styles of the configuration. Pure: no views or drivers involved.
/// </summary>
internal static class DetailColors
{
    // The 16 named ANSI colors by index, as the color names of the terminal theme map them (the same table as the output view's).
    private static readonly ColorName16[] Named16 =
    [
        ColorName16.Black,
        ColorName16.Red,
        ColorName16.Green,
        ColorName16.Yellow,
        ColorName16.Blue,
        ColorName16.Magenta,
        ColorName16.Cyan,
        ColorName16.Gray,
        ColorName16.DarkGray,
        ColorName16.BrightRed,
        ColorName16.BrightGreen,
        ColorName16.BrightYellow,
        ColorName16.BrightBlue,
        ColorName16.BrightMagenta,
        ColorName16.BrightCyan,
        ColorName16.White,
    ];

    /// <summary>
    /// The attribute of a span of a line, on the normal background. A highlight span takes the color of its rule's style (see
    /// <see cref="Highlight"/>); any other span takes the attribute of its <see cref="SpanStyle"/> (see <see cref="For(SpanStyle, Attribute)"/>).
    /// </summary>
    /// <param name="span">The span.</param>
    /// <param name="normal">The normal attribute of the view.</param>
    /// <param name="config">The configuration, which holds the styles that highlight rules name.</param>
    public static Attribute For(StyledSpan span, Attribute normal, LogvuConfig config) =>
        span.Style == SpanStyle.Highlight && span.HighlightStyle is { } name
            ? Highlight(name, normal, config)
            : For(span.Style, normal);

    /// <summary>
    /// The attribute of a structural span. Labels and names are bold, exception types and messages are bold red, methods are bold,
    /// file locations are cyan, directories and framework frames are dimmed, and JSON members, strings, numbers and literals each have a
    /// color. Fold markers are dim italic and muted text is dim. Plain text and punctuation keep the normal attribute.
    /// </summary>
    /// <param name="style">The span's style.</param>
    /// <param name="normal">The normal attribute of the view.</param>
    public static Attribute For(SpanStyle style, Attribute normal)
    {
        Color fg = normal.Foreground;
        Color bg = normal.Background;
        return style switch
        {
            SpanStyle.Label => new Attribute(fg, bg, TextStyle.Bold),
            SpanStyle.FieldName => new Attribute(fg, bg, TextStyle.Bold),
            SpanStyle.ExceptionType => new Attribute(ColorName16.BrightRed, bg, TextStyle.Bold),
            SpanStyle.ExceptionMessage => new Attribute(ColorName16.Red, bg, TextStyle.Bold),
            SpanStyle.Method => new Attribute(fg, bg, TextStyle.Bold),
            SpanStyle.FileLocation => new Attribute(ColorName16.Cyan, bg, TextStyle.None),
            SpanStyle.Directory => new Attribute(fg, bg, TextStyle.Faint),
            SpanStyle.Framework => new Attribute(fg, bg, TextStyle.Faint),
            SpanStyle.JsonKey => new Attribute(ColorName16.BrightBlue, bg, TextStyle.None),
            SpanStyle.JsonString => new Attribute(ColorName16.Green, bg, TextStyle.None),
            SpanStyle.JsonNumber => new Attribute(ColorName16.Magenta, bg, TextStyle.None),
            SpanStyle.JsonLiteral => new Attribute(ColorName16.Yellow, bg, TextStyle.None),
            SpanStyle.FoldMarker => new Attribute(fg, bg, TextStyle.Faint | TextStyle.Italic),
            SpanStyle.Muted => new Attribute(fg, bg, TextStyle.Faint),
            _ => normal,
        };
    }

    /// <summary>
    /// The attribute of a highlight rule's style: a built-in style name (<c>error</c> red, <c>warning</c> yellow, <c>info</c> cyan,
    /// <c>debug</c> dim, <c>accent</c> magenta, <c>emphasis</c> bold, <c>dim</c> dim, <c>success</c> green), or else a style of the
    /// configuration. Names are matched ignoring case.
    /// </summary>
    /// <param name="name">The style name of the rule.</param>
    /// <param name="normal">The normal attribute of the view.</param>
    /// <param name="config">The configuration.</param>
    /// <returns>The normal attribute when the name is neither built in nor configured.</returns>
    public static Attribute Highlight(string name, Attribute normal, LogvuConfig config)
    {
        Color fg = normal.Foreground;
        Color bg = normal.Background;
        switch (name.ToLowerInvariant())
        {
            case "error":
                return new Attribute(ColorName16.Red, bg, TextStyle.None);
            case "warning":
                return new Attribute(ColorName16.Yellow, bg, TextStyle.None);
            case "info":
                return new Attribute(ColorName16.Cyan, bg, TextStyle.None);
            case "debug":
            case "dim":
                return new Attribute(fg, bg, TextStyle.Faint);
            case "accent":
                return new Attribute(ColorName16.Magenta, bg, TextStyle.None);
            case "emphasis":
                return new Attribute(fg, bg, TextStyle.Bold);
            case "success":
                return new Attribute(ColorName16.Green, bg, TextStyle.None);
        }

        foreach (KeyValuePair<string, StyleSpec> configured in config.Styles)
        {
            if (string.Equals(configured.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return FromSpec(configured.Value, normal);
            }
        }

        return normal;
    }

    /// <summary>
    /// The attribute of a configured style: its colors where it sets them (the normal ones where it does not), and its bold, italic and
    /// underline flags.
    /// </summary>
    /// <param name="spec">The style.</param>
    /// <param name="normal">The normal attribute of the view.</param>
    public static Attribute FromSpec(StyleSpec spec, Attribute normal)
    {
        Color fg = spec.Foreground is { } foreground ? ToColor(foreground) : normal.Foreground;
        Color bg = spec.Background is { } background ? ToColor(background) : normal.Background;
        TextStyle style = TextStyle.None;
        if (spec.Bold)
        {
            style |= TextStyle.Bold;
        }

        if (spec.Italic)
        {
            style |= TextStyle.Italic;
        }

        if (spec.Underline)
        {
            style |= TextStyle.Underline;
        }

        return new Attribute(fg, bg, style);
    }

    /// <summary>
    /// The attribute of a span on the cursor line: the focus colors, as the timeline's selected row uses them, so the text reads on the
    /// selection background. The bold, italic and underline of the span are kept; faint is not, since it is dim on the selection.
    /// </summary>
    /// <param name="attr">The span's attribute.</param>
    /// <param name="focus">The focus attribute of the view.</param>
    public static Attribute Selected(Attribute attr, Attribute focus) =>
        new(focus.Foreground, focus.Background, attr.Style & ~TextStyle.Faint);

    /// <summary>The color of a configured color: a named ANSI color, or the RGB color.</summary>
    private static Color ToColor(LogColor color)
    {
        if (color.Named is { } named)
        {
            return Named16[(int)named];
        }

        RgbColor rgb = color.Rgb!.Value;
        return new Color(rgb.R, rgb.G, rgb.B);
    }
}
