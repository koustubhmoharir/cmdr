using Cfgedit.Core.Text;

namespace Cfgedit.Core.Toml;

/// <summary>
/// Builds the <see cref="TomlDocument"/> tree from the text. It is recursive descent over the characters, one line at a time,
/// because TOML's meaning depends on context: the same characters are a key on the left of <c>=</c> and a value on its right.
/// Nesting of arrays and inline tables is capped at the JSON limit, so the recursion is bounded.
/// </summary>
internal sealed class TomlParser
{
    private const int MaxDepth = Cfgedit.Core.Json.JsonDocument.MaxDepth;

    private readonly SourceText _source;
    private readonly string _s;
    private int _pos;

    private TomlParser(SourceText source)
    {
        _source = source;
        _s = source.Text;
    }

    /// <exception cref="CfgeditException">The text is not valid TOML 1.0 syntax.</exception>
    internal static TomlDocument Parse(SourceText source)
    {
        var parser = new TomlParser(source);
        return new TomlDocument(source, parser.ParseLines());
    }

    private List<TomlItem> ParseLines()
    {
        var items = new List<TomlItem>();
        while (_pos < _s.Length)
        {
            items.Add(ParseLine());
        }

        return items;
    }

    private TomlItem ParseLine()
    {
        int start = _pos;
        var indent = new TomlSpan(start, SkipSpaces());
        if (_pos >= _s.Length || _s[_pos] is '\n' or '\r')
        {
            TomlSpan blankBreak = ReadLineEnd();
            return new TomlBlankItem(start, _pos, indent, blankBreak);
        }

        if (_s[_pos] == '#')
        {
            TomlComment comment = TomlStrings.ReadComment(_source, _pos);
            _pos = comment.End;
            TomlSpan commentBreak = ReadLineEnd();
            return new TomlCommentItem(start, _pos, indent, comment, commentBreak);
        }

        if (_s[_pos] == '[')
        {
            return ParseHeader(start, indent);
        }

        return ParseKeyValue(start, indent);
    }

    private TomlItem ParseKeyValue(int start, TomlSpan indent)
    {
        TomlKey key = ParseKey();
        TomlSpan assignment = ParseAssignment(key.End);
        TomlValue value = ParseValue(0);
        TomlSpan lineBreak = FinishLine(out TomlSpan trailing, out TomlComment? comment);
        return new TomlKeyValueItem(start, _pos, indent, key, assignment, value, trailing, comment, lineBreak);
    }

    private TomlItem ParseHeader(int start, TomlSpan indent)
    {
        string s = _s;
        bool isArray = _pos + 1 < s.Length && s[_pos + 1] == '[';
        int openLength = isArray ? 2 : 1;
        var openSpan = new TomlSpan(_pos, _pos + openLength);
        _pos += openLength;

        int afterOpen = _pos;
        SkipSpaces();
        var spaceAfterOpen = new TomlSpan(afterOpen, _pos);

        TomlKey key = ParseKey();
        _pos = key.End;
        int beforeClose = _pos;
        SkipSpaces();
        var spaceBeforeClose = new TomlSpan(beforeClose, _pos);

        int closeLength = isArray ? 2 : 1;
        for (int k = 0; k < closeLength; k++)
        {
            int at = _pos + k;
            if (at >= s.Length || s[at] != ']')
            {
                string expected = isArray ? "']]' to close the array table name" : "']' to close the table name";
                throw TomlStrings.Fail(_source, at, $"expected {expected} but found {Describe(at)}");
            }
        }

        var closeSpan = new TomlSpan(_pos, _pos + closeLength);
        _pos += closeLength;

        TomlSpan lineBreak = FinishLine(out TomlSpan trailing, out TomlComment? comment);
        if (isArray)
        {
            return new TomlArrayTableItem(
                start, _pos, indent, openSpan, spaceAfterOpen, key, spaceBeforeClose, closeSpan, trailing, comment, lineBreak);
        }

        return new TomlTableItem(
            start, _pos, indent, openSpan, spaceAfterOpen, key, spaceBeforeClose, closeSpan, trailing, comment, lineBreak);
    }

