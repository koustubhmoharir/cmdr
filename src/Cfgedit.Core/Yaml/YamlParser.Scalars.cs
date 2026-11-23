using System.Text;

namespace Cfgedit.Core.Yaml;

/// <summary>
/// Scalars: the dispatch on the first character of a node, the flow scalars (plain, single- and double-quoted) with line folding
/// and escapes, and the block scalars (<c>|</c> and <c>&gt;</c>) with indentation indicators and chomping.
/// </summary>
internal sealed partial class YamlParser
{
    /// <summary>
    /// A line of a block scalar: its text after the content indentation, and whether it is empty. Every line ends with a line feed in the
    /// value, including a last line with no line break in the source: YAML's end-of-input rule for chomping. Only strip removes it.
    /// </summary>
    private readonly record struct BlockLine(string Text, bool Empty);

    /// <summary>
    /// Parses the node at the current position, after its properties: an alias, a collection, a quoted, plain or block scalar.
    /// Block scalars are refused in flow context.
    /// </summary>
    private YamlValueNode ParseNode(int n, Props props, bool inFlow)
    {
        char c = _s[_pos];
        switch (c)
        {
            case '*':
                if (props.Any)
                {
                    throw Fail(_pos, "an alias cannot have an anchor or a tag");
                }

                return ParseAlias();
            case '[':
            case '{':
                return ParseFlowCollection(n, props);
            case '"':
                return ParseFlowScalar(YamlScalarStyle.DoubleQuoted, n, props, inFlow);
            case '\'':
                return ParseFlowScalar(YamlScalarStyle.SingleQuoted, n, props, inFlow);
            case '|':
            case '>':
                if (inFlow)
                {
                    throw Fail(_pos, "a block scalar cannot be inside a flow collection");
                }

                return ParseBlockScalar(n, props);
        }

        if (IsExplicitKeyAt(_pos))
        {
            throw Fail(_pos, "complex keys (?) are not supported");
        }

        if (IsPlainFirst(_pos, inFlow))
        {
            return ParseFlowScalar(YamlScalarStyle.Plain, n, props, inFlow);
        }

        throw Fail(_pos, $"unexpected character '{c}'");
    }

    private YamlScalar ParseFlowScalar(YamlScalarStyle style, int n, Props props, bool inFlow)
    {
        int start = _pos;
        var sb = new StringBuilder();
        int end = ScanFlowScalar(start, style, n, inFlow, singleLine: false, sb);
        var parts = new List<YamlNode>(props.Parts) { Token(YamlTokenKind.Scalar, start, end) };
        _pos = end;
        CloseAnchor(props);
        return new YamlScalar(parts, props.Anchor, props.Tag, style, sb.ToString(), isEmpty: false);
    }

