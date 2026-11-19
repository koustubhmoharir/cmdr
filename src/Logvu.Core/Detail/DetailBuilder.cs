using System.Globalization;
using System.Text;
using System.Text.Json;
using Logvu.Core.Config;
using Logvu.Core.Filter;
using Logvu.Core.Parsing;
using Logvu.Core.Store;

namespace Logvu.Core.Detail;

/// <summary>
/// Builds the structured document of a loaded event, with every node and fold expanded: the fields and message of a text event, or the
/// JSON tree of a JSON event (preceded by its message when logvu rendered it). Highlights are not applied here.
/// </summary>
/// <remarks>
/// <para>
/// JSON layout: a node is <c>▾ key</c> or <c>▸ key</c>, for an object or an array. It sits at its depth column and its children one step
/// deeper. Scalars sit at the same label column as nodes: <c>  key: value</c>. Array items are labelled <c>[i]</c> counting from 1, with
/// the numbers right-aligned to the array's length; the labels are dimmed.
/// </para>
/// <para>
/// Depth counts the root's members as depth 1. A node is expanded by default when it is at depth two or less, sits inside an error key
/// (<c>err</c>, <c>error</c>, <c>exception</c>, <c>exceptionDetail</c>, <c>stack</c>, <c>cause</c>), or contains one. A node whose
/// members are all skipped has no marker and no id.
/// </para>
/// <para>
/// Ids: a node's id is its path (<c>a.b[2].c</c>); a member that repeats a name in the same object takes <c>#n</c> after the name. Paths
/// count array items from 0, so only the displayed labels start at 1. A fold's id is <c>fold:{scope}#{index}</c>, where index is its first
/// frame's position in the scope's lines.
/// </para>
/// <para>
/// A label whose path is a configured column or message field (<c>columns</c> and <c>messageFields</c>) is drawn as
/// <see cref="SpanStyle.ConfiguredKey"/>. A JSON path matches only a member that <see cref="JsonFieldPath"/> can address: the last of
/// repeated names, and a name with no dot or bracket. Such a member's subtree never matches, since its paths cannot resolve. A text event's
/// field matches a column of the same name, and the rendered message label of a JSON event matches a <c>message</c> column.
/// </para>
/// </remarks>
internal sealed partial class DetailBuilder
{
    private const int Step = 2;
    private const char ExpandedMarker = '▾';
    private const char CollapsedMarker = '▸';
    private const string FoldIdPrefix = "fold:";
    private const string MessageScope = "message";
    private const string MessageLabel = "Message";

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

    /// <summary>
    /// The canonical forms of the configured JSON paths: the columns' and the message fields' paths that are well formed.
    /// </summary>
    private readonly HashSet<string> _configuredPaths = new(StringComparer.Ordinal);

    /// <summary>The configured column paths as written. A text event's field matches one by name.</summary>
    private readonly HashSet<string> _columnNames = new(StringComparer.Ordinal);

    /// <summary>
    /// The number of enclosing members that cannot be addressed by a path: a repeated name, or a name with a dot or bracket. While it is
    /// positive no label matches a configured path.
    /// </summary>
    private int _unaddressable;