    /// <summary>
    /// A key: a simple key, then any number of dot-separated simple keys. The spaces around a dot belong to the separator. The
    /// spaces after the last part are not part of the key.
    /// </summary>
    private TomlKey ParseKey()
    {
        int start = _pos;
        var parts = new List<TomlKeyPart> { ParseSimpleKey(afterDot: false) };
        var separators = new List<TomlSpan>();
        while (true)
        {
            int beforeSpace = _pos;
            SkipSpaces();
            if (_pos >= _s.Length || _s[_pos] != '.')
            {
                _pos = beforeSpace;
                break;
            }

            _pos++;
            SkipSpaces();
            int previousEnd = parts[^1].End;
            TomlKeyPart part = ParseSimpleKey(afterDot: true);
            separators.Add(new TomlSpan(previousEnd, part.Start));
            parts.Add(part);
        }

        return new TomlKey(start, parts[^1].End, parts, separators);
    }

    private TomlKeyPart ParseSimpleKey(bool afterDot)
    {
        string s = _s;
        int start = _pos;
        if (_pos < s.Length && s[_pos] == '"')
        {
            TomlString quoted = TomlStrings.ReadBasic(_source, _pos);
            _pos = quoted.End;
            return new TomlKeyPart(quoted.Start, quoted.End, TomlKeyForm.Basic, quoted.Raw, quoted.Value);
        }

        if (_pos < s.Length && s[_pos] == '\'')
        {
            TomlString quoted = TomlStrings.ReadLiteral(_source, _pos);
            _pos = quoted.End;
            return new TomlKeyPart(quoted.Start, quoted.End, TomlKeyForm.Literal, quoted.Raw, quoted.Value);
        }

        int end = start;
        while (end < s.Length && IsBareKeyCharacter(s[end]))
        {
            end++;
        }

        if (end == start)
        {
            string expected = afterDot ? "a key after '.'" : "a key";
            throw TomlStrings.Fail(_source, _pos, $"expected {expected} but found {Describe(_pos)}");
        }

        _pos = end;
        string raw = s.Substring(start, end - start);
        return new TomlKeyPart(start, end, TomlKeyForm.Bare, raw, raw);
    }

    /// <summary>
    /// Reads <c>=</c> with its surrounding spaces, after a key that ends at <paramref name="keyEnd"/>. Returns the span from the
    /// key's end to the start of the value.
    /// </summary>
    private TomlSpan ParseAssignment(int keyEnd)
    {
        SkipSpaces();
        if (_pos >= _s.Length || _s[_pos] != '=')
        {
            throw TomlStrings.Fail(_source, _pos, $"expected '=' after the key but found {Describe(_pos)}");
        }

        _pos++;
        SkipSpaces();
        return new TomlSpan(keyEnd, _pos);
    }

    /// <summary>
    /// Parses a value. <paramref name="enclosing"/> is the number of arrays and inline tables around it, so an array or an inline
    /// table that starts here is at level <paramref name="enclosing"/> + 1.
    /// </summary>
    private TomlValue ParseValue(int enclosing)
    {
        string s = _s;
        if (_pos >= s.Length)
        {
            throw TomlStrings.Fail(_source, _pos, "expected a value but found end of file");
        }

        char c = s[_pos];
        if (c is '\n' or '\r')
        {
            throw TomlStrings.Fail(_source, _pos, "expected a value but found end of line");
        }

        switch (c)
        {
            case '"':
                {
                    TomlString value = IsTripleQuote(s, _pos, '"')
                        ? TomlStrings.ReadMultiLineBasic(_source, _pos)
                        : TomlStrings.ReadBasic(_source, _pos);
                    _pos = value.End;
                    return value;
                }

            case '\'':
                {
                    TomlString value = IsTripleQuote(s, _pos, '\'')
                        ? TomlStrings.ReadMultiLineLiteral(_source, _pos)
                        : TomlStrings.ReadLiteral(_source, _pos);
                    _pos = value.End;
                    return value;
                }

            case '[':
                return ParseArray(enclosing + 1);

            case '{':
                return ParseInlineTable(enclosing + 1);

            default:
                return ParseScalar();
        }
    }