    /// <summary>
    /// Scans a plain, single-quoted or double-quoted scalar that starts at <paramref name="start"/>, decodes it into
    /// <paramref name="sb"/>, and returns the offset just after it. A plain scalar ends before its trailing blanks, and a quoted one
    /// after its closing quote. Line breaks fold as YAML 1.2 says: one break becomes a space, and each empty line becomes a line
    /// feed. A line break in a single-line key is refused.
    /// </summary>
    /// <param name="n">The indentation of the enclosing block. A continuation line must be indented more than this.</param>
    /// <param name="inFlow">True inside a flow collection, where the flow indicators end a plain scalar.</param>
    /// <param name="singleLine">True for an implicit key: a plain scalar stops at a line break, and a quoted one may not span lines.</param>
    private int ScanFlowScalar(int start, YamlScalarStyle style, int n, bool inFlow, bool singleLine, StringBuilder sb)
    {
        bool quoted = style != YamlScalarStyle.Plain;
        char quote = style == YamlScalarStyle.DoubleQuoted ? '"' : '\'';
        int i = quoted ? start + 1 : start;
        int lastContentEnd = start;
        var pending = new StringBuilder();
        int breaks = 0;
        bool escapedBreak = false;

        // Emits what the pending blanks and line breaks come to before the next content, and clears them.
        void Flush()
        {
            if (breaks > 0 || escapedBreak)
            {
                if (escapedBreak)
                {
                    sb.Append('\n', breaks);
                }
                else if (breaks == 1)
                {
                    sb.Append(' ');
                }
                else
                {
                    sb.Append('\n', breaks - 1);
                }
            }
            else
            {
                sb.Append(pending);
            }

            breaks = 0;
            escapedBreak = false;
            pending.Clear();
        }

        while (true)
        {
            if (i >= Length)
            {
                if (quoted)
                {
                    throw Fail(start, style == YamlScalarStyle.DoubleQuoted ? "unterminated double-quoted string" : "unterminated single-quoted string");
                }

                return lastContentEnd;
            }

            char c = _s[i];
            if (IsBreak(c))
            {
                if (quoted)
                {
                    if (singleLine)
                    {
                        throw Fail(start, "a key that spans lines is not supported");
                    }

                    (int count, int content) = QuotedLineBreak(i, n, start, style);
                    breaks += count;
                    pending.Clear();
                    i = content;
                    continue;
                }

                if (singleLine)
                {
                    return lastContentEnd;
                }

                int resume = PlainContinuation(i, n, inFlow, out int plainBreaks);
                if (resume < 0)
                {
                    return lastContentEnd;
                }

                breaks += plainBreaks;
                pending.Clear();
                i = resume;
                continue;
            }

            if (IsBlank(c))
            {
                pending.Append(c);
                i++;
                continue;
            }

            if (!quoted)
            {
                bool colonEnds = c == ':'
                    && (i + 1 >= Length || IsBlankOrBreakOrEnd(i + 1) || (inFlow && IsFlowIndicator(_s[i + 1])));
                if (colonEnds || (c == '#' && pending.Length > 0) || (inFlow && IsFlowIndicator(c)))
                {
                    return lastContentEnd;
                }
            }
            else if (c == quote)
            {
                if (style == YamlScalarStyle.SingleQuoted && i + 1 < Length && _s[i + 1] == '\'')
                {
                    Flush();
                    sb.Append('\'');
                    i += 2;
                    continue;
                }

                Flush();
                return i + 1;
            }
            else if (c == '\\' && style == YamlScalarStyle.DoubleQuoted)
            {
                if (i + 1 >= Length)
                {
                    throw Fail(start, "unterminated double-quoted string");
                }

                Flush();
                if (IsBreak(_s[i + 1]))
                {
                    // An escaped line break joins the lines: no space is folded in, and the blanks that start the next line go.
                    (int count, int content) = QuotedLineBreak(i + 1, n, start, style);
                    breaks = count - 1;
                    escapedBreak = true;
                    i = content;
                    continue;
                }

                (string decoded, int length) = DecodeEscape(i);
                sb.Append(decoded);
                i += length;
                lastContentEnd = i;
                continue;
            }

            Flush();
            sb.Append(c);
            i++;
            lastContentEnd = i;
        }
    }

    /// <summary>
    /// Reads a line break inside a quoted scalar, and any empty lines after it. Returns how many breaks there were, and the offset of
    /// the first content on the line that follows. The content must be indented more than <paramref name="n"/>, and no document marker
    /// may start it.
    /// </summary>
    private (int Count, int Content) QuotedLineBreak(int breakPos, int n, int start, YamlScalarStyle style)
    {
        int count = 0;
        int j = breakPos;
        int k;
        while (true)
        {
            j = BreakEnd(j);
            count++;
            k = SkipBlanks(j);
            if (k < Length && IsBreak(_s[k]))
            {
                j = k;
                continue;
            }

            break;
        }

        if (k >= Length)
        {
            throw Fail(start, style == YamlScalarStyle.DoubleQuoted ? "unterminated double-quoted string" : "unterminated single-quoted string");
        }

        if (IsDocumentMarkerAt(j))
        {
            throw Fail(j, "a document marker cannot be inside a quoted scalar");
        }

        if (SpacesFrom(j) <= n)
        {
            throw Fail(k, "bad indentation of a quoted scalar");
        }

        return (count, k);
    }

    /// <summary>
    /// Looks at the line after a break inside a plain scalar. Returns the offset of the continuation's first character, with the number
    /// of breaks it crosses in <paramref name="count"/>, or -1 when the plain scalar ends before the break. A continuation must be
    /// indented more than <paramref name="n"/> and must not be a comment, a document marker, or a flow indicator in flow context.
    /// </summary>
    private int PlainContinuation(int breakPos, int n, bool inFlow, out int count)
    {
        count = 0;
        int j = breakPos;
        while (true)
        {
            j = BreakEnd(j);
            count++;
            int k = SkipBlanks(j);
            if (k >= Length)
            {
                return -1;
            }

            if (IsBreak(_s[k]))
            {
                j = k;
                continue;
            }

            if (SpacesFrom(j) <= n || IsDocumentMarkerAt(j) || _s[k] == '#' || (inFlow && IsFlowIndicator(_s[k])))
            {
                return -1;
            }

            return k;
        }
    }

