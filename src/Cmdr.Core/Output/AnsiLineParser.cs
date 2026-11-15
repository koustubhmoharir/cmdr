using System.Text;

namespace Cmdr.Core.Output;

/// <summary>
/// Turns a stream of text chunks, possibly containing ANSI escape sequences, into <see cref="OutputLine"/>s.
/// SGR sequences are applied to the style of subsequent text; all other escape sequences are dropped.
/// Escape sequences may be split across chunks at any character.
/// A tab pads with spaces to the next multiple of eight display columns (see <see cref="DisplayWidth"/>), so wide
/// characters and combining marks count as the terminal draws them.
/// </summary>
/// <remarks>Not thread-safe. Use one instance per stream.</remarks>
public sealed class AnsiLineParser
{
    private const char Esc = '\u001B';
    private const char Bel = '\u0007';
    private const char CsiC1 = '\u009B';
    private const char OscC1 = '\u009D';
    private const char StringTerminatorC1 = '\u009C';
    private const int MaxCsiParamLength = 256;

    private enum State
    {
        Normal,
        Escape,
        EscapeIntermediate,
        Csi,
        StringSeq,
        StringEscape,
    }

    private const int InitialCapacity = 64;
    private const int InitialRunCapacity = 4;

    private readonly OutputSource _source;
    private readonly int _maxLineLength;
    private readonly StringBuilder _csiParams = new();

    // The current line: _chars[0.._length) and _runs[0.._runCount) (run start index and style).
    private char[] _chars = new char[InitialCapacity];
    private int _length;
    private (int Start, SpanStyle Style)[] _runs = new (int Start, SpanStyle Style)[InitialRunCapacity];
    private int _runCount;

    // Published snapshots (partial lines) may share _chars and _runs. They cover [0.._publishedLength) and
    // [0.._publishedRuns). Writing below those indices first moves to fresh arrays (see EnsureWritable), so a snapshot
    // never changes. Pure appends past them need no copy.
    private bool _shared;

    // The identity of the current line (see OutputLine.Identity): shared by its partial snapshots and its completed form.
    private long _identity = OutputLine.NewIdentity();
    private int _publishedLength;
    private int _publishedRuns;

    private SpanStyle _style = SpanStyle.Default;
    private State _state = State.Normal;
    private bool _pendingCr;
    private bool _overwritePending;
    private bool _csiHasIntermediate;
    private bool _csiOverflow;

    // An already-measured prefix of the current line, so that tabs do not re-measure the whole line. Always 0/0 or a point
    // just after tab spaces, and reset whenever the line is cleared or shortened below it.
    private int _widthCheckpointLength;
    private int _widthCheckpointColumns;