    /// <summary>
    /// Reads a boolean, a number or a date-time. The text runs over letters, digits and <c>_ + - . :</c>. A date may be followed
    /// by a space and a time (the RFC 3339 form with a space in place of T).
    /// </summary>
    private TomlValue ParseScalar()
    {
        string s = _s;
        int start = _pos;
        int end = ScanScalarRun(start);
        if (LooksLikeDate(s, start, end) && end + 1 < s.Length && s[end] == ' ' && IsDigit(s[end + 1]))
        {
            end = ScanScalarRun(end + 1);
        }

        string raw = s.Substring(start, end - start);
        if (raw.Length == 0)
        {
            throw TomlStrings.Fail(_source, start, $"expected a value but found {Describe(start)}");
        }

        if (raw == "true" || raw == "false")
        {
            _pos = end;
            return new TomlBoolean(start, end, raw == "true");
        }

        if (IsDigit(raw[0]) && (raw.Contains(':') || (raw.Length > 4 && raw[4] == '-')))
        {
            string? dateError = TomlScalars.ValidateDateTime(raw, out TomlDateTimeKind kind);
            if (dateError is not null)
            {
                throw TomlStrings.Fail(_source, start, dateError);
            }

            _pos = end;
            return new TomlDateTime(start, end, raw, kind);
        }

        bool signed = raw[0] is '+' or '-';
        string unsignedText = signed ? raw.Substring(1) : raw;
        bool special = unsignedText.StartsWith("inf", StringComparison.Ordinal) || unsignedText.StartsWith("nan", StringComparison.Ordinal);
        if (special || (unsignedText.Length > 0 && IsDigit(unsignedText[0])))
        {
            bool radixPrefix = unsignedText.Length >= 2 && unsignedText[0] == '0' && unsignedText[1] is 'x' or 'o' or 'b';
            bool isFloat = special || (!radixPrefix && (raw.Contains('.') || raw.Contains('e') || raw.Contains('E')));
            _pos = end;
            if (isFloat)
            {
                string? floatError = TomlScalars.ValidateFloat(raw, out double number);
                if (floatError is not null)
                {
                    throw TomlStrings.Fail(_source, start, floatError);
                }

                return new TomlFloat(start, end, raw, number);
            }

            string? integerError = TomlScalars.ValidateInteger(raw, out long integer, out int radix);
            if (integerError is not null)
            {
                throw TomlStrings.Fail(_source, start, integerError);
            }

            return new TomlInteger(start, end, raw, integer, radix);
        }

        throw TomlStrings.Fail(_source, start, $"expected a value but found '{raw}'");
    }

    /// <summary>
    /// An array. Its items and its trivia (whitespace, newlines and comments) are entries in source order. A comma is optional
    /// after the last item and required between items.
    /// </summary>
    private TomlArray ParseArray(int level)
    {
        string s = _s;
        int start = _pos;
        CheckDepth(level, start);
        _pos++;

        var entries = new List<TomlNode>();
        bool needSeparator = false;
        while (true)
        {
            ReadArrayTrivia(entries);
            if (_pos >= s.Length)
            {
                throw TomlStrings.Fail(_source, _pos, "expected a value or ']' but found end of file");
            }

            if (s[_pos] == ']')
            {
                break;
            }

            if (needSeparator)
            {
                throw TomlStrings.Fail(_source, _pos, $"expected ',' or ']' but found {Describe(_pos)}");
            }

            TomlValue value = ParseValue(level);

            // Whitespace and comments may sit between a value and its comma. They are read as entries first; when a comma
            // follows, they move into the item, which then ends at the comma. Otherwise they stay entries of the array.
            int mark = entries.Count;
            ReadArrayTrivia(entries);
            List<TomlNode> beforeComma = entries.GetRange(mark, entries.Count - mark);
            TomlSpan? comma = null;
            if (_pos < s.Length && s[_pos] == ',')
            {
                entries.RemoveRange(mark, entries.Count - mark);
                comma = new TomlSpan(_pos, _pos + 1);
                _pos++;
                needSeparator = false;
            }
            else
            {
                beforeComma = [];
                needSeparator = true;
            }

            int itemEnd = comma?.End ?? value.End;
            entries.Insert(mark, new TomlArrayItem(value.Start, itemEnd, value, beforeComma, comma));
        }

        _pos++;
        return new TomlArray(start, _pos, entries);
    }

