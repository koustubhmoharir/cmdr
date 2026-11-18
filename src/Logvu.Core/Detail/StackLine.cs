namespace Logvu.Core.Detail;

/// <summary>The role of a line of a stack trace.</summary>
public enum StackLineKind
{
    /// <summary>An exception header: <c>Namespace.TypeException: message</c>, possibly after <c>---&gt; </c>.</summary>
    ExceptionHeader,

    /// <summary>A stack frame.</summary>
    Frame,

    /// <summary>The <c>--- End of inner exception stack trace ---</c> marker.</summary>
    EndOfInner,

    /// <summary>The <c>--- End of stack trace from previous location ---</c> marker.</summary>
    EndOfStack,

    /// <summary>Any other line.</summary>
    Other,
}

/// <summary>A classified line of a stack trace, with the spans that style it.</summary>
/// <param name="Kind">The role of the line.</param>
/// <param name="IsFramework">Whether the line is a frame whose method starts with a configured framework prefix.</param>
/// <param name="Text">The line as given.</param>
/// <param name="Spans">The styled spans of <paramref name="Text"/>: ordered, non-overlapping and inside the text.</param>
public sealed record StackLine(StackLineKind Kind, bool IsFramework, string Text, IReadOnlyList<StyledSpan> Spans);
