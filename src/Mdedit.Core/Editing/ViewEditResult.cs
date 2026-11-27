namespace Mdedit.Core.Editing;

/// <summary>
/// The outcome of a rendered-view edit: one replacement of the source text with the caret's offset after it, a pending insertion that the
/// view holds until the next character, or a refusal with the message to show. A refusal changes nothing.
/// </summary>
public abstract record ViewEditResult
{
    /// <summary>
    /// The edit: the source characters from <see cref="Start"/> for <see cref="Length"/> characters are replaced by <see cref="Text"/>. The
    /// replacement covers exactly the changed middle of the text, so the edit is one undo step.
    /// </summary>
    /// <param name="Start">The offset of the replaced text in the source before the edit.</param>
    /// <param name="Length">The number of characters replaced.</param>
    /// <param name="Text">The text that replaces them.</param>
    /// <param name="Caret">The caret's source offset in the source after the edit.</param>
    public sealed record Replacement(int Start, int Length, string Text, int Caret) : ViewEditResult;

    /// <summary>
    /// Typed text that would not show, such as a space at the start or end of a block. Nothing is written. The view keeps the result, draws
    /// <see cref="SourceText"/> at the caret, and passes it back as the <c>pending</c> argument of the next insertion at the same
    /// <see cref="Offset"/>. Any other command, or a caret move, drops it.
    /// </summary>
    /// <param name="Offset">The caret's source offset the text is held at.</param>
    /// <param name="SourceText">The source text waiting to be written, which the next insertion writes in front of its own text.</param>
    public sealed record Pending(int Offset, string SourceText) : ViewEditResult;

    /// <summary>An edit that is not made, with the message to show.</summary>
    /// <param name="Message">The message to show.</param>
    public sealed record Refusal(string Message) : ViewEditResult;
}
