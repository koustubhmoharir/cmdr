using Mdedit.Core.Render;
using Terminal.Gui.Drawing;

namespace Mdedit.Tui;

/// <summary>
/// The code colouring of the viewer: the fork's <see cref="TextMateSyntaxHighlighter"/> adapted to <see cref="ICodeColorizer"/>. Each line
/// of the code is highlighted in turn with the tokenizer state carried from the line before, and the segments' VisualRoles become
/// <see cref="CodeToken"/>s by their lengths over the code. The highlighter is built once for the application (see <see cref="Shared"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Languages.</b> The canonical ids of <see cref="CodeLanguages.Normalize"/> are the grammars' language ids, except <c>shell</c>, which the
/// grammar calls <c>shellscript</c>. A language with no grammar, or a platform without the native Oniguruma library (the highlighter then
/// returns one plain segment), gives null.
/// </para>
/// <para>
/// <b>Tags.</b> The fork maps markup tags (<c>meta.tag</c>) to <see cref="VisualRole.CodeAttribute"/>, so an HTML tag name is an
/// <see cref="CodeToken.Attribute"/> token; there is no separate tag role in the fork.
/// </para>
/// </remarks>
internal sealed class TextMateColorizer : ICodeColorizer
{
    private static readonly Lazy<TextMateColorizer> Instance = new(() => new TextMateColorizer());

    private readonly TextMateSyntaxHighlighter _highlighter = new();

    /// <summary>The colorizer of the application: created on first use, then shared by every document (the highlighter keeps no per-document state).</summary>
    public static TextMateColorizer Shared => Instance.Value;

    /// <summary>
    /// The spans of a code block: each line is highlighted after the lines before it, with the tokenizer state reset at the first line. A
    /// line's span starts at its offset in the joined code (lines are separated by one line feed).
    /// </summary>
    /// <param name="code">The code: its lines joined with line feeds.</param>
    /// <param name="language">The canonical language id, from <see cref="CodeLanguages.Normalize"/>.</param>
    /// <returns>The spans, or null when the language has no colouring (no grammar, or no native library).</returns>
    public IReadOnlyList<CodeSpan>? Colorize(string code, string language)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(language);

        _highlighter.ResetState();
        var spans = new List<CodeSpan>();
        int start = 0;
        foreach (string line in code.Split('\n'))
        {
            AddSpans(spans, _highlighter.Highlight(line, GrammarOf(language)), start);
            start += line.Length + 1;
        }

        return spans.Count == 0 ? null : spans;
    }

    /// <summary>The language id the highlighter looks a grammar up by, for a canonical id: the same, except for the ids the grammars name otherwise.</summary>
    /// <param name="language">The canonical language id.</param>
    internal static string GrammarOf(string language) => language == "shell" ? "shellscript" : language;

    /// <summary>
    /// Adds the spans of one line's segments, which start at <paramref name="start"/> in the code. Each segment covers its text's length of
    /// characters, in order; a segment without a token adds nothing, and an empty one is skipped.
    /// </summary>
    /// <param name="spans">The list to add to.</param>
    /// <param name="segments">The segments of the line, in order.</param>
    /// <param name="start">The index in the code of the line's first character.</param>
    internal static void AddSpans(List<CodeSpan> spans, IEnumerable<StyledSegment> segments, int start)
    {
        int at = start;
        foreach (StyledSegment segment in segments)
        {
            int length = segment.Text.Length;
            CodeToken token = TokenOf(segment.Role);
            if (length > 0 && token != CodeToken.None)
            {
                spans.Add(new CodeSpan(at, length, token));
            }

            at += length;
        }
    }

    /// <summary>The token of a highlighter role. Plain code and roles that are not code are <see cref="CodeToken.None"/>.</summary>
    /// <param name="role">The role of a segment, or null.</param>
    internal static CodeToken TokenOf(VisualRole? role) => role switch
    {
        VisualRole.CodeKeyword => CodeToken.Keyword,
        VisualRole.CodeString => CodeToken.String,
        VisualRole.CodeNumber => CodeToken.Number,
        VisualRole.CodeComment => CodeToken.Comment,
        VisualRole.CodeType => CodeToken.Type,
        VisualRole.CodeFunctionName => CodeToken.Function,
        VisualRole.CodeOperator => CodeToken.Operator,
        VisualRole.CodePunctuation => CodeToken.Punctuation,
        VisualRole.CodePreprocessor => CodeToken.Preprocessor,
        VisualRole.CodeConstant => CodeToken.Constant,
        VisualRole.CodeAttribute => CodeToken.Attribute,
        VisualRole.CodeIdentifier => CodeToken.Identifier,
        _ => CodeToken.None,
    };
}