    /// <summary>
    /// An inline table, on one line. Its members and whitespace are entries in source order. A comma is required between members
    /// and not allowed after the last one.
    /// </summary>
    private TomlInlineTable ParseInlineTable(int level)
    {
        string s = _s;
        int start = _pos;
        CheckDepth(level, start);
        _pos++;

        var entries = new List<TomlNode>();
        bool any = false;
        bool lastHadComma = false;
        while (true)
        {
            int wsStart = _pos;
            SkipSpaces();
            if (_pos > wsStart)
            {
                entries.Add(new TomlWhitespace(wsStart, _pos));
            }

            if (_pos < s.Length && s[_pos] == '}')
            {
                if (any && lastHadComma)
                {
                    throw TomlStrings.Fail(_source, _pos, "a trailing comma is not allowed in an inline table");
                }

                break;
            }

            if (_pos >= s.Length)
            {
                throw TomlStrings.Fail(_source, start, "an inline table is not closed");
            }

            if (s[_pos] is '\n' or '\r')
            {
                throw TomlStrings.Fail(_source, _pos, "a newline is not allowed in an inline table");
            }

            if (any && !lastHadComma)
            {
                throw TomlStrings.Fail(_source, _pos, $"expected ',' or '}}' but found {Describe(_pos)}");
            }

            TomlKey key = ParseKey();
            TomlSpan assignment = ParseAssignment(key.End);
            TomlValue value = ParseValue(level);
            TomlSpan spaceBeforeComma = ReadSpaceBeforeComma(value.End, out TomlSpan? comma);
            if (comma is null)
            {
                _pos = value.End;
            }
            else
            {
                _pos = comma.Value.End;
            }

            int memberEnd = comma?.End ?? value.End;
            entries.Add(new TomlInlineMember(key.Start, memberEnd, key, assignment, value, spaceBeforeComma, comma));
            any = true;
            lastHadComma = comma is not null;
        }

        _pos++;
        return new TomlInlineTable(start, _pos, entries);
    }

    /// <summary>
    /// Looks past spaces after a value for a comma. Returns the spaces before the comma, and the comma when there is one. When
    /// there is none, the spaces are not consumed and the result is empty.
    /// </summary>
    private TomlSpan ReadSpaceBeforeComma(int valueEnd, out TomlSpan? comma)
    {
        int p = valueEnd;
        while (p < _s.Length && _s[p] is ' ' or '\t')
        {
            p++;
        }

        if (p < _s.Length && _s[p] == ',')
        {
            comma = new TomlSpan(p, p + 1);
            return new TomlSpan(valueEnd, p);
        }

        comma = null;
        return new TomlSpan(valueEnd, valueEnd);
    }

    /// <summary>
    /// Reads whitespace (spaces, tabs and line breaks) and comments between the entries of an array, adding them in order.
    /// </summary>
    private void ReadArrayTrivia(List<TomlNode> entries)
    {
        while (true)
        {
            int wsStart = _pos;
            SkipLayout();
            if (_pos > wsStart)
            {
                entries.Add(new TomlWhitespace(wsStart, _pos));
            }

            if (_pos < _s.Length && _s[_pos] == '#')
            {
                TomlComment comment = TomlStrings.ReadComment(_source, _pos);
                entries.Add(comment);
                _pos = comment.End;
                continue;
            }

            return;
        }
    }

