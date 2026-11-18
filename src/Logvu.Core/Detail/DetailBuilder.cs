using System.Globalization;
using System.Text.Json;
using Logvu.Core.Config;
using Logvu.Core.Parsing;
using Logvu.Core.Store;

namespace Logvu.Core.Detail;

/// <summary>
/// Builds the structured document of a loaded event, with every node and fold expanded: the header, then the fields and message of a text
/// event, or the summary and the JSON tree of a JSON event. Highlights are not applied here.
/// </summary>
/// <remarks>
/// <para>
/// JSON layout: a node is <c>▾ key {n}</c> or <c>▸ key {n}</c> (objects) or <c>key [n]</c> with the same markers (arrays). It sits at
/// its depth column and its children one step deeper. Scalars sit at the same label column as nodes: <c>  key: value</c>.
/// </para>
/// <para>
/// Depth counts the root's members as depth 1. A node is expanded by default when it is at depth two or less, sits inside an error key
/// (<c>err</c>, <c>error</c>, <c>exception</c>, <c>exceptionDetail</c>, <c>stack</c>, <c>cause</c>), or contains one. A node whose
/// members are all skipped has no marker and no id.
/// </para>
/// <para>
/// Ids: a node's id is its path (<c>a.b[2].c</c>); a member that repeats a name in the same object takes <c>#n</c> after the name. A
/// fold's id is <c>fold:{scope}#{index}</c>, where index is its first frame's position in the scope's lines.
/// </para>
/// </remarks>
internal sealed class DetailBuilder
{
    private const int Step = 2;
    private const char ExpandedMarker = '▾';
    private const char CollapsedMarker = '▸';
    private const string FoldIdPrefix = "fold:";
    private const string MessageScope = "message";
    private const string MessageLabel = "Message";
    private const string ErrorLabel = "Error";

