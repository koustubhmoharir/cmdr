using Shared.Tui;
using Terminal.Gui.Views;

namespace Cmdr.Tui;

/// <summary>
/// The original value of a single-line field of a dialog, and the text it shows at the start. A <see cref="TextField"/> cannot
/// hold a line break, so an original that contains CR or LF is read-only and shows its escaped form; accepting the dialog keeps
/// the original. Any other field is edited as text.
/// </summary>
internal sealed class FieldDraft
{
    private static readonly char[] LineBreaks = { '\r', '\n' };

    public FieldDraft(string? original)
    {
        Original = original;
        Editable = original is null || original.IndexOfAny(LineBreaks) < 0;
        Initial = Editable ? original ?? string.Empty : EscapeText.Format(original);
    }

    /// <summary>The value the field was opened with (null stays null).</summary>
    public string? Original { get; }

    /// <summary>False when the original cannot be shown or typed in a single line; the field is then read-only.</summary>
    public bool Editable { get; }

    /// <summary>The text the field shows at the start.</summary>
    public string Initial { get; }

    /// <summary>
    /// The kept original when the field was not edited (or is read-only); null when its text was edited, and the text is then
    /// saved as typed (see <see cref="SavedText"/>).
    /// </summary>
    public KeptValue? KeptIfUnchanged(string shown) =>
        !Editable || shown == Initial ? new KeptValue(Original) : null;
}

/// <summary>
/// The original value of a single-line field, kept as it was: it is saved exactly. A <c>null</c> <see cref="KeptValue"/>? means
/// the field was edited; <c>new KeptValue(null)</c> means the original was null and is kept as null.
/// </summary>
internal readonly record struct KeptValue(string? Value);

/// <summary>How the text of a single-line field is saved. A kept original is saved exactly; edited text follows the rule of its field.</summary>
internal static class SavedText
{
    /// <summary>A required value (name, program): the edited text trimmed.</summary>
    public static string Required(string text, KeptValue? kept) =>
        kept is { } original ? original.Value ?? string.Empty : text.Trim();

    /// <summary>An optional value (working dir, log path): the edited text trimmed, and blank becomes null.</summary>
    public static string? Optional(string text, KeptValue? kept) =>
        kept is { } original ? original.Value : string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>A readiness pattern: the edited text as typed (a regular expression is not trimmed), and blank becomes null.</summary>
    public static string? Pattern(string text, KeptValue? kept) =>
        kept is { } original ? original.Value : string.IsNullOrWhiteSpace(text) ? null : text;
}

/// <summary>The controls shared by the command and group dialogs.</summary>
internal static class DialogControls
{
    public static Label Caption(string text, int x, int y) => new() { Text = text, X = x, Y = y };

    public static TextField Field(int x, int y, int width, string text) =>
        new() { X = x, Y = y, Width = width, Text = text };

    /// <summary>A single-line field showing the draft; read-only when the draft cannot be edited.</summary>
    public static TextField Field(int x, int y, int width, FieldDraft draft) =>
        new() { X = x, Y = y, Width = width, Text = draft.Initial, ReadOnly = !draft.Editable };

    /// <summary>A multi-line area: Enter adds a line, and Tab moves the focus (see <see cref="TextArea"/>).</summary>
    public static TextArea Area(int x, int y, int width, int height, string text) => new()
    {
        X = x,
        Y = y,
        Width = width,
        Height = height,
        Text = text,
    };
}
