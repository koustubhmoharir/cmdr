using Cfgedit.Core.Model;
using Shared.Tui;
using Terminal.Gui.Views;

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
/// Which editor a part opens with. A value is edited inline only when a <see cref="TextField"/> holds it exactly (decided by a round
/// trip through the field), and in the multi-line editor only when it has no CR (see <see cref="CanEditMultiline"/>). Pure apart from
/// building the field.
/// </summary>
internal static class EditRules
{
    /// <summary>The message for a part that cannot be edited at all, such as an index or a summary.</summary>
    public const string CannotEdit = "this part cannot be edited";

    /// <summary>The message for a value that neither editor can show, such as one with a CR.</summary>
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
    /// True when the multi-line editor can show the value and give it back unchanged: the value has no CR. The editor keeps its text
    /// as it is, but a terminal shows a CR as a line break, so a CR in the value would be drawn as one; such a value is edited in text
    /// mode instead. The rule is the same on every platform.
    /// </summary>
    /// <param name="text">The text.</param>
    public static bool CanEditMultiline(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return !text.Contains('\r');
    }

    /// <summary>
    /// The multi-line editor's text, as it is. The editor's line break is LF, so Enter and a paste write only LF, and the text needs no
    /// conversion before it is written back.
    /// </summary>
    /// <param name="area">The text area of the editor.</param>
    public static string TextOf(TextArea area)
    {
        ArgumentNullException.ThrowIfNull(area);
        return area.Text;
    }

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

        // A TOML or YAML key is one line: a quoted key with a line break has no single-line field, so it is not edited in the tree.
        if (part.Kind == PartKind.Key && node.Kind is NodeKind.TomlTable or NodeKind.TomlArray or NodeKind.TomlScalar
            or NodeKind.YamlMapping or NodeKind.YamlSequence or NodeKind.YamlScalar or NodeKind.YamlAlias)
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