    /// <summary>The number of spaces (not tabs) that start at the offset.</summary>
    private int SpacesFrom(int i)
    {
        int count = 0;
        while (i + count < Length && _s[i + count] == ' ')
        {
            count++;
        }

        return count;
    }

    /// <summary>Decodes the escape that starts at the backslash at <paramref name="i"/>. Returns its text and its length in the source.</summary>
    private (string Text, int Length) DecodeEscape(int i)
    {
        char e = _s[i + 1];
        switch (e)
        {
            case '0': return ("\0", 2);
            case 'a': return ("\a", 2);
            case 'b': return ("\b", 2);
            case 't':
            case '\t': return ("\t", 2);
            case 'n': return ("\n", 2);
            case 'v': return ("\v", 2);
            case 'f': return ("\f", 2);
            case 'r': return ("\r", 2);
            case 'e': return ("\u001B", 2);
            case ' ': return (" ", 2);
            case '"': return ("\"", 2);
            case '/': return ("/", 2);
            case '\\': return ("\\", 2);
            case 'N': return ("\u0085", 2);
            case '_': return ("\u00A0", 2);
            case 'L': return ("\u2028", 2);
            case 'P': return ("\u2029", 2);
            case 'x': return (HexEscape(i, 2), 4);
            case 'u': return (HexEscape(i, 4), 6);
            case 'U': return (HexEscape(i, 8), 10);
            default:
                throw Fail(i, $"invalid escape '\\{e}'");
        }
    }

    private string HexEscape(int i, int digits)
    {
        if (i + 2 + digits > Length)
        {
            throw Fail(i, "an escape needs its hex digits");
        }

        int code = 0;
        for (int k = 0; k < digits; k++)
        {
            char d = _s[i + 2 + k];
            if (!char.IsAsciiHexDigit(d))
            {
                throw Fail(i + 2 + k, $"'{d}' is not a hex digit");
            }

            code = (code << 4) + Convert.ToInt32(d.ToString(), 16);
        }

        if (code is >= 0xD800 and <= 0xDFFF or > 0x10FFFF)
        {
            throw Fail(i, "an escape is not a valid unicode character");
        }

        return char.ConvertFromUtf32(code);
    }

    // ----- Block scalars -----

    private YamlScalar ParseBlockScalar(int n, Props props)
    {
        int start = _pos;
        bool literal = _s[_pos] == '|';
        _pos++;
        int indicator = 0;
        char chomp = ' ';
        for (int k = 0; k < 2 && _pos < Length; k++)
        {
            char c = _s[_pos];
            if (c is >= '1' and <= '9' && indicator == 0)
            {
                indicator = c - '0';
                _pos++;
            }
            else if (c is '+' or '-' && chomp == ' ')
            {
                chomp = c;
                _pos++;
            }
            else
            {
                break;
            }
        }

        if (_pos < Length && _s[_pos] == '0')
        {
            throw Fail(_pos, "the indentation indicator of a block scalar must be 1 to 9");
        }

        var parts = new List<YamlNode>(props.Parts) { Token(YamlTokenKind.BlockHeader, start, _pos) };
        int afterHeader = SkipBlanks(_pos);
        if (afterHeader > _pos)
        {
            parts.Add(Whitespace(_pos, afterHeader));
            _pos = afterHeader;
        }

        if (_pos < Length && _s[_pos] == '#')
        {
            parts.Add(ReadComment());
        }

        if (_pos < Length)
        {
            if (!IsBreak(_s[_pos]))
            {
                throw Fail(_pos, "unexpected text after the block scalar header");
            }

            int headerBreakEnd = BreakEnd(_pos);
            parts.Add(LineBreakToken(_pos, headerBreakEnd));
            _pos = headerBreakEnd;
        }

        int bodyStart = _pos;
        int contentIndent = BlockContentIndent(n, indicator, bodyStart);
        List<BlockLine> lines = ScanBlockLines(bodyStart, contentIndent, out int bodyEnd);
        if (bodyEnd > bodyStart)
        {
            parts.Add(Token(YamlTokenKind.BlockBody, bodyStart, bodyEnd));
        }

        _pos = bodyEnd;
        CloseAnchor(props);
        YamlScalarStyle style = literal ? YamlScalarStyle.Literal : YamlScalarStyle.Folded;
        return new YamlScalar(parts, props.Anchor, props.Tag, style, BlockValue(lines, literal, chomp), isEmpty: false);
    }