    /// <summary>
    /// The node paths of the last occurrences of repeated names (<c>req#2</c>), each with the path that addresses it (<c>req</c>). Recorded
    /// already translated, so an alias inside another resolves fully by the longest prefix.
    /// </summary>
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);

    /// <summary>Creates a builder for one loaded event.</summary>
    /// <param name="detail">The loaded event.</param>
    /// <param name="config">The configuration: the framework prefixes and the configured paths.</param>
    public DetailBuilder(EventDetail.Loaded detail, LogvuConfig config)
    {
        _detail = detail;
        _config = config;
        foreach (ColumnSpec column in config.Columns)
        {
            _columnNames.Add(column.Path);
            AddConfiguredPath(column.Path);
        }

        foreach (string path in config.MessageFields)
        {
            AddConfiguredPath(path);
        }
    }

    /// <summary>Builds the structured document.</summary>
    public DetailModel Build()
    {
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

    /// <summary>
    /// A text event starts with its fields, in pattern order. The <c>message</c> field is a multi-line value: its text is analyzed as a stack
    /// trace, and its continuation lines are indented to its value. A raw event has no fields, so its notice and its text come instead.
    /// </summary>
    private void EmitText(LogEvent e)
    {
        if (e.IsRaw)
        {
            Add(NoticeLine(e.ParseError is null
                ? "Text before the first header of the file"
                : $"Not a parsed record: {e.ParseError}").Line, null);
            EmitTextValue(
                MessageLabel + ": ",
                LabelSpans(0, MessageLabel, SpanStyle.JsonKey),
                e.Message,
                LineKind.Message,
                _ => MessageScope,
                MessageScope,
                null,
                textEvent: true);
            return;
        }

        foreach (LogField field in e.Fields)
        {
            if (field.Name == EventFields.MessagePath)
            {
                EmitTextValue(
                    field.Name + ": ",
                    LabelSpans(0, field.Name, TextKeyStyle(field.Name)),
                    field.Value,
                    LineKind.Message,
                    _ => MessageScope,
                    MessageScope,
                    null,
                    textEvent: true);
                continue;
            }

            string head = field.Name + ": ";
            var spans = new List<StyledSpan>
            {
                new(0, field.Name.Length, TextKeyStyle(field.Name)),
                new(field.Name.Length, 2, SpanStyle.Punctuation),
            };
            spans.AddRange(ValueStyling.Field(field.Value).Select(span => span with { Start = span.Start + head.Length }));
            Add(new DetailLine(head + field.Value, spans, LineKind.Field, head.Length, null, field.Name, field.Value), null);
        }
    }

    /// <summary>
    /// A JSON event starts with its tree. The message comes first only when logvu rendered it from a Serilog template, since that text
    /// appears nowhere else in the event.
    /// </summary>
    private void EmitJson(LogEvent e, JsonElement root)
    {
        if (e.MessageRendered)
        {
            EmitTextValue(
                MessageLabel + ": ",
                LabelSpans(0, MessageLabel, _columnNames.Contains(EventFields.MessagePath) ? SpanStyle.ConfiguredKey : SpanStyle.FieldName),
                e.Message,
                LineKind.Message,
                _ => MessageScope,
                MessageScope,
                null,
                textEvent: false);
            Add(new DetailLine(string.Empty, [], LineKind.Json, 0, null, null), null);
        }

        EmitMembers(root, string.Empty, 1, 0, null, inError: false, skip: null);
    }

    /// <summary>Emits the members of an object. Returns whether any of them, or anything under them, is an error key.</summary>
    private bool EmitMembers(JsonElement obj, string basePath, int depth, int pad, Anchor? parent, bool inError, string? skip)
    {
        bool childError = false;

        // A path resolves a repeated name to its last occurrence (see JsonFieldPath), so only that one can match a configured path.
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (JsonProperty property in obj.EnumerateObject())
        {
            totals[property.Name] = totals.GetValueOrDefault(property.Name) + 1;
        }

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
            bool addressable = seen + 1 == totals[name] && IsAddressable(name);
            if (!addressable)
            {
                _unaddressable++;
            }
            else if (seen > 0 && _unaddressable == 0)
            {
                // The last of repeated names: its id carries #n, but a configured path names it plainly.
                _aliases[Join(basePath, segment)] = Join(MatchPath(basePath), name);
            }

            childError |= EmitValue(name, name, Join(basePath, segment), property.Value, depth, pad, parent, inError, SpanStyle.JsonKey);
            if (!addressable)
            {
                _unaddressable--;
            }
        }

        return childError;
    }

    /// <summary>
    /// Emits one value, labelled. The label is drawn in <paramref name="labelStyle"/>: a member's key, or a dimmed array index. Returns
    /// whether the key is an error key or anything under it is.
    /// </summary>
    private bool EmitValue(
        string label,
        string? memberName,
        string path,
        JsonElement value,
        int depth,
        int pad,
        Anchor? parent,
        bool inError,
        SpanStyle labelStyle)
    {
        bool isError = IsErrorKey(memberName);
        bool error = inError || isError;
        labelStyle = KeyStyleAt(path, labelStyle);
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                return EmitObject(label, path, value, depth, pad, parent, error, labelStyle) || isError;

            case JsonValueKind.Array:
                return EmitArray(label, memberName, path, value, depth, pad, parent, error, labelStyle) || isError;

            case JsonValueKind.String:
                string text = value.GetString()!;
                if (text.Contains('\n'))
                {
                    EmitTextValue(
                        Head(pad, label),
                        LabelSpans(pad + Step, label, labelStyle),
                        text,
                        LineKind.Json,
                        _ => path,
                        path,
                        parent,
                        textEvent: false);
                }
                else
                {
                    string raw = value.GetRawText();
                    AddScalar(pad, label, path, raw, text, parent, labelStyle, ValueStyling.JsonString(raw, text));
                }

                return isError;

            case JsonValueKind.Number:
                AddScalar(pad, label, path, value.GetRawText(), JsonValues.AsText(value), parent, labelStyle, SpanStyle.JsonNumber);
                return isError;

            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                AddScalar(pad, label, path, value.GetRawText(), JsonValues.AsText(value), parent, labelStyle, SpanStyle.JsonLiteral);
                return isError;

            default:
                return isError;
        }
    }

    private bool EmitObject(string label, string path, JsonElement obj, int depth, int pad, Anchor? parent, bool error, SpanStyle labelStyle)
    {
        if (obj.GetPropertyCount() == 0)
        {
            AddEmptyNode(pad, label, path, "{}", parent, labelStyle);
            return false;
        }

        int index = Reserve();
        bool childError = EmitMembers(obj, path, depth + 1, pad + Step, new Anchor(path, parent), error, skip: null);
        Finish(index, label, path, pad, parent, depth <= 2 || error || childError, labelStyle);
        return childError;
    }

    private bool EmitArray(
        string label,
        string? memberName,
        string path,
        JsonElement arr,
        int depth,
        int pad,
        Anchor? parent,
        bool error,
        SpanStyle labelStyle)
    {
        int count = arr.GetArrayLength();
        if (count == 0)
        {
            AddEmptyNode(pad, label, path, "[]", parent, labelStyle);
            return false;
        }

        if (memberName == "stack" && IsSingleLineStrings(arr))
        {
            return EmitGoStack(label, path, arr, pad, parent, labelStyle);
        }

        int index = Reserve();
        var anchor = new Anchor(path, parent);
        string[] labels = IndexLabels(count);
        bool childError = false;
        int i = 0;
        foreach (JsonElement item in arr.EnumerateArray())
        {
            childError |= EmitValue(labels[i], null, $"{path}[{i}]", item, depth + 1, pad + Step, anchor, error, SpanStyle.Muted);
            i++;
        }

        Finish(index, label, path, pad, parent, depth <= 2 || error || childError, labelStyle);
        return childError;
    }

    private bool EmitGoStack(string label, string path, JsonElement arr, int pad, Anchor? parent, SpanStyle labelStyle)
    {
        var frames = new List<string>();
        foreach (JsonElement item in arr.EnumerateArray())
        {
            frames.Add(item.GetString()!);
        }

        int index = Reserve();
        IReadOnlyList<StackLine> analysis = StackTraceAnalyzer.AnalyzeGoStack(frames, _config.FrameworkPrefixes);
        EmitStack(
            analysis,
            Pad(pad + 2 * Step),
            [],
            LineKind.Json,
            k => $"{path}[{k}]",
            path,
            new Anchor(path, parent),
            IndexLabels(frames.Count),
            k => KeyStyleAt($"{path}[{k}]", SpanStyle.Muted));
        Finish(index, label, path, pad, parent, expanded: true, labelStyle: labelStyle);
        return true;
    }

    /// <summary>
    /// Emits a multi-line value as a stack trace. Its lines the analysis leaves plain are styled as message text: a text event's (or raw
    /// event's) message is a JSON string with its exception type names, and every such line takes its links.
    /// </summary>
    /// <param name="textEvent">Whether the value is the message of a text event or a raw event.</param>
    private void EmitTextValue(
        string firstPrefix,
        IReadOnlyList<StyledSpan> firstPrefixSpans,
        string value,
        LineKind kind,
        Func<int, string> scopeOf,
        string foldScope,
        Anchor? parent,
        bool textEvent)
    {
        IReadOnlyList<StackLine> analysis = StackTraceAnalyzer.Analyze(SplitLines(value), _config.FrameworkPrefixes);
        EmitStack(ValueStyling.StyleOtherLines(analysis, textEvent), firstPrefix, firstPrefixSpans, kind, scopeOf, foldScope, parent);
    }

    /// <summary>
    /// Emits analyzed lines. Each run of two or more consecutive framework frames gets a fold marker above it, and its frames sit under the
    /// marker's id. The first line carries the prefix (and its spans); every line is indented to the prefix's width.
    /// </summary>
    /// <param name="labels">
    /// Optional. When given, every line opens with its label instead of the prefix: the label sits at the column of
    /// <paramref name="firstPrefix"/>, which is then only an indent, and the text follows <c>: </c>. The labels must all have the same
    /// length. The fold markers and the frames are indented past the labels.
    /// </param>
    /// <param name="labelStyleOf">Optional. The style of a line's label, by the line's index. Muted when not given.</param>
    private void EmitStack(
        IReadOnlyList<StackLine> analysis,
        string firstPrefix,
        IReadOnlyList<StyledSpan> firstPrefixSpans,
        LineKind kind,
        Func<int, string> scopeOf,
        string foldScope,
        Anchor? parent,
        IReadOnlyList<string>? labels = null,
        Func<int, SpanStyle>? labelStyleOf = null)
    {
        int width = labels is null ? firstPrefix.Length : firstPrefix.Length + labels[0].Length + 2;
        string rest = Pad(width);

        void EmitOne(int k, Anchor? anchor)
        {
            StackLine line = analysis[k];
            var spans = new List<StyledSpan>();
            string prefix;
            if (labels is null)
            {
                prefix = k == 0 ? firstPrefix : rest;
                if (k == 0)
                {
                    spans.AddRange(firstPrefixSpans);
                }
            }
            else
            {
                prefix = firstPrefix + labels[k] + ": ";
                spans.Add(new StyledSpan(firstPrefix.Length, labels[k].Length, labelStyleOf?.Invoke(k) ?? SpanStyle.Muted));
                spans.Add(new StyledSpan(firstPrefix.Length + labels[k].Length, 2, SpanStyle.Punctuation));
            }

            foreach (StyledSpan span in line.Spans)
            {
                spans.Add(span with { Start = span.Start + width });
            }

            Add(new DetailLine(prefix + line.Text, spans, kind, width, null, scopeOf(k)), anchor);
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

    /// <summary>
    /// Adds a labelled scalar line. The value's spans are relative to its text: they are given as <paramref name="valueSpans"/>, or else the
    /// whole value is drawn in <paramref name="style"/>.
    /// </summary>
    private void AddScalar(
        int pad,
        string label,
        string path,
        string valueText,
        string value,
        Anchor? parent,
        SpanStyle labelStyle,
        SpanStyle style,
        IReadOnlyList<StyledSpan>? valueSpans = null)
    {
        AddScalar(pad, label, path, valueText, value, parent, labelStyle, valueSpans ?? ValueStyling.Whole(valueText, style));
    }

    private void AddScalar(
        int pad,
        string label,
        string path,
        string valueText,
        string value,
        Anchor? parent,
        SpanStyle labelStyle,
        IReadOnlyList<StyledSpan> valueSpans)
    {
        string head = Head(pad, label);
        var spans = new List<StyledSpan>
        {
            new(pad + Step, label.Length, labelStyle),
            new(head.Length - 2, 2, SpanStyle.Punctuation),
        };
        foreach (StyledSpan span in valueSpans)
        {
            spans.Add(span with { Start = head.Length + span.Start });
        }

        Add(new DetailLine(head + valueText, spans, LineKind.Json, head.Length, null, path, value), parent);
    }

    private void AddEmptyNode(int pad, string label, string path, string suffix, Anchor? parent, SpanStyle labelStyle)
    {
        string head = Pad(pad + Step) + label + " ";
        var spans = new List<StyledSpan>
        {
            new(pad + Step, label.Length, labelStyle),
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

    /// <summary>Fills the slot of a node line: its marker, its label in <paramref name="labelStyle"/>, and its expanded state.</summary>
    private void Finish(int index, string label, string path, int pad, Anchor? parent, bool expanded, SpanStyle labelStyle)
    {
        string head = Pad(pad) + (expanded ? ExpandedMarker : CollapsedMarker) + " " + label;
        var spans = new List<StyledSpan>
        {
            new(pad, 1, SpanStyle.Punctuation),
            new(pad + Step, label.Length, labelStyle),
        };
        RecordNode(path, expanded);
        _lines[index] = new ModelLine(new DetailLine(head, spans, LineKind.Json, pad + Step, path, path), parent);
    }

    private void RecordNode(string path, bool expanded)
    {
        _defaults[path] = expanded;
        _nodeIds.Add(path);
    }

    private void Add(DetailLine line, Anchor? parent) => _lines.Add(new ModelLine(line, parent));

    private static bool IsErrorKey(string? name) => name is not null && ErrorKeys.Contains(name);

    /// <summary>
    /// The style of a label at a JSON path: <see cref="SpanStyle.ConfiguredKey"/> when the path is a configured one and no enclosing member
    /// is unaddressable, otherwise <paramref name="normal"/>.
    /// </summary>
    private SpanStyle KeyStyleAt(string path, SpanStyle normal) =>
        _unaddressable == 0 && _configuredPaths.Contains(MatchPath(path)) ? SpanStyle.ConfiguredKey : normal;

    /// <summary>
    /// The path a configured path is compared with: the node path with the longest alias prefix replaced (see <see cref="_aliases"/>). The
    /// node path itself when no repeated name is on it.
    /// </summary>
    private string MatchPath(string path)
    {
        if (_aliases.Count == 0)
        {
            return path;
        }

        string? bestKey = null;
        foreach (string key in _aliases.Keys)
        {
            bool prefix = path.Length == key.Length
                ? path == key
                : path.Length > key.Length && path.StartsWith(key, StringComparison.Ordinal) && path[key.Length] is '.' or '[';
            if (prefix && (bestKey is null || key.Length > bestKey.Length))
            {
                bestKey = key;
            }
        }

        return bestKey is null ? path : _aliases[bestKey] + path[bestKey.Length..];
    }

    /// <summary>The style of a text event's field label: a column's name is <see cref="SpanStyle.ConfiguredKey"/>.</summary>
    private SpanStyle TextKeyStyle(string name) => _columnNames.Contains(name) ? SpanStyle.ConfiguredKey : SpanStyle.JsonKey;

    /// <summary>
    /// Adds a configured path's canonical form to the set. A path that is not well formed can never resolve, so it is skipped.
    /// </summary>
    private void AddConfiguredPath(string path)
    {
        if (CanonicalPath(path) is { } canonical)
        {
            _configuredPaths.Add(canonical);
        }
    }

    /// <summary>
    /// Tells whether a member name can be addressed by a path: it is not empty and has no dot or bracket, the characters that
    /// <see cref="JsonFieldPath"/> reads as separators.
    /// </summary>
    private static bool IsAddressable(string name) => name.Length > 0 && name.IndexOfAny(['.', '[']) < 0;

    /// <summary>
    /// The canonical form of a well-formed path, as the builder's paths are written: named segments joined by dots, and every index in
    /// decimal without leading zeros (<c>a.b[2]</c>, and <c>[0]</c> written as a segment of its own joins its name with no dot).
    /// </summary>
    /// <returns>The canonical form, or <c>null</c> when the path is not well formed.</returns>
    private static string? CanonicalPath(string path)
    {
        if (!JsonFieldPath.IsWellFormed(path))
        {
            return null;
        }

        var text = new StringBuilder();
        foreach (string segment in path.Split('.'))
        {
            int position = segment.IndexOf('[');
            string name = position < 0 ? segment : segment[..position];
            if (name.Length > 0)
            {
                if (text.Length > 0)
                {
                    text.Append('.');
                }

                text.Append(name);
            }

            while (position >= 0 && position < segment.Length)
            {
                int close = segment.IndexOf(']', position);
                ReadOnlySpan<char> digits = segment.AsSpan(position + 1, close - position - 1);
                if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                {
                    return null;
                }

                text.Append('[').Append(index).Append(']');
                position = close + 1;
            }
        }

        return text.ToString();
    }

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

    /// <summary>The labels of the items of an array of <paramref name="count"/> items: <c>[1]</c> to <c>[count]</c>, right-aligned.</summary>
    private static string[] IndexLabels(int count)
    {
        int digits = count.ToString().Length;
        var labels = new string[count];
        for (int i = 0; i < count; i++)
        {
            labels[i] = "[" + (i + 1).ToString().PadLeft(digits) + "]";
        }

        return labels;
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
