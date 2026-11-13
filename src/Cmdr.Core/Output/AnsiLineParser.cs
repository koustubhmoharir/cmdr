using System.Text;

namespace Cmdr.Core.Output;

/// <summary>
/// Turns a stream of text chunks, possibly containing ANSI escape sequences, into <see cref="OutputLine"/>s.
/// SGR sequences are applied to the style of subsequent text; all other escape sequences are dropped.
/// Escape sequences may be split across chunks at any character.
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

    private readonly record struct Run(int Start, SpanStyle Style);

    private readonly OutputSource _source;
    private readonly int _maxLineLength;
    private readonly StringBuilder _text = new();
    private readonly List<Run> _runs = new();
    private readonly StringBuilder _csiParams = new();

    private SpanStyle _style = SpanStyle.Default;
    private State _state = State.Normal;
    private bool _pendingCr;
    private bool _overwritePending;
    private bool _csiHasIntermediate;
    private bool _csiOverflow;

    /// <summary>Creates a parser for one output stream.</summary>
    /// <param name="source">The source tagged on every line this parser produces.</param>
    /// <param name="maxLineLength">A line reaching this many UTF-16 code units is completed as if a newline was seen. A supplementary scalar is kept intact, so a limit of one may be exceeded by one code unit.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxLineLength"/> is not positive.</exception>
    public AnsiLineParser(OutputSource source, int maxLineLength = 65536)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLineLength);
        _source = source;
        _maxLineLength = maxLineLength;
    }

    /// <summary>The current incomplete line, or null if it is empty.</summary>
    public OutputLine? Partial => _text.Length == 0 ? null : BuildLine();

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

        if (_text.Length > 0)
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
                int spaces = 8 - (_text.Length % 8);
                for (int i = 0; i < spaces; i++)
                {
                    AppendChar(' ', completed);
                }

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
        if (_overwritePending || _text.Length == 0)
        {
            return;
        }

        int removeLength = _text.Length >= 2 && char.IsLowSurrogate(_text[^1]) && char.IsHighSurrogate(_text[^2]) ? 2 : 1;
        _text.Length -= removeLength;
        while (_runs.Count > 0 && _runs[^1].Start >= _text.Length)
        {
            _runs.RemoveAt(_runs.Count - 1);
        }
    }

    private void AppendChar(char c, List<OutputLine> completed)
    {
        ClearPendingOverwrite();

        bool completesPair = char.IsLowSurrogate(c) && _text.Length > 0 && char.IsHighSurrogate(_text[^1]);
        if (completesPair && _text.Length > 1 && _text.Length >= _maxLineLength)
        {
            SpanStyle pairStyle = _runs[^1].Style;
            SpanStyle currentStyle = _style;
            char high = _text[^1];
            _text.Length--;
            while (_runs.Count > 0 && _runs[^1].Start >= _text.Length)
            {
                _runs.RemoveAt(_runs.Count - 1);
            }

            CompleteLine(completed);
            _style = pairStyle;
            AppendChar(high, completed);
            _style = currentStyle;
        }

        // The line is completed before a character that would exceed the limit, so a line of exactly
        // maxLineLength characters followed by a newline yields one line.
        completesPair = char.IsLowSurrogate(c) && _text.Length > 0 && char.IsHighSurrogate(_text[^1]);
        if (_text.Length >= _maxLineLength && !completesPair)
        {
            CompleteLine(completed);
        }

        if (_runs.Count == 0 || _runs[^1].Style != _style)
        {
            _runs.Add(new Run(_text.Length, _style));
        }

        _text.Append(c);
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
        _text.Clear();
        _runs.Clear();
    }

    private void CompleteLine(List<OutputLine> completed)
    {
        completed.Add(BuildLine());
        ClearLine();
        _overwritePending = false;
    }

    private OutputLine BuildLine()
    {
        var spans = new StyledSpan[_runs.Count];
        for (int i = 0; i < _runs.Count; i++)
        {
            int start = _runs[i].Start;
            int end = i + 1 < _runs.Count ? _runs[i + 1].Start : _text.Length;
            spans[i] = new StyledSpan(_text.ToString(start, end - start), _runs[i].Style);
        }

        return new OutputLine(_source, spans);
    }
}