    private static readonly HashSet<string> ErrorKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "err", "error", "exception", "exceptiondetail", "stack", "cause",
    };

    private static readonly ModelLine Placeholder = new(new DetailLine(string.Empty, [], LineKind.Json, 0, null, null), null);

    private readonly EventDetail.Loaded _detail;
    private readonly LogvuConfig _config;
    private readonly List<ModelLine> _lines = [];
    private readonly Dictionary<string, bool> _defaults = new(StringComparer.Ordinal);
    private readonly List<string> _nodeIds = [];
    private readonly List<string> _foldIds = [];

    /// <summary>Creates a builder for one loaded event.</summary>
    /// <param name="detail">The loaded event.</param>
    /// <param name="config">The configuration: the time zone, and the framework prefixes.</param>
    public DetailBuilder(EventDetail.Loaded detail, LogvuConfig config)
    {
        _detail = detail;
        _config = config;
    }

    /// <summary>Builds the structured document.</summary>
    public DetailModel Build()
    {
        EmitHeader();

        LogEvent e = _detail.Event;
        if (e.Json is JsonElement root && root.ValueKind == JsonValueKind.Object)
        {
            EmitJson(e, root);
        }
        else
        {
            EmitText(e);
        }

        return new DetailModel(_lines, _defaults, _nodeIds, _foldIds);
    }

    /// <summary>A notice line: one muted line of text.</summary>
    /// <param name="text">The notice.</param>
    public static ModelLine NoticeLine(string text)
    {
        var spans = new List<StyledSpan>();
        if (text.Length > 0)
        {
            spans.Add(new StyledSpan(0, text.Length, SpanStyle.Muted));
        }

        return new ModelLine(new DetailLine(text, spans, LineKind.Notice, 0, null, null), null);
    }

    /// <summary>Splits text into its lines, with <c>\r\n</c> and <c>\n</c> as terminators.</summary>
    public static string[] SplitLines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private void EmitHeader()
    {
        LogEvent e = _detail.Event;
        AddHeader("Path", _detail.Path, missing: false);
        AddHeader("Offset", $"{_detail.Offset} bytes", missing: false);
        AddHeader("Length", $"{_detail.Length} bytes", missing: false);

        (string time, bool timeMissing) = TimeValue(e);
        AddHeader("Time", time, timeMissing);

        (string level, bool levelMissing) = LevelValue(e);
        AddHeader("Level", level, levelMissing);

        AddHeader("Source", e.Source ?? "-", e.Source is null);

        Add(new DetailLine(string.Empty, [], LineKind.Header, 0, null, null), null);
    }

    private void AddHeader(string label, string value, bool missing)
    {
        string head = label + ": ";
        var spans = new List<StyledSpan>
        {
            new(0, label.Length, SpanStyle.Label),
            new(label.Length, 2, SpanStyle.Punctuation),
        };
        if (missing)
        {
            spans.Add(new StyledSpan(head.Length, value.Length, SpanStyle.Muted));
        }

        Add(new DetailLine(head + value, spans, LineKind.Header, head.Length, null, null), null);
    }

    private (string Text, bool Missing) TimeValue(LogEvent e)
    {
        if (e.Time is DateTimeOffset time)
        {
            DateTimeOffset local = TimeZoneInfo.ConvertTime(time, _config.TimeZone);
            // Milliseconds, or as many fraction digits as the instant has beyond them (Serilog writes 7, Go 9 cut to 7).
            string fraction = local.ToString("fffffff", CultureInfo.InvariantCulture).TrimEnd('0').PadRight(3, '0');
            string normalized = local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "." + fraction
                + local.ToString(" zzz", CultureInfo.InvariantCulture);
            return (e.TimeText is null ? normalized : $"{normalized}  (as written: {e.TimeText})", false);
        }

        if (e.TimeText is not null)
        {
            return ($"{e.TimeText}  (not a timestamp)", false);
        }

        return ("-", true);
    }

    private static (string Text, bool Missing) LevelValue(LogEvent e)
    {
        if (e.LevelText is null)
        {
            return ("-", true);
        }

        string label = e.Level.ToString();
        return (string.Equals(label, e.LevelText, StringComparison.OrdinalIgnoreCase)
            ? label
            : $"{label}  (as written: {e.LevelText})", false);
    }

    private void EmitText(LogEvent e)
    {
        if (e.IsRaw)
        {
            Add(NoticeLine(e.ParseError is null
                ? "Text before the first header of the file"
                : $"Not a parsed record: {e.ParseError}").Line, null);
        }

        foreach (LogField field in e.Fields)
        {
            // The time, level and logger groups are shown in the header, with their original spelling.
            if (field.Name is "time" or "level" or "logger")
            {
                continue;
            }

            string head = field.Name + ": ";
            var spans = new List<StyledSpan>
            {
                new(0, field.Name.Length, SpanStyle.FieldName),
                new(field.Name.Length, 2, SpanStyle.Punctuation),
            };
            Add(new DetailLine(head + field.Value, spans, LineKind.Field, head.Length, null, field.Name, field.Value), null);
        }

        EmitTextValue(
            MessageLabel + ": ",
            LabelSpans(0, MessageLabel, SpanStyle.FieldName),
            e.Message,
            LineKind.Message,
            _ => MessageScope,
            MessageScope,
            null);
    }

    private void EmitJson(LogEvent e, JsonElement root)
    {
        EmitTextValue(
            MessageLabel + ": ",
            LabelSpans(0, MessageLabel, SpanStyle.FieldName),
            e.Message,
            LineKind.Message,
            _ => MessageScope,
            MessageScope,
            null);

        if (ErrorSummary(root) is { } summary)
        {
            EmitTextValue(
                ErrorLabel + ": ",
                LabelSpans(0, ErrorLabel, SpanStyle.FieldName),
                summary.Text,
                LineKind.Message,
                _ => summary.Scope,
                summary.Scope,
                null);
        }

        Add(new DetailLine(string.Empty, [], LineKind.Json, 0, null, null), null);
        EmitMembers(root, string.Empty, 1, 0, null, inError: false, skip: null);
    }

    /// <summary>
    /// The error summary of a JSON event, with the scope of its text: a Serilog <c>Exception</c>, else <c>err</c> or <c>error</c> (an object's
    /// <c>message</c>, or a string), else the first element of <c>cause</c>. The text is the first line only.
    /// </summary>
    private static (string Text, string Scope)? ErrorSummary(JsonElement root)
    {
        if (root.TryGetProperty("Exception", out JsonElement exception) && exception.ValueKind == JsonValueKind.String)
        {
            string? line = FirstLine(exception.GetString()!);
            if (line is not null)
            {
                return (line, "Exception");
            }
        }

        foreach (string name in new[] { "err", "error" })
        {
            if (!root.TryGetProperty(name, out JsonElement value))
            {
                continue;
            }

            (string Text, string Scope)? found = ErrorTextOf(value, name);
            if (found is not null)
            {
                return found;
            }
        }

        if (root.TryGetProperty("cause", out JsonElement cause)
            && cause.ValueKind == JsonValueKind.Array
            && cause.GetArrayLength() > 0)
        {
            return ErrorTextOf(cause.EnumerateArray().First(), "cause[0]");
        }

        return null;
    }

    private static (string Text, string Scope)? ErrorTextOf(JsonElement value, string scope)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return FirstLine(value.GetString()!) is { } text ? (text, scope) : null;
        }

        if (value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("message", out JsonElement message)
            && message.ValueKind == JsonValueKind.String)
        {
            return FirstLine(message.GetString()!) is { } text ? (text, scope + ".message") : null;
        }

        return null;
    }

    private static string? FirstLine(string text)
    {
        int newline = text.IndexOf('\n');
        string line = (newline < 0 ? text : text[..newline]).TrimEnd('\r');
        return line.Length > 0 ? line : null;
    }

    /// <summary>Emits the members of an object. Returns whether any of them, or anything under them, is an error key.</summary>
    private bool EmitMembers(JsonElement obj, string basePath, int depth, int pad, Anchor? parent, bool inError, string? skip)
    {
        bool childError = false;
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (JsonProperty property in obj.EnumerateObject())
        {
            string name = property.Name;
            if (name == skip)
            {
                continue;
            }

            occurrences.TryGetValue(name, out int seen);
            occurrences[name] = seen + 1;
            string segment = seen == 0 ? name : $"{name}#{seen + 1}";
            childError |= EmitValue(name, name, Join(basePath, segment), property.Value, depth, pad, parent, inError);
        }

        return childError;
    }

    /// <summary>Emits one value, labelled. Returns whether the key is an error key or anything under it is.</summary>
    private bool EmitValue(string label, string? memberName, string path, JsonElement value, int depth, int pad, Anchor? parent, bool inError)
    {
        bool isError = IsErrorKey(memberName);
        bool error = inError || isError;
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                return EmitObject(label, path, value, depth, pad, parent, error) || isError;

            case JsonValueKind.Array:
                return EmitArray(label, memberName, path, value, depth, pad, parent, error) || isError;

            case JsonValueKind.String:
                string text = value.GetString()!;
                if (text.Contains('\n'))
                {
                    EmitTextValue(
                        Head(pad, label),
                        LabelSpans(pad + Step, label, SpanStyle.JsonKey),
                        text,
                        LineKind.Json,
                        _ => path,
                        path,
                        parent);
                }
                else
                {
                    AddScalar(pad, label, path, value.GetRawText(), JsonValues.AsText(value), SpanStyle.JsonString, parent);
                }

                return isError;

            case JsonValueKind.Number:
                AddScalar(pad, label, path, value.GetRawText(), JsonValues.AsText(value), SpanStyle.JsonNumber, parent);
                return isError;

            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                AddScalar(pad, label, path, value.GetRawText(), JsonValues.AsText(value), SpanStyle.JsonLiteral, parent);
                return isError;

            default:
                return isError;
        }
    }

    private bool EmitObject(string label, string path, JsonElement obj, int depth, int pad, Anchor? parent, bool error)
    {
        int count = obj.GetPropertyCount();
        if (count == 0)
        {
            AddEmptyNode(pad, label, path, "{}", parent);
            return false;
        }

        int index = Reserve();
        bool childError = EmitMembers(obj, path, depth + 1, pad + Step, new Anchor(path, parent), error, skip: null);
        Finish(index, label, $"{{{count}}}", path, pad, parent, depth <= 2 || error || childError);
        return childError;
    }

    private bool EmitArray(string label, string? memberName, string path, JsonElement arr, int depth, int pad, Anchor? parent, bool error)
    {
        int count = arr.GetArrayLength();
        if (count == 0)
        {
            AddEmptyNode(pad, label, path, "[]", parent);
            return false;
        }

        if (memberName == "stack" && IsSingleLineStrings(arr))
        {
            return EmitGoStack(label, path, arr, pad, parent);
        }

        bool isCause = memberName == "cause";
        int index = Reserve();
        var anchor = new Anchor(path, parent);
        bool childError = false;
        int i = 0;
        foreach (JsonElement item in arr.EnumerateArray())
        {
            string itemPath = $"{path}[{i}]";
            childError |= isCause
                ? EmitCauseItem(i, itemPath, item, depth + 1, pad + Step, anchor, error)
                : EmitValue($"[{i}]", null, itemPath, item, depth + 1, pad + Step, anchor, error);
            i++;
        }

        Finish(index, label, $"[{count}]", path, pad, parent, depth <= 2 || error || childError);
        return childError;
    }

    /// <summary>One item of a <c>cause</c> array: a string is <c>cause[i]: text</c>; an object with a message heads its own node.</summary>
    private bool EmitCauseItem(int index, string path, JsonElement item, int depth, int pad, Anchor? parent, bool error)
    {
        string label = $"cause[{index}]";
        if (item.ValueKind == JsonValueKind.String)
        {
            string text = item.GetString()!;
            if (text.Contains('\n'))
            {
                EmitTextValue(Head(pad, label), LabelSpans(pad + Step, label, SpanStyle.JsonKey), text, LineKind.Json, _ => path, path, parent);
            }
            else
            {
                AddScalar(pad, label, path, text, text, SpanStyle.JsonString, parent);
            }

            return false;
        }

        if (item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty("message", out JsonElement message)
            && message.ValueKind == JsonValueKind.String
            && !message.GetString()!.Contains('\n'))
        {
            return EmitCauseNode(label, path, item, message.GetString()!, depth, pad, parent, error);
        }

        return EmitValue(label, null, path, item, depth, pad, parent, error);
    }

    /// <summary>A cause object: its message heads the line, and its other members are the children.</summary>
    private bool EmitCauseNode(string label, string path, JsonElement obj, string message, int depth, int pad, Anchor? parent, bool error)
    {
        bool hasOthers = false;
        foreach (JsonProperty property in obj.EnumerateObject())
        {
            if (property.Name != "message")
            {
                hasOthers = true;
                break;
            }
        }

        string messageScope = path + ".message";
        if (!hasOthers)
        {
            string head = Head(pad, label);
            var spans = new List<StyledSpan>
            {
                new(pad + Step, label.Length, SpanStyle.JsonKey),
                new(head.Length - 2, 2, SpanStyle.Punctuation),
            };
            Add(new DetailLine(head + message, spans, LineKind.Json, head.Length, null, messageScope), parent);
            return false;
        }

        int index = Reserve();
        bool childError = EmitMembers(obj, path, depth + 1, pad + Step, new Anchor(path, parent), error, skip: "message");
        bool expanded = depth <= 2 || error || childError;
        string prefix = Pad(pad) + (expanded ? ExpandedMarker : CollapsedMarker) + " " + label + ": ";
        var headSpans = new List<StyledSpan>
        {
            new(pad, 1, SpanStyle.Punctuation),
            new(pad + Step, label.Length, SpanStyle.JsonKey),
            new(pad + Step + label.Length, 2, SpanStyle.Punctuation),
        };
        RecordNode(path, expanded);
        _lines[index] = new ModelLine(
            new DetailLine(prefix + message, headSpans, LineKind.Json, prefix.Length, path, messageScope),
            parent);
        return childError;
    }

    private bool EmitGoStack(string label, string path, JsonElement arr, int pad, Anchor? parent)
    {
        var frames = new List<string>();
        foreach (JsonElement item in arr.EnumerateArray())
        {
            frames.Add(item.GetString()!);
        }

        int index = Reserve();
        IReadOnlyList<StackLine> analysis = StackTraceAnalyzer.AnalyzeGoStack(frames, _config.FrameworkPrefixes);
        EmitStack(analysis, Pad(pad + Step), [], LineKind.Json, k => $"{path}[{k}]", path, new Anchor(path, parent));
        Finish(index, label, $"[{frames.Count}]", path, pad, parent, expanded: true);
        return true;
    }

    private void EmitTextValue(
        string firstPrefix,
        IReadOnlyList<StyledSpan> firstPrefixSpans,
        string value,
        LineKind kind,
        Func<int, string> scopeOf,
        string foldScope,
        Anchor? parent)
    {
        IReadOnlyList<StackLine> analysis = StackTraceAnalyzer.Analyze(SplitLines(value), _config.FrameworkPrefixes);
        EmitStack(analysis, firstPrefix, firstPrefixSpans, kind, scopeOf, foldScope, parent);
    }

    /// <summary>
    /// Emits analyzed lines. Each run of two or more consecutive framework frames gets a fold marker above it, and its frames sit under the
    /// marker's id. The first line carries the prefix (and its spans); every line is indented to the prefix's width.
    /// </summary>
    private void EmitStack(
        IReadOnlyList<StackLine> analysis,
        string firstPrefix,
        IReadOnlyList<StyledSpan> firstPrefixSpans,
        LineKind kind,
        Func<int, string> scopeOf,
        string foldScope,
        Anchor? parent)
    {
        int width = firstPrefix.Length;
        string rest = Pad(width);

        void EmitOne(int k, Anchor? anchor)
        {
            StackLine line = analysis[k];
            var spans = new List<StyledSpan>();
            if (k == 0)
            {
                spans.AddRange(firstPrefixSpans);
            }

            foreach (StyledSpan span in line.Spans)
            {
                spans.Add(span with { Start = span.Start + width });
            }

            Add(new DetailLine((k == 0 ? firstPrefix : rest) + line.Text, spans, kind, width, null, scopeOf(k)), anchor);
        }

        int i = 0;
        while (i < analysis.Count)
        {
            int end = i;
            while (end < analysis.Count && analysis[end] is { Kind: StackLineKind.Frame, IsFramework: true })
            {
                end++;
            }

            if (end - i >= 2)
            {
                string foldId = FoldIdPrefix + foldScope + "#" + i;
                string marker = $"… {end - i} framework frames";
                _foldIds.Add(foldId);
                _defaults[foldId] = true;
                Add(
                    new DetailLine(
                        rest + marker,
                        [new StyledSpan(width, marker.Length, SpanStyle.FoldMarker)],
                        LineKind.Fold,
                        width,
                        foldId,
                        null),
                    parent);

                var foldAnchor = new Anchor(foldId, parent);
                for (int k = i; k < end; k++)
                {
                    EmitOne(k, foldAnchor);
                }

                i = end;
            }
            else
            {
                EmitOne(i, parent);
                i++;
            }
        }
    }

    private void AddScalar(int pad, string label, string path, string valueText, string value, SpanStyle style, Anchor? parent)
    {
        string head = Head(pad, label);
        var spans = new List<StyledSpan>
        {
            new(pad + Step, label.Length, SpanStyle.JsonKey),
            new(head.Length - 2, 2, SpanStyle.Punctuation),
        };
        if (valueText.Length > 0)
        {
            spans.Add(new StyledSpan(head.Length, valueText.Length, style));
        }

        Add(new DetailLine(head + valueText, spans, LineKind.Json, head.Length, null, path, value), parent);
    }

    private void AddEmptyNode(int pad, string label, string path, string suffix, Anchor? parent)
    {
        string head = Pad(pad + Step) + label + " ";
        var spans = new List<StyledSpan>
        {
            new(pad + Step, label.Length, SpanStyle.JsonKey),
            new(head.Length, suffix.Length, SpanStyle.Muted),
        };
        Add(new DetailLine(head + suffix, spans, LineKind.Json, pad + Step, null, path), parent);
    }

    /// <summary>Reserves the slot of a node line, to be filled by <see cref="Finish"/> once its children are known.</summary>
    private int Reserve()
    {
        _lines.Add(Placeholder);
        return _lines.Count - 1;
    }

    private void Finish(int index, string label, string suffix, string path, int pad, Anchor? parent, bool expanded)
    {
        string head = Pad(pad) + (expanded ? ExpandedMarker : CollapsedMarker) + " " + label + " ";
        var spans = new List<StyledSpan>
        {
            new(pad, 1, SpanStyle.Punctuation),
            new(pad + Step, label.Length, SpanStyle.JsonKey),
            new(head.Length, suffix.Length, SpanStyle.Muted),
        };
        RecordNode(path, expanded);
        _lines[index] = new ModelLine(new DetailLine(head + suffix, spans, LineKind.Json, pad + Step, path, path), parent);
    }

    private void RecordNode(string path, bool expanded)
    {
        _defaults[path] = expanded;
        _nodeIds.Add(path);
    }

    private void Add(DetailLine line, Anchor? parent) => _lines.Add(new ModelLine(line, parent));

    private static bool IsErrorKey(string? name) => name is not null && ErrorKeys.Contains(name);

    private static bool IsSingleLineStrings(JsonElement arr)
    {
        foreach (JsonElement item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString()!.Contains('\n'))
            {
                return false;
            }
        }

        return true;
    }

    private static List<StyledSpan> LabelSpans(int start, string label, SpanStyle style) =>
    [
        new(start, label.Length, style),
        new(start + label.Length, 2, SpanStyle.Punctuation),
    ];

    /// <summary>The start of a JSON line for a value at <paramref name="pad"/>: the label column is two columns in, past the marker.</summary>
    private static string Head(int pad, string label) => Pad(pad + Step) + label + ": ";

    private static string Pad(int width) => new(' ', width);

    private static string Join(string basePath, string segment) => basePath.Length == 0 ? segment : basePath + "." + segment;
}