    /// <summary>Creates a parser for one output stream.</summary>
    /// <param name="source">The source tagged on every line this parser produces.</param>
    /// <param name="maxLineLength">A line reaching this many UTF-16 code units is completed as if a newline was seen. The last grapheme is kept intact by moving it to the next line; only a grapheme that is the whole line is split, except a supplementary scalar, for which a limit of one may be exceeded by one code unit. A high surrogate at the limit is appended before its low surrogate decides, so the line may exceed the limit by one code unit for a moment.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxLineLength"/> is not positive.</exception>
    public AnsiLineParser(OutputSource source, int maxLineLength = 65536)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLineLength);
        _source = source;
        _maxLineLength = maxLineLength;
    }

    /// <summary>
    /// The current incomplete line, or null if it is empty. Taking it does not copy the text: the line's spans and text are
    /// built when first read, and later writes to this parser never change what a returned line shows.
    /// </summary>
    public OutputLine? Partial
    {
        get
        {
            if (_length == 0)
            {
                return null;
            }

            _shared = true;
            _publishedLength = Math.Max(_publishedLength, _length);
            _publishedRuns = Math.Max(_publishedRuns, _runCount);
            return OutputLine.FromPartial(_source, _chars, _length, _runs, _runCount, _identity);
        }
    }

    /// <summary>
    /// Parses <paramref name="chunk"/>, appending each completed line to <paramref name="completed"/>.
    /// Parser state carries over to the next call.
    /// </summary>
    public void Feed(ReadOnlySpan<char> chunk, List<OutputLine> completed)
    {
        ArgumentNullException.ThrowIfNull(completed);

        for (int i = 0; i < chunk.Length; i++)
        {
            Process(chunk[i], completed);
        }
    }

    /// <summary>
    /// End of stream: completes the partial line if it is non-empty and discards any incomplete escape
    /// sequence. A trailing bare carriage return keeps the line's content. The SGR style is kept.
    /// </summary>
    public void Flush(List<OutputLine> completed)
    {
        ArgumentNullException.ThrowIfNull(completed);

        // A CR at end of stream only moves the cursor, so the content is kept.
        _pendingCr = false;
        _overwritePending = false;
        _state = State.Normal;
        ResetCsi();

        if (_length > 0)
        {
            CompleteLine(completed);
        }
    }

    private void Process(char c, List<OutputLine> completed)
    {
        if (_pendingCr)
        {
            _pendingCr = false;
            if (c == '\n')
            {
                CompleteLine(completed);
                return;
            }

            // The CR was bare. Like a terminal, it only moves the cursor to column 0; the content is
            // cleared when the next printable character or tab is written.
            _overwritePending = true;
        }

        switch (_state)
        {
            case State.Normal:
                ProcessNormal(c, completed);
                break;
            case State.Escape:
                ProcessEscape(c, completed);
                break;
            case State.EscapeIntermediate:
                ProcessEscapeIntermediate(c, completed);
                break;
            case State.Csi:
                ProcessCsi(c, completed);
                break;
            case State.StringSeq:
                ProcessString(c, completed);
                break;
            case State.StringEscape:
                ProcessStringEscape(c, completed);
                break;
        }
    }

    private void ProcessNormal(char c, List<OutputLine> completed)
    {
        switch (c)
        {
            case Esc:
                _state = State.Escape;
                return;
            case '\n':
                CompleteLine(completed);
                return;
            case '\u0090': // DCS
            case '\u0098': // SOS
            case OscC1: // OSC
            case '\u009E': // PM
            case '\u009F': // APC
                _state = State.StringSeq;
                return;
            case '\r':
                _pendingCr = true;
                return;
            case '\t':
                ClearPendingOverwrite();
                AppendTab(completed);
                return;
            case '\b':
                Backspace();
                return;
            case CsiC1:
                _state = State.Csi;
                ResetCsi();
                return;
        }

        if (c < 0x20 || (c >= 0x7F && c <= 0x9F))
        {
            return; // Other C0 controls and DEL are dropped.
        }

        AppendChar(c, completed);
    }

    private void ProcessEscape(char c, List<OutputLine> completed)
    {
        switch (c)
        {
            case '[':
                _state = State.Csi;
                ResetCsi();
                return;
            case ']': // OSC
            case 'P': // DCS
            case 'X': // SOS
            case '^': // PM
            case '_': // APC
                _state = State.StringSeq;
                return;
            case Esc:
                return; // A new escape restarts the sequence.
        }

        if (c >= 0x20 && c <= 0x2F)
        {
            _state = State.EscapeIntermediate;
        }
        else if (c >= 0x30 && c <= 0x7E)
        {
            _state = State.Normal; // Two-character sequence such as ESC = or ESC ( B: dropped.
        }
        else
        {
            _state = State.Normal; // Malformed: abandon the sequence and handle the character normally.
            ProcessNormal(c, completed);
        }
    }

    private void ProcessEscapeIntermediate(char c, List<OutputLine> completed)
    {
        if (c >= 0x20 && c <= 0x2F)
        {
            return;
        }

        if (c >= 0x30 && c <= 0x7E)
        {
            _state = State.Normal;
            return;
        }

        _state = State.Normal;
        ProcessNormal(c, completed);
    }

    private void ProcessCsi(char c, List<OutputLine> completed)
    {
        if (c >= 0x30 && c <= 0x3F)
        {
            if (_csiParams.Length < MaxCsiParamLength)
            {
                _csiParams.Append(c);
            }
            else
            {
                _csiOverflow = true;
            }

            return;
        }

        if (c >= 0x20 && c <= 0x2F)
        {
            _csiHasIntermediate = true;
            return;
        }

        if (c >= 0x40 && c <= 0x7E)
        {
            if (c == 'm' && !_csiHasIntermediate && !_csiOverflow && IsSgrParamText())
            {
                ApplySgr(_csiParams.ToString());
            }

            _state = State.Normal; // Any other CSI sequence is dropped.
            return;
        }

        if (c == 0x7F)
        {
            return;
        }

        // ESC, a C0 control, or a non-ASCII character cannot appear in a CSI: abandon it and handle the character normally.
        _state = State.Normal;
        ProcessNormal(c, completed);
    }

    /// <summary>Drops an OSC, DCS, SOS, PM or APC payload up to BEL or ST.</summary>
    private void ProcessString(char c, List<OutputLine> completed)
    {
        switch (c)
        {
            case Bel:
            case StringTerminatorC1:
                _state = State.Normal;
                return;
            case Esc:
                _state = State.StringEscape;
                return;
            case '\n':
                // An unterminated string must not swallow the rest of the stream.
                _state = State.Normal;
                ProcessNormal(c, completed);
                return;
            default:
                return; // Payload is dropped.
        }
    }

    private void ProcessStringEscape(char c, List<OutputLine> completed)
    {
        if (c == '\\' || c == StringTerminatorC1)
        {
            _state = State.Normal; // ST terminates the string.
            return;
        }

        // ESC inside a string that is not followed by a backslash starts a new escape sequence.
        _state = State.Escape;
        ProcessEscape(c, completed);
    }

    private void ResetCsi()
    {
        _csiParams.Clear();
        _csiHasIntermediate = false;
        _csiOverflow = false;
    }

    private bool IsSgrParamText()
    {
        for (int i = 0; i < _csiParams.Length; i++)
        {
            char c = _csiParams[i];
            if (!(c >= '0' && c <= '9') && c != ';' && c != ':')
            {
                return false;
            }
        }

        return true;
    }

    private void ApplySgr(string parameters)
    {
        string[] groups = parameters.Split(';');
        for (int i = 0; i < groups.Length; i++)
        {
            int code = ParseNumber(groups[i].Split(':')[0]);
            if (code == 38 || code == 48)
            {
                i = ApplyExtendedColor(code == 38, groups, i);
            }
            else
            {
                ApplySimpleSgr(code);
            }
        }
    }

    /// <summary>Handles 38/48 in either semicolon or colon form. Returns the index of the last group consumed.</summary>
    private int ApplyExtendedColor(bool foreground, string[] groups, int i)
    {
        string[] sub = groups[i].Split(':');
        if (sub.Length > 1)
        {
            // Colon form: 38:5:n, 38:2:r:g:b, 38:2::r:g:b, 38:2:cs:r:g:b. The colorspace id is skipped when present.
            int mode = ParseNumber(sub[1]);
            if (mode == 5)
            {
                if (sub.Length > 2)
                {
                    SetIndexed(foreground, ParseNumber(sub[2]));
                }
            }
            else if (mode == 2)
            {
                int count = sub.Length - 2;
                if (count == 3)
                {
                    SetRgb(foreground, sub[2], sub[3], sub[4]);
                }
                else if (count == 4)
                {
                    SetRgb(foreground, sub[3], sub[4], sub[5]);
                }
            }

            return i;
        }

        // Semicolon form: 38;5;n or 38;2;r;g;b.
        if (i + 1 >= groups.Length)
        {
            return i;
        }

        int semicolonMode = ParseNumber(groups[i + 1]);
        if (semicolonMode == 5)
        {
            if (i + 2 < groups.Length)
            {
                SetIndexed(foreground, ParseNumber(groups[i + 2]));
            }

            return Math.Min(i + 2, groups.Length - 1);
        }

        if (semicolonMode == 2)
        {
            if (i + 4 < groups.Length)
            {
                SetRgb(foreground, groups[i + 2], groups[i + 3], groups[i + 4]);
            }

            return Math.Min(i + 4, groups.Length - 1);
        }

        return i + 1;
    }

    private void ApplySimpleSgr(int code)
    {
        switch (code)
        {
            case 0:
                _style = SpanStyle.Default;
                break;
            case 1:
                AddAttrs(TextAttrs.Bold);
                break;
            case 2:
                AddAttrs(TextAttrs.Dim);
                break;
            case 3:
                AddAttrs(TextAttrs.Italic);
                break;
            case 4:
                AddAttrs(TextAttrs.Underline);
                break;
            case 5:
                AddAttrs(TextAttrs.Blink);
                break;
            case 7:
                AddAttrs(TextAttrs.Inverse);
                break;
            case 9:
                AddAttrs(TextAttrs.Strikethrough);
                break;
            case 22:
                RemoveAttrs(TextAttrs.Bold | TextAttrs.Dim);
                break;
            case 23:
                RemoveAttrs(TextAttrs.Italic);
                break;
            case 24:
                RemoveAttrs(TextAttrs.Underline);
                break;
            case 25:
                RemoveAttrs(TextAttrs.Blink);
                break;
            case 27:
                RemoveAttrs(TextAttrs.Inverse);
                break;
            case 29:
                RemoveAttrs(TextAttrs.Strikethrough);
                break;
            case >= 30 and <= 37:
                _style = _style with { Foreground = AnsiColor.FromIndex((byte)(code - 30)) };
                break;
            case 39:
                _style = _style with { Foreground = AnsiColor.Default };
                break;
            case >= 40 and <= 47:
                _style = _style with { Background = AnsiColor.FromIndex((byte)(code - 40)) };
                break;
            case 49:
                _style = _style with { Background = AnsiColor.Default };
                break;
            case >= 90 and <= 97:
                _style = _style with { Foreground = AnsiColor.FromIndex((byte)(code - 82)) };
                break;
            case >= 100 and <= 107:
                _style = _style with { Background = AnsiColor.FromIndex((byte)(code - 92)) };
                break;
            default:
                break; // Unknown codes are ignored.
        }
    }

    private void AddAttrs(TextAttrs attrs) => _style = _style with { Attrs = _style.Attrs | attrs };

    private void RemoveAttrs(TextAttrs attrs) => _style = _style with { Attrs = _style.Attrs & ~attrs };

    private void SetIndexed(bool foreground, int index)
    {
        if (index < 0 || index > 255)
        {
            return; // Out-of-range: this color spec is ignored.
        }

        SetColor(foreground, AnsiColor.FromIndex((byte)index));
    }

    private void SetRgb(bool foreground, string r, string g, string b)
    {
        int rv = ParseNumber(r);
        int gv = ParseNumber(g);
        int bv = ParseNumber(b);
        if (!IsByte(rv) || !IsByte(gv) || !IsByte(bv))
        {
            return;
        }

        SetColor(foreground, AnsiColor.FromRgb((byte)rv, (byte)gv, (byte)bv));
    }

    private void SetColor(bool foreground, AnsiColor color)
    {
        _style = foreground
            ? _style with { Foreground = color }
            : _style with { Background = color };
    }

    private static bool IsByte(int value) => value >= 0 && value <= 255;

    /// <summary>Parses a decimal number. Empty means 0. Returns -1 for anything non-numeric or too long.</summary>
    private static int ParseNumber(string s)
    {
        if (s.Length == 0)
        {
            return 0;
        }

        if (s.Length > 9)
        {
            return -1;
        }

        int value = 0;
        foreach (char c in s)
        {
            if (c < '0' || c > '9')
            {
                return -1;
            }

            value = (value * 10) + (c - '0');
        }

        return value;
    }

    private void Backspace()
    {
        if (_overwritePending || _length == 0)
        {
            return;
        }

        int removeLength = _length >= 2 && char.IsLowSurrogate(_chars[_length - 1]) && char.IsHighSurrogate(_chars[_length - 2]) ? 2 : 1;
        _length -= removeLength;
        DropRunsFromLength();

        ResetWidthCheckpointIfAbove();
    }

    /// <summary>
    /// Pads to the next tab stop: the next multiple of eight display columns. The width of the text before the tab is
    /// found from the checkpoint, so only text appended since the previous tab is measured.
    /// </summary>
    private void AppendTab(List<OutputLine> completed)
    {
        int columns = MeasureLineColumns();
        int spaces = 8 - (columns % 8);

        int completedBefore = completed.Count;
        for (int i = 0; i < spaces; i++)
        {
            AppendChar(' ', completed);
        }

        // A forced break in the middle of the spaces leaves only the spaces written after it on the current line.
        // Otherwise the line holds the measured text plus the spaces. Each space is one column.
        bool broke = completed.Count != completedBefore;
        SetWidthCheckpoint(_length, broke ? _length : columns + spaces);
    }

    /// <summary>Display columns of the current line. Measures only the text after the checkpoint, then moves the checkpoint to the end.</summary>
    private int MeasureLineColumns()
    {
        int length = _length;
        int columns = _widthCheckpointColumns;
        if (length > _widthCheckpointLength)
        {
            columns += DisplayWidth.Of(new ReadOnlySpan<char>(_chars, _widthCheckpointLength, length - _widthCheckpointLength));
        }

        SetWidthCheckpoint(length, columns);
        return columns;
    }

    private void SetWidthCheckpoint(int length, int columns)
    {
        _widthCheckpointLength = length;
        _widthCheckpointColumns = columns;
    }

    private void ResetWidthCheckpointIfAbove()
    {
        if (_widthCheckpointLength > _length)
        {
            SetWidthCheckpoint(0, 0);
        }
    }

    private void AppendChar(char c, List<OutputLine> completed)
    {
        ClearPendingOverwrite();

        // The line is completed before a character that would exceed the limit, so a line of exactly maxLineLength characters
        // followed by a newline yields one line. A character that continues the last grapheme (the low half of a surrogate
        // pair, a combining mark, a joiner...) takes that whole grapheme to the new line, so a grapheme is not split. When the
        // grapheme is the whole line it cannot move: a surrogate pair then stays together (the line exceeds the limit by one
        // code unit), and anything longer is split.
        //
        // A high surrogate cannot be classified until its low surrogate arrives, so at the limit it is appended without a
        // decision and the line exceeds the limit by one code unit for a moment. The low surrogate then decides with the whole
        // scalar. A lone high surrogate (not followed by a low one) is decided by the next character, so the line can still
        // exceed the limit by that one unit; a second high surrogate right after one is decided at once, so it cannot exceed it by more.
        if (_length >= _maxLineLength && !DefersHighSurrogate(c))
        {
            int graphemeStart = StartOfGraphemeContinuedBy(c);
            if (graphemeStart > 0)
            {
                MoveTailToNewLine(graphemeStart, completed);
            }
            else if (graphemeStart == 0 && char.IsLowSurrogate(c) && char.IsHighSurrogate(_chars[_length - 1]) && _length > 1)
            {
                // The line is one grapheme that ends with a high surrogate: it is split before that surrogate, so the pair stays whole.
                MoveTailToNewLine(_length - 1, completed);
            }
            else if (!(graphemeStart == 0 && _length == 1 && char.IsLowSurrogate(c) && char.IsHighSurrogate(_chars[0])))
            {
                CompleteLine(completed);
            }
        }

        EnsureWritable();
        if (_length == _chars.Length)
        {
            Replace(_chars.Length * 2, _runs.Length);
        }

        if (_runCount == 0 || _runs[_runCount - 1].Style != _style)
        {
            AddRun(_length, _style);
        }

        _chars[_length++] = c;
    }

    /// <summary>
    /// True when <paramref name="c"/> is a high surrogate arriving at the limit, after a unit that is not a high surrogate: its
    /// break decision waits for the low surrogate (see <see cref="AppendChar"/>).
    /// </summary>
    private bool DefersHighSurrogate(char c) =>
        char.IsHighSurrogate(c) && _length > 0 && !char.IsHighSurrogate(_chars[_length - 1]);

    /// <summary>Longest grapheme tail examined at a forced break; a longer grapheme is split there.</summary>
    private const int MaxCarriedGrapheme = 64;

    /// <summary>
    /// The index where the line's last grapheme starts if <paramref name="c"/> would continue it, or -1 when <paramref name="c"/>
    /// starts a new grapheme (or the grapheme is longer than <see cref="MaxCarriedGrapheme"/>).
    /// </summary>
    private int StartOfGraphemeContinuedBy(char c)
    {
        if (_length == 0)
        {
            return -1;
        }

        int windowStart = Math.Max(0, _length - MaxCarriedGrapheme);
        var window = new string(_chars, windowStart, _length - windowStart) + c;
        int start = 0;
        int lastStart = 0;
        while (start < window.Length)
        {
            lastStart = start;
            start += System.Globalization.StringInfo.GetNextTextElementLength(window, start);
        }

        // c starts its own grapheme when the last one begins at c; a grapheme reaching back to the window's start may be longer.
        if (lastStart == window.Length - 1 || (lastStart == 0 && windowStart > 0))
        {
            return -1;
        }

        return windowStart + lastStart;
    }

    /// <summary>Completes the line before <paramref name="from"/>, and starts the next line with the characters after it, in their styles.</summary>
    private void MoveTailToNewLine(int from, List<OutputLine> completed)
    {
        var carried = new List<(char Char, SpanStyle Style)>(_length - from);
        for (int i = from; i < _length; i++)
        {
            carried.Add((_chars[i], StyleAt(i)));
        }

        _length = from;
        DropRunsFromLength();
        ResetWidthCheckpointIfAbove();
        CompleteLine(completed);

        SpanStyle currentStyle = _style;
        foreach ((char ch, SpanStyle style) in carried)
        {
            _style = style;
            AppendChar(ch, completed);
        }

        _style = currentStyle;
    }

    /// <summary>The style of the character at <paramref name="index"/>: that of the last run starting at or before it.</summary>
    private SpanStyle StyleAt(int index)
    {
        for (int r = _runCount - 1; r >= 0; r--)
        {
            if (_runs[r].Start <= index)
            {
                return _runs[r].Style;
            }
        }

        return _style;
    }

    /// <summary>Removes runs that start at or after the current length (their text was removed).</summary>
    private void DropRunsFromLength()
    {
        while (_runCount > 0 && _runs[_runCount - 1].Start >= _length)
        {
            _runCount--;
        }
    }

    private void AddRun(int start, SpanStyle style)
    {
        if (_runCount == _runs.Length)
        {
            Replace(_chars.Length, _runs.Length * 2);
        }

        _runs[_runCount++] = (start, style);
    }

    /// <summary>
    /// Called before writing at <see cref="_length"/> or <see cref="_runCount"/>. If a published snapshot covers that index,
    /// the kept prefix moves to fresh arrays first, so the snapshot keeps its text.
    /// </summary>
    private void EnsureWritable()
    {
        if (_shared && (_length < _publishedLength || _runCount < _publishedRuns))
        {
            Replace(_chars.Length, _runs.Length);
        }
    }

    /// <summary>
    /// Copies the kept prefix of the current line into new arrays of the given capacities. The old arrays are left as they
    /// are, so any snapshot referring to them is unaffected.
    /// </summary>
    private void Replace(int charCapacity, int runCapacity)
    {
        var chars = new char[charCapacity];
        Array.Copy(_chars, chars, _length);
        var runs = new (int Start, SpanStyle Style)[runCapacity];
        Array.Copy(_runs, runs, _runCount);

        _chars = chars;
        _runs = runs;
        _shared = false;
        _publishedLength = 0;
        _publishedRuns = 0;
    }

    /// <summary>Applies a pending bare CR: the existing content is discarded when new content is written.</summary>
    private void ClearPendingOverwrite()
    {
        if (_overwritePending)
        {
            _overwritePending = false;
            ClearLine();
        }
    }

    private void ClearLine()
    {
        _identity = OutputLine.NewIdentity();
        _length = 0;
        _runCount = 0;
        if (_shared)
        {
            // The published arrays keep their text; the parser continues in fresh ones.
            Replace(InitialCapacity, InitialRunCapacity);
        }

        SetWidthCheckpoint(0, 0);
    }

    private void CompleteLine(List<OutputLine> completed)
    {
        completed.Add(BuildLine());
        ClearLine();
        _overwritePending = false;
    }

    private OutputLine BuildLine()
    {
        var spans = new StyledSpan[_runCount];
        for (int i = 0; i < _runCount; i++)
        {
            int start = _runs[i].Start;
            int end = i + 1 < _runCount ? _runs[i + 1].Start : _length;
            spans[i] = new StyledSpan(new string(_chars, start, end - start), _runs[i].Style);
        }

        return new OutputLine(_source, spans, _identity);
    }
}