    /// <summary>
    /// Ends a line after its content: reads the spaces, an optional comment, and the line break (or the end of the text). It
    /// returns the line break's span and sets <paramref name="trailing"/> to the spaces before the comment (or before the break).
    /// </summary>
    private TomlSpan FinishLine(out TomlSpan trailing, out TomlComment? comment)
    {
        int trailingStart = _pos;
        SkipSpaces();
        trailing = new TomlSpan(trailingStart, _pos);
        comment = null;
        if (_pos < _s.Length && _s[_pos] == '#')
        {
            comment = TomlStrings.ReadComment(_source, _pos);
            _pos = comment.End;
        }

        return ReadLineEnd();
    }

    /// <summary>Reads a line break, LF or CRLF, or accepts the end of the text. Anything else is an error.</summary>
    private TomlSpan ReadLineEnd()
    {
        if (_pos >= _s.Length)
        {
            return new TomlSpan(_pos, _pos);
        }

        int start = _pos;
        if (_s[_pos] == '\n')
        {
            _pos++;
            return new TomlSpan(start, _pos);
        }

        if (_s[_pos] == '\r')
        {
            if (_pos + 1 < _s.Length && _s[_pos + 1] == '\n')
            {
                _pos += 2;
                return new TomlSpan(start, _pos);
            }

            throw TomlStrings.Fail(_source, _pos, "a carriage return must be followed by a line feed");
        }

        throw TomlStrings.Fail(_source, _pos, $"expected end of line but found {Describe(_pos)}");
    }

    /// <summary>Skips spaces and tabs, and returns the offset after them.</summary>
    private int SkipSpaces()
    {
        while (_pos < _s.Length && _s[_pos] is ' ' or '\t')
        {
            _pos++;
        }

        return _pos;
    }

    /// <summary>Skips spaces, tabs, LF and CRLF. A lone carriage return is an error.</summary>
    private void SkipLayout()
    {
        while (_pos < _s.Length)
        {
            char c = _s[_pos];
            if (c is ' ' or '\t' or '\n')
            {
                _pos++;
            }
            else if (c == '\r')
            {
                if (_pos + 1 < _s.Length && _s[_pos + 1] == '\n')
                {
                    _pos += 2;
                }
                else
                {
                    throw TomlStrings.Fail(_source, _pos, "a carriage return must be followed by a line feed");
                }
            }
            else
            {
                return;
            }
        }
    }

    private void CheckDepth(int level, int offset)
    {
        if (level > MaxDepth)
        {
            throw TomlStrings.Fail(_source, offset, $"nesting is deeper than {MaxDepth} levels");
        }
    }

    private string Describe(int offset)
    {
        return TomlStrings.Describe(_s, offset);
    }

    private int ScanScalarRun(int i)
    {
        while (i < _s.Length && IsScalarCharacter(_s[i]))
        {
            i++;
        }

        return i;
    }

    private static bool IsScalarCharacter(char c)
    {
        return IsDigit(c) || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c is '_' or '+' or '-' or '.' or ':';
    }

    /// <summary>A bare key character: ASCII letters, digits, <c>-</c> and <c>_</c>.</summary>
    internal static bool IsBareKeyCharacter(char c)
    {
        return IsDigit(c) || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c is '_' or '-';
    }

    private static bool IsDigit(char c)
    {
        return c >= '0' && c <= '9';
    }

    private static bool IsTripleQuote(string s, int i, char quote)
    {
        return i + 2 < s.Length && s[i] == quote && s[i + 1] == quote && s[i + 2] == quote;
    }

    /// <summary>True for the text of a date in the form <c>dddd-dd-dd</c>, with the digits and dashes in place.</summary>
    private static bool LooksLikeDate(string s, int start, int end)
    {
        if (end - start != 10 || s[start + 4] != '-' || s[start + 7] != '-')
        {
            return false;
        }

        for (int i = start; i < end; i++)
        {
            if (i != start + 4 && i != start + 7 && !IsDigit(s[i]))
            {
                return false;
            }
        }

        return true;
    }
}