    /// <summary>
    /// The content indentation of a block scalar: the parent's indentation plus the indicator, or, without an indicator, the indentation
    /// of the first non-empty line. Leading empty lines may not be more indented than that line.
    /// </summary>
    private int BlockContentIndent(int n, int indicator, int bodyStart)
    {
        if (indicator > 0)
        {
            return n + indicator;
        }

        int maxEmpty = 0;
        int i = bodyStart;
        while (i < Length && !IsDocumentMarkerAt(i))
        {
            int sp = SpacesFrom(i);
            int j = i + sp;
            if (j >= Length || IsBreak(_s[j]))
            {
                maxEmpty = Math.Max(maxEmpty, sp);
                i = j < Length ? BreakEnd(j) : Length;
                continue;
            }

            int indent = Math.Max(sp, n + 1);
            if (maxEmpty > indent)
            {
                throw Fail(i, "a leading line of spaces is more indented than the first line of the block scalar");
            }

            return indent;
        }

        return Math.Max(n + 1, maxEmpty);
    }

    /// <summary>
    /// Reads the lines of a block scalar: the lines at the content indentation or more, and the empty lines, up to the first line that
    /// is less indented, or a document marker. Trailing empty lines belong to the scalar, since <c>keep</c> chomping needs them.
    /// </summary>
    private List<BlockLine> ScanBlockLines(int bodyStart, int contentIndent, out int bodyEnd)
    {
        var lines = new List<BlockLine>();
        bodyEnd = bodyStart;
        int i = bodyStart;
        while (i < Length && !IsDocumentMarkerAt(i))
        {
            int sp = SpacesFrom(i);
            int j = i + sp;
            bool lineEnd = j >= Length || IsBreak(_s[j]);
            if (sp < contentIndent && j < Length && _s[j] == '\t' && IsTabOnlyLine(j))
            {
                throw Fail(j, "tabs are not allowed for indentation");
            }

            if (lineEnd && sp <= contentIndent)
            {
                int next = j < Length ? BreakEnd(j) : Length;
                lines.Add(new BlockLine(string.Empty, Empty: true));
                i = next;
                bodyEnd = next;
                continue;
            }

            if (!lineEnd && sp < contentIndent)
            {
                break;
            }

            int eol = j;
            while (eol < Length && !IsBreak(_s[eol]))
            {
                eol++;
            }

            string text = _s.Substring(i + contentIndent, eol - i - contentIndent);
            bool hasBreak = eol < Length;
            int nextLine = hasBreak ? BreakEnd(eol) : Length;
            lines.Add(new BlockLine(text, Empty: false));
            i = nextLine;
            bodyEnd = nextLine;
        }

        return lines;
    }

    /// <summary>
    /// The value of a block scalar. A literal keeps its lines. A folded scalar joins lines with spaces, except where a line is
    /// more indented or an empty line stands between them. Then the chomping indicator decides the trailing line breaks.
    /// </summary>
    private static string BlockValue(List<BlockLine> lines, bool literal, char chomp)
    {
        int last = -1;
        for (int k = 0; k < lines.Count; k++)
        {
            if (!lines[k].Empty)
            {
                last = k;
            }
        }

        var sb = new StringBuilder();
        if (last >= 0)
        {
            if (literal)
            {
                for (int k = 0; k <= last; k++)
                {
                    sb.Append(lines[k].Text).Append('\n');
                }
            }
            else
            {
                string? previous = null;
                int empties = 0;
                for (int k = 0; k <= last; k++)
                {
                    if (lines[k].Empty)
                    {
                        empties++;
                        continue;
                    }

                    string text = lines[k].Text;
                    if (previous is null)
                    {
                        sb.Append('\n', empties);
                    }
                    else if (!IsSpacedText(previous) && !IsSpacedText(text))
                    {
                        if (empties == 0)
                        {
                            sb.Append(' ');
                        }
                        else
                        {
                            sb.Append('\n', empties);
                        }
                    }
                    else
                    {
                        sb.Append('\n', empties + 1);
                    }

                    sb.Append(text);
                    previous = text;
                    empties = 0;
                }

                sb.Append('\n');
            }
        }

        string content = sb.ToString();
        switch (chomp)
        {
            case '-':
                return last >= 0 ? content[..^1] : content;
            case '+':
                var keep = new StringBuilder(content);
                for (int k = last + 1; k < lines.Count; k++)
                {
                    keep.Append('\n');
                }

                return keep.ToString();
            default:
                return content;
        }
    }

    /// <summary>A folded line that starts with a space or a tab is "more indented": the breaks around it are kept.</summary>
    private static bool IsSpacedText(string text) => text.Length > 0 && text[0] is ' ' or '\t';
}
