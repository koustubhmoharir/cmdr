using System.Text.RegularExpressions;

namespace Logvu.Core.Detail;

/// <summary>
/// Classifies the lines of .NET exception text and the frames of Go structured-error stack arrays. Each input line yields exactly one
/// <see cref="StackLine"/>, in order, so the caller can map every result back to its input line.
/// </summary>
/// <remarks>
/// <para>
/// A .NET exception header is <c>Namespace.TypeException: message</c> (optionally after <c>---&gt; </c>). A header whose type does not end
/// in <c>Exception</c> counts only when it is dotted and the next line is a frame, so an ordinary message such as <c>Note: retry later</c>
/// is never styled as a type.
/// </para>
/// <para>
/// Matching runs with a 250 ms timeout. A line whose match times out is classified as <see cref="StackLineKind.Other"/>.
/// </para>
/// </remarks>
public static partial class StackTraceAnalyzer
{
    private const string InnerEndText = "--- End of inner exception stack trace ---";
    private const string PreviousEndText = "--- End of stack trace from previous location";

    [GeneratedRegex(@"^(?<lead>(?:\s*---> )?)(?<type>[A-Za-z_`][\w`]*(?:\.[A-Za-z_`][\w`]*)*)(?:(?<sep>: )(?<message>.*))?$",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^(?<lead>\s*)at (?<method>[^\s(]+)\((?<args>[^)]*)\)(?: in (?<file>.+?)(?::line (?<line>\d+))?)?\s*$",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex FramePattern();

    [GeneratedRegex(@"^(?<func>.+) \((?<file>[^()]*):(?<line>\d+)\)$",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex GoFramePattern();

    /// <summary>Classifies the lines of a multi-line .NET exception or stack trace.</summary>
    /// <param name="lines">The lines, without their terminators.</param>
    /// <param name="frameworkPrefixes">
    /// The namespace prefixes whose frames are framework frames. A frame is one when its method starts with a prefix (ordinal). An empty
    /// prefix is ignored.
    /// </param>
    /// <returns>One <see cref="StackLine"/> per input line, in order.</returns>
    public static IReadOnlyList<StackLine> Analyze(IReadOnlyList<string> lines, IReadOnlyList<string> frameworkPrefixes)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(frameworkPrefixes);

        var result = new StackLine[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            string? next = i + 1 < lines.Count ? lines[i + 1] : null;
            result[i] = ClassifyDotNet(lines[i], next, frameworkPrefixes);
        }

        return result;
    }

    /// <summary>Classifies the frames of a Go structured error's <c>stack</c> array.</summary>
    /// <remarks>
    /// Each frame has the form <c>pkg.Func (path/file.go:42)</c>, or <c>pkg.(*T).Method (file.go:10)</c>. A frame that does not have that
    /// form is <see cref="StackLineKind.Other"/>.
    /// </remarks>
    /// <param name="frames">The frames, one per array element.</param>
    /// <param name="frameworkPrefixes">The package prefixes whose frames are framework frames (ordinal).</param>
    /// <returns>One <see cref="StackLine"/> per frame, in order.</returns>
    public static IReadOnlyList<StackLine> AnalyzeGoStack(IReadOnlyList<string> frames, IReadOnlyList<string> frameworkPrefixes)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(frameworkPrefixes);

        var result = new StackLine[frames.Count];
        for (int i = 0; i < frames.Count; i++)
        {
            result[i] = ClassifyGo(frames[i], frameworkPrefixes);
        }

        return result;
    }

    private static StackLine ClassifyDotNet(string text, string? next, IReadOnlyList<string> prefixes)
    {
        Match? frame = TryMatch(FramePattern(), text);
        if (frame is not null)
        {
            return FrameLine(frame, text, prefixes);
        }

        string trimmed = text.TrimStart();
        if (trimmed.StartsWith(InnerEndText, StringComparison.Ordinal))
        {
            return Marker(StackLineKind.EndOfInner, text);
        }

        if (trimmed.StartsWith(PreviousEndText, StringComparison.Ordinal))
        {
            return Marker(StackLineKind.EndOfStack, text);
        }

        Match? header = TryMatch(HeaderPattern(), text);
        if (header is not null && IsExceptionHeader(header, next))
        {
            return HeaderLine(header, text);
        }

        return new StackLine(StackLineKind.Other, false, text, []);
    }

    private static bool IsExceptionHeader(Match header, string? next)
    {
        string type = header.Groups["type"].Value;
        int dot = type.LastIndexOf('.');
        string last = type[(dot + 1)..];
        if (last.EndsWith("Exception", StringComparison.Ordinal))
        {
            return true;
        }

        return header.Groups["sep"].Success
            && dot >= 0
            && next is not null
            && TryMatch(FramePattern(), next) is not null;
    }

    private static StackLine HeaderLine(Match match, string text)
    {
        var spans = new List<StyledSpan>();
        Group lead = match.Groups["lead"];
        if (lead.Length > 0)
        {
            spans.Add(new StyledSpan(lead.Index, lead.Length, SpanStyle.Muted));
        }

        Group type = match.Groups["type"];
        spans.Add(new StyledSpan(type.Index, type.Length, SpanStyle.ExceptionType));

        Group sep = match.Groups["sep"];
        if (sep.Success)
        {
            spans.Add(new StyledSpan(sep.Index, sep.Length, SpanStyle.Punctuation));
            Group message = match.Groups["message"];
            if (message.Length > 0)
            {
                spans.Add(new StyledSpan(message.Index, message.Length, SpanStyle.ExceptionMessage));
            }
        }

        return new StackLine(StackLineKind.ExceptionHeader, false, text, spans);
    }

    private static StackLine FrameLine(Match match, string text, IReadOnlyList<string> prefixes)
    {
        Group method = match.Groups["method"];
        if (IsFramework(method.Value, prefixes))
        {
            return new StackLine(StackLineKind.Frame, true, text, [new StyledSpan(0, text.Length, SpanStyle.Framework)]);
        }

        Group lead = match.Groups["lead"];
        var spans = new List<StyledSpan>
        {
            new(lead.Index + lead.Length, 2, SpanStyle.Muted),
            new(method.Index, method.Length, SpanStyle.Method),
        };

        Group args = match.Groups["args"];
        int open = method.Index + method.Length;
        int close = args.Index + args.Length;
        spans.Add(new StyledSpan(open, 1, SpanStyle.Punctuation));
        spans.Add(new StyledSpan(close, 1, SpanStyle.Punctuation));

        Group file = match.Groups["file"];
        if (file.Success)
        {
            spans.Add(new StyledSpan(close + 1, file.Index - (close + 1), SpanStyle.Muted));

            int separator = file.Value.LastIndexOfAny(['\\', '/']);
            int directory = separator + 1;
            if (directory > 0)
            {
                spans.Add(new StyledSpan(file.Index, directory, SpanStyle.Directory));
            }

            Group line = match.Groups["line"];
            int locationStart = file.Index + directory;
            int locationEnd = line.Success ? line.Index + line.Length : file.Index + file.Length;
            spans.Add(new StyledSpan(locationStart, locationEnd - locationStart, SpanStyle.FileLocation));
        }

        return new StackLine(StackLineKind.Frame, false, text, spans);
    }

    private static StackLine ClassifyGo(string text, IReadOnlyList<string> prefixes)
    {
        Match? match = TryMatch(GoFramePattern(), text);
        if (match is null)
        {
            return new StackLine(StackLineKind.Other, false, text, []);
        }

        Group function = match.Groups["func"];
        if (IsFramework(function.Value, prefixes))
        {
            return new StackLine(StackLineKind.Frame, true, text, [new StyledSpan(0, text.Length, SpanStyle.Framework)]);
        }

        Group file = match.Groups["file"];
        Group line = match.Groups["line"];
        int open = function.Index + function.Length + 1;
        int directory = file.Value.LastIndexOfAny(['/', '\\']) + 1;

        var spans = new List<StyledSpan>
        {
            new(function.Index, function.Length, SpanStyle.Method),
            new(open, 1, SpanStyle.Punctuation),
        };
        if (directory > 0)
        {
            spans.Add(new StyledSpan(file.Index, directory, SpanStyle.Directory));
        }

        int locationStart = file.Index + directory;
        spans.Add(new StyledSpan(locationStart, line.Index + line.Length - locationStart, SpanStyle.FileLocation));
        spans.Add(new StyledSpan(text.Length - 1, 1, SpanStyle.Punctuation));
        return new StackLine(StackLineKind.Frame, false, text, spans);
    }

    private static StackLine Marker(StackLineKind kind, string text) =>
        new(kind, false, text, [new StyledSpan(0, text.Length, SpanStyle.Muted)]);

    private static bool IsFramework(string method, IReadOnlyList<string> prefixes)
    {
        foreach (string prefix in prefixes)
        {
            if (prefix.Length > 0 && method.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static Match? TryMatch(Regex regex, string text)
    {
        try
        {
            Match match = regex.Match(text);
            return match.Success ? match : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }
}
