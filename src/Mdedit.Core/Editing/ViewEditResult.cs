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
    /// <param name="Then">A pending insertion the view holds at <paramref name="Caret"/> after the edit, or null. Used when the edit removes the last character of a block.</param>
    public sealed record Replacement(int Start, int Length, string Text, int Caret, Pending? Then = null) : ViewEditResult;

    /// <summary>
    /// Typed text that would not show, or a new block the view draws before writing it. Nothing is written. The view keeps the result, draws
    /// <see cref="Display"/> on the caret's line (for <see cref="PendingPlacement.Inline"/>, the source text itself), and passes it back as the
    /// <c>pending</c> argument of the next insertion at the same <see cref="Offset"/>. Any other command, or a caret move, drops it.
    /// </summary>
    /// <param name="Offset">The caret's source offset the pending is held at.</param>
    /// <param name="SourceText">
    /// The source text the next typed character writes. For <see cref="PendingPlacement.Above"/> it is written before the typed text; for the
    /// other placements, in front of it.
    /// </param>
    /// <param name="Placement">Where the pending block is drawn relative to the caret's line.</param>
    /// <param name="Display">The text drawn before the caret on the pending's line. Null means the view draws <paramref name="SourceText"/>.</param>
    /// <param name="Tail">The source text the next typed character writes after its own text, for <see cref="PendingPlacement.Above"/>; empty otherwise.</param>
    public sealed record Pending(int Offset, string SourceText, PendingPlacement Placement = PendingPlacement.Inline, string? Display = null, string Tail = "") : ViewEditResult
    {
        /// <summary>The text the view draws before the caret on the pending's line.</summary>
        public string DisplayText => Display ?? SourceText;
    }

    /// <summary>An edit that is not made, with the message to show.</summary>
    /// <param name="Message">The message to show.</param>
    public sealed record Refusal(string Message) : ViewEditResult;
}

/// <summary>Where a pending insertion is drawn: on the caret's line, or on a new line below or above it.</summary>
public enum PendingPlacement
{
    /// <summary>On the caret's line, at the caret (a held space).</summary>
    Inline,

    /// <summary>On a new line below the caret's line (a new block after the caret's block).</summary>
    Below,

    /// <summary>On a new line above the caret's line (a new block before the caret's block).</summary>
    Above,
}
