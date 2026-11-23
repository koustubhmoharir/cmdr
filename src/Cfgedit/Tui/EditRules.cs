using Cfgedit.Core.Model;
using Terminal.Gui.Views;

// TextView is marked obsolete in Terminal.Gui 2.5.0; the multi-line editor and its round trip use it as cmdr's dialogs do.
#pragma warning disable CS0618

namespace Cfgedit.Tui;

/// <summary>What a focused part does on Enter or F2.</summary>
internal enum EditorKind
{
    /// <summary>The part cannot be edited in the tree.</summary>
    None,

    /// <summary>The single-line field, over the part.</summary>
    Inline,

    /// <summary>The multi-line editor dialog.</summary>
    Multiline,

    /// <summary>
    /// A JSON true, false or null: Enter and F2 open the type menu, with the current type chosen. Enter never changes a value directly.
    /// </summary>
    Type,
}

/// <summary>
/// Which editor a part opens with, decided by a round trip through the Terminal.Gui text controls. A value is edited inline only
/// when a <see cref="TextField"/> holds it exactly, and in the multi-line editor only when a <see cref="TextView"/> does. Pure apart
/// from building the two controls.
/// </summary>
internal static class EditRules
{
    /// <summary>The message for a part that cannot be edited at all, such as an index or a summary.</summary>
    public const string CannotEdit = "this part cannot be edited";

    /// <summary>The message for a value that neither text control holds exactly, such as one with a lone CR.</summary>
    public const string TextModeOnly = "this value can only be edited in text mode (Ctrl+T)";


    /// <summary>
    /// True when a single-line field holds the text exactly: setting it and reading it back gives the same text, and it has no line break.
    /// A field cannot show a line break, so a CR or LF is read-only here even where the control keeps it.
    /// </summary>
    /// <param name="text">The text.</param>
    public static bool CanEditInline(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.IndexOfAny(LineBreaks) >= 0)
        {
            return false;
        }

        var field = new TextField { Text = text };
        return field.Text == text;
    }

    /// <summary>
    /// True when the multi-line editor holds the text exactly: setting it and reading it back gives the same text, and no CR stands alone.
    /// The control keeps a lone CR as text, but the terminal reads it as a line break, so the value is read-only. A CR LF pair is not
    /// kept by the control (it reads back as LF), so such a value fails the round trip.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <summary>
    /// True when the multi-line editor can show the value and give it back unchanged. A value with any CR cannot be: the
    /// editor joins its lines with <see cref="Environment.NewLine"/> (CRLF on Windows), so its line breaks are read back as
    /// LF (see <see cref="TextOf"/>), and a CR the user did not type would be lost or doubled. Such values are edited in
    /// text mode. The rule is the same on every platform.
    /// </summary>
    public static bool CanEditMultiline(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Contains('\r'))
        {
            return false;
        }

        var area = new TextView { Multiline = true, TabKeyAddsTab = false, Text = text };
        return TextOf(area) == text;
    }

    /// <summary>The multi-line editor's text with its line breaks as LF, whatever <see cref="Environment.NewLine"/> is.</summary>
    public static string TextOf(TextView area)
    {
        ArgumentNullException.ThrowIfNull(area);
        return WithLineFeeds(area.Text, Environment.NewLine);
    }

    /// <summary>
    /// <paramref name="text"/>, as joined with <paramref name="newline"/> by the editor, with each of those line breaks as LF.
    /// Separate from <see cref="TextOf"/> so that the Windows case can be tested anywhere.
    /// </summary>
    internal static string WithLineFeeds(string text, string newline) =>
        newline == "\n" ? text : text.Replace(newline, "\n", StringComparison.Ordinal);

    private static readonly char[] LineBreaks = ['\r', '\n'];

    /// <summary>The editor a node's part opens with.</summary>
    /// <param name="node">The node the part belongs to.</param>
    /// <param name="part">The part.</param>
    public static EditorKind KindOf(DocNode node, DocPart part)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(part);
        if (!part.Editable)
        {
            return EditorKind.None;
        }

        if (part.Kind == PartKind.Value)
        {
            if (node.JsonKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null)
            {
                return EditorKind.Type;
            }
        }

        // A TOML key is one line: a quoted key with a line break has no single-line field, so it is not edited in the tree.
        if (part.Kind == PartKind.Key && node.Kind is NodeKind.TomlTable or NodeKind.TomlArray or NodeKind.TomlScalar)
        {
            return CanEditInline(part.Value) ? EditorKind.Inline : EditorKind.None;
        }

        if (CanEditInline(part.Value))
        {
            return EditorKind.Inline;
        }

        return CanEditMultiline(part.Value) ? EditorKind.Multiline : EditorKind.None;
    }

    /// <summary>The message shown when Enter or F2 is pressed on a part that has no editor.</summary>
    /// <param name="part">The part.</param>
    public static string RefusalOf(DocPart part)
    {
        ArgumentNullException.ThrowIfNull(part);
        return part.Editable ? TextModeOnly : CannotEdit;
    }
}
