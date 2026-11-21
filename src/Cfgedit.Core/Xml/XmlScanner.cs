using System.Globalization;
using System.Text;
using Cfgedit.Core.Text;

namespace Cfgedit.Core.Xml;

/// <summary>
/// What the scanner found: the nodes of the document and the facts the cross-check needs about the DOCTYPE.
/// </summary>
internal sealed class XmlScanResult
{
    public XmlDeclarationSyntax? Declaration { get; set; }

    public List<XmlNodeSyntax> Prolog { get; } = [];

    public XmlElementSyntax? Root { get; set; }

    public List<XmlNodeSyntax> Epilog { get; } = [];

    public XmlDoctypeSyntax? Doctype { get; set; }

    /// <summary>Every reference to a non-predefined entity in the document body, in document order, with its offset.</summary>
    public List<(string Name, int Offset)> EntityReferences { get; } = [];

    /// <summary>The general entities whose replacement text contains a '&lt;' once character references are expanded.</summary>
    public HashSet<string> MarkupEntities { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Scans the text of a document into its concrete syntax tree. It finds where things are; it does not decide validity.
/// XmlReader has already checked the text, so anything unexpected here is a bug in the scanner, and it is reported as an
/// internal parse mismatch rather than as a wrong tree.
/// </summary>
/// <remarks>
/// Element nesting is tracked with an explicit stack, so the depth of a document is not limited by the call stack.
/// </remarks>
internal sealed class XmlScanner
{
    private readonly SourceText _source;
    private readonly string _text;
    private readonly XmlScanResult _result = new();
    private int _pos;

    private XmlScanner(SourceText source)
    {
        _source = source;
        _text = source.Text;
    }

    /// <summary>Scans a source text that XmlReader has accepted.</summary>
    /// <exception cref="CfgeditException">The scanner met something it did not expect: an internal parse mismatch.</exception>
    internal static XmlScanResult Scan(SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var scanner = new XmlScanner(source);
        return scanner.Run();
    }

    /// <summary>An element on the stack while its children are being scanned.</summary>
    private sealed class ElementFrame
    {
        public ElementFrame(int start, string name, int nameStart, List<XmlAttributeSyntax> attributes, int startTagEnd, bool isEmpty)
        {
            Start = start;
            Name = name;
            NameStart = nameStart;
            Attributes = attributes;
            StartTagEnd = startTagEnd;
            IsEmpty = isEmpty;
        }

        public int Start { get; }

        public string Name { get; }

        public int NameStart { get; }

        public List<XmlAttributeSyntax> Attributes { get; }

        public int StartTagEnd { get; }

        public bool IsEmpty { get; }

        public List<XmlNodeSyntax> Children { get; } = [];
    }

    private XmlScanResult Run()
    {
        if (StartsAt(0, "<?xml") && _text.Length > 5 && IsSpace(_text[5]))
        {
            _result.Declaration = ReadDeclaration();
        }

        ReadMisc(_result.Prolog, inProlog: true);
        _result.Root = ReadElement();
        ReadMisc(_result.Epilog, inProlog: false);
        return _result;
    }

    /// <summary>Reads the whitespace, comments, PIs and (in the prolog) the DOCTYPE around the root element.</summary>
    private void ReadMisc(List<XmlNodeSyntax> list, bool inProlog)
    {
        while (true)
        {
            if (_pos >= _text.Length)
            {
                if (inProlog)
                {
                    Fail("the document has no root element", _pos);
                }

                return;
            }

            char c = _text[_pos];
            if (IsSpace(c))
            {
                XmlTextSyntax text = ReadText();
                if (!text.IsWhitespace)
                {
                    Fail("character data outside the root element", text.Start);
                }

                list.Add(text);
            }
            else if (StartsAt(_pos, "<!--"))
            {
                list.Add(ReadComment());
            }
            else if (StartsAt(_pos, "<?"))
            {
                list.Add(ReadPi());
            }
            else if (inProlog && StartsAt(_pos, "<!DOCTYPE"))
            {
                if (_result.Doctype is not null)
                {
                    Fail("a second document type declaration", _pos);
                }

                _result.Doctype = ReadDoctype();
                list.Add(_result.Doctype);
            }
            else if (inProlog && c == '<')
            {
                return;
            }
            else
            {
                Fail("unexpected markup outside the root element", _pos);
            }
        }
    }

    /// <summary>
    /// Reads the element that starts at the current position and everything inside it. The loop keeps the open elements on
    /// an explicit stack, so the nesting depth costs heap, not call stack.
    /// </summary>
    private XmlElementSyntax ReadElement()
    {
        var stack = new Stack<ElementFrame>();
        stack.Push(ReadStartTag());

        while (true)
        {
            ElementFrame top = stack.Peek();
            XmlElementSyntax? finished = null;
            if (top.IsEmpty)
            {
                finished = Build(top, endTag: null, end: top.StartTagEnd);
            }
            else if (_pos >= _text.Length)
            {
                Fail($"element '{top.Name}' is not closed", _pos);
            }
            else if (StartsAt(_pos, "</"))
            {
                XmlEndTagSyntax endTag = ReadEndTag();
                finished = Build(top, endTag, endTag.End);
            }
            else if (StartsAt(_pos, "<!--"))
            {
                top.Children.Add(ReadComment());
            }
            else if (StartsAt(_pos, "<![CDATA["))
            {
                top.Children.Add(ReadCData());
            }
            else if (StartsAt(_pos, "<?"))
            {
                top.Children.Add(ReadPi());
            }
            else if (_text[_pos] == '<')
            {
                stack.Push(ReadStartTag());
                continue;
            }
            else
            {
                top.Children.Add(ReadText());
            }

            if (finished is null)
            {
                continue;
            }

            stack.Pop();
            if (stack.Count == 0)
            {
                return finished;
            }

            stack.Peek().Children.Add(finished);
        }
    }

    private static XmlElementSyntax Build(ElementFrame frame, XmlEndTagSyntax? endTag, int end)
    {
        return new XmlElementSyntax(
            frame.Start,
            end,
            frame.Name,
            frame.NameStart,
            frame.Attributes.ToArray(),
            frame.StartTagEnd,
            frame.IsEmpty,
            frame.Children.ToArray(),
            endTag);
    }

    /// <summary>Reads a start tag, up to and including its <c>&gt;</c> or <c>/&gt;</c>.</summary>
    private ElementFrame ReadStartTag()
    {
        int start = _pos;
        _pos++;
        int nameStart = _pos;
        string name = ReadName();
        var attributes = new List<XmlAttributeSyntax>();
        bool isEmpty;
        int startTagEnd;

        while (true)
        {
            SkipSpace();
            if (_pos >= _text.Length)
            {
                Fail($"the start tag of '{name}' is not closed", start);
            }

            char c = _text[_pos];
            if (c == '>')
            {
                _pos++;
                isEmpty = false;
                startTagEnd = _pos;
                break;
            }

            if (c == '/')
            {
                Expect('>', after: 1);
                _pos += 2;
                isEmpty = true;
                startTagEnd = _pos;
                break;
            }

            attributes.Add(ReadAttribute());
        }

        return new ElementFrame(start, name, nameStart, attributes, startTagEnd, isEmpty);
    }

    private XmlAttributeSyntax ReadAttribute()
    {
        int start = _pos;
        string name = ReadName();
        SkipSpace();
        Expect('=');
        _pos++;
        SkipSpace();
        if (_pos >= _text.Length || _text[_pos] is not ('"' or '\''))
        {
            Fail($"the value of attribute '{name}' is not quoted", _pos);
        }

        char quote = _text[_pos];
        int valueStart = _pos;
        int close = IndexOfOrFail(quote, valueStart + 1);
        int rawStart = valueStart + 1;
        string raw = _text[rawStart..close];
        string value = DecodeRange(rawStart, close, isAttribute: true, out bool isRaw);
        _pos = close + 1;
        return new XmlAttributeSyntax(start, _pos, name, valueStart, quote, raw, isRaw ? raw : value, isRaw);
    }

    private XmlEndTagSyntax ReadEndTag()
    {
        int start = _pos;
        _pos += 2;
        int nameStart = _pos;
        string name = ReadName();
        SkipSpace();
        Expect('>');
        _pos++;
        return new XmlEndTagSyntax(start, _pos, name, nameStart);
    }

    /// <summary>Reads character data up to the next <c>&lt;</c> or the end of the text.</summary>
    private XmlTextSyntax ReadText()
    {
        int start = _pos;
        int end = _text.IndexOf('<', start);
        if (end < 0)
        {
            end = _text.Length;
        }

        _pos = end;
        string value = DecodeRange(start, end, isAttribute: false, out bool isRaw);
        string raw = _text[start..end];
        bool isWhitespace = !isRaw && AllSpace(start, end);
        return new XmlTextSyntax(start, end, raw, isRaw ? raw : value, isWhitespace, isRaw);
    }

    private XmlCommentSyntax ReadComment()
    {
        int start = _pos;
        int close = IndexOfOrFail("-->", start + 4);
        _pos = close + 3;
        return new XmlCommentSyntax(start, _pos, NormalizeLineEnds(_text[(start + 4)..close]));
    }

    private XmlCDataSyntax ReadCData()
    {
        int start = _pos;
        int close = IndexOfOrFail("]]>", start + 9);
        _pos = close + 3;
        return new XmlCDataSyntax(start, _pos, NormalizeLineEnds(_text[(start + 9)..close]));
    }

    private XmlProcessingInstructionSyntax ReadPi()
    {
        int start = _pos;
        _pos += 2;
        string target = ReadName();
        string data = string.Empty;
        if (StartsAt(_pos, "?>"))
        {
            _pos += 2;
        }
        else
        {
            if (_pos >= _text.Length || !IsSpace(_text[_pos]))
            {
                Fail($"the processing instruction '{target}' is not closed", start);
            }

            SkipSpace();
            int close = IndexOfOrFail("?>", _pos);
            data = NormalizeLineEnds(_text[_pos..close]);
            _pos = close + 2;
        }

        return new XmlProcessingInstructionSyntax(start, _pos, target, data);
    }

    /// <summary>Reads the XML declaration, from <c>&lt;?xml</c> through <c>?&gt;</c>.</summary>
    private XmlDeclarationSyntax ReadDeclaration()
    {
        int start = _pos;
        _pos += 5;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        while (true)
        {
            SkipSpace();
            if (StartsAt(_pos, "?>"))
            {
                _pos += 2;
                break;
            }

            string name = ReadName();
            SkipSpace();
            Expect('=');
            _pos++;
            SkipSpace();
            if (_pos >= _text.Length || _text[_pos] is not ('"' or '\''))
            {
                Fail($"the declaration's '{name}' is not quoted", _pos);
            }

            char quote = _text[_pos];
            int close = IndexOfOrFail(quote, _pos + 1);
            values[name] = _text[(_pos + 1)..close];
            _pos = close + 1;
        }

        string version = values.TryGetValue("version", out string? declaredVersion)
            ? declaredVersion
            : throw ScannerMismatch(_source, "the XML declaration has no version", start);
        values.TryGetValue("encoding", out string? encoding);
        values.TryGetValue("standalone", out string? standalone);
        return new XmlDeclarationSyntax(start, _pos, version, encoding, standalone);
    }

    /// <summary>
    /// Reads a DOCTYPE from <c>&lt;!DOCTYPE</c> through its closing <c>&gt;</c>. Quoted literals are skipped, and the
    /// internal subset is skipped by <see cref="SkipInternalSubset"/>.
    /// </summary>
    private XmlDoctypeSyntax ReadDoctype()
    {
        int start = _pos;
        _pos += "<!DOCTYPE".Length;
        SkipSpace();
        int nameStart = _pos;
        string name = ReadName();
        string? subset = null;
        while (true)
        {
            if (_pos >= _text.Length)
            {
                Fail("the document type declaration is not closed", start);
            }

            char c = _text[_pos];
            if (c == '[')
            {
                int subsetStart = _pos + 1;
                int close = SkipInternalSubset(subsetStart);
                subset = _text[subsetStart..close];
                _pos = close + 1;
                SkipSpace();
                Expect('>');
                _pos++;
                break;
            }

            if (c == '>')
            {
                _pos++;
                break;
            }

            if (c is '"' or '\'')
            {
                _pos = IndexOfOrFail(c, _pos + 1) + 1;
                continue;
            }

            _pos++;
        }

        return new XmlDoctypeSyntax(start, _pos, name, nameStart, subset);
    }

    /// <summary>
    /// Skips the internal subset from <paramref name="i"/> to its closing <c>]</c>, which it returns. Comments, PIs and
    /// quoted literals are skipped, so a <c>]</c> or <c>&gt;</c> inside them does not end the subset. General entity
    /// declarations are read on the way, for the markup check.
    /// </summary>
    private int SkipInternalSubset(int i)
    {
        while (true)
        {
            if (i >= _text.Length)
            {
                Fail("the internal subset is not closed", i);
            }

            if (StartsAt(i, "<!--"))
            {
                i = IndexOfOrFail("-->", i + 4) + 3;
            }
            else if (StartsAt(i, "<?"))
            {
                i = IndexOfOrFail("?>", i + 2) + 2;
            }
            else if (StartsAt(i, "<!ENTITY"))
            {
                i = ReadEntityDeclaration(i);
            }
            else if (_text[i] is '"' or '\'')
            {
                i = IndexOfOrFail(_text[i], i + 1) + 1;
            }
            else if (_text[i] == ']')
            {
                return i;
            }
            else
            {
                i++;
            }
        }
    }

    /// <summary>
    /// Reads one general entity declaration starting at <paramref name="i"/>, and records its name when its replacement
    /// text contains a '&lt;' after character references are expanded. Returns the offset to continue from.
    /// </summary>
    private int ReadEntityDeclaration(int i)
    {
        int j = SkipSpaceAt(i + "<!ENTITY".Length);
        if (j >= _text.Length || _text[j] == '%')
        {
            return j;
        }

        int nameEnd = NameEndAt(j);
        if (nameEnd == j)
        {
            return j;
        }

        string name = _text[j..nameEnd];
        int k = SkipSpaceAt(nameEnd);
        if (k >= _text.Length || _text[k] is not ('"' or '\''))
        {
            // An external entity: its quoted literals are skipped by the caller.
            return nameEnd;
        }

        int close = IndexOfOrFail(_text[k], k + 1);
        string replacement = ExpandCharacterReferences(_text[(k + 1)..close], k + 1);
        if (replacement.Contains('<', StringComparison.Ordinal))
        {
            _result.MarkupEntities.Add(name);
        }

        return close + 1;
    }

    /// <summary>Expands character references only, as an entity's replacement text is built. Other references stay as written.</summary>
    private string ExpandCharacterReferences(string value, int offset)
    {
        var sb = new StringBuilder(value.Length);
        int i = 0;
        while (i < value.Length)
        {
            if (value[i] == '&' && i + 1 < value.Length && value[i + 1] == '#')
            {
                int semi = value.IndexOf(';', i);
                if (semi < 0)
                {
                    Fail("a character reference is not terminated", offset + i);
                }

                sb.Append(CharacterReference(value[(i + 1)..semi], offset + i));
                i = semi + 1;
            }
            else
            {
                sb.Append(value[i]);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Decodes the range of text from <paramref name="start"/> to <paramref name="end"/>. Line ends are normalised as XmlReader
    /// does: in text CRLF and CR become LF, and in an attribute value tab, LF, CR and CRLF each become a space. The five
    /// predefined references and character references are expanded. A reference to any other entity sets
    /// <paramref name="isRaw"/> and is recorded for the markup check.
    /// </summary>
    private string DecodeRange(int start, int end, bool isAttribute, out bool isRaw)
    {
        isRaw = false;
        var sb = new StringBuilder(end - start);
        int i = start;
        while (i < end)
        {
            char c = _text[i];
            if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < end && _text[i + 1] == '\n')
                {
                    i++;
                }

                sb.Append(isAttribute ? ' ' : '\n');
                i++;
            }
            else if (c == '\t' && isAttribute)
            {
                sb.Append(' ');
                i++;
            }
            else if (c == '&')
            {
                int semi = _text.IndexOf(';', i + 1);
                if (semi < 0 || semi >= end)
                {
                    Fail("a reference is not terminated", i);
                }

                string name = _text[(i + 1)..semi];
                switch (name)
                {
                    case "lt":
                        sb.Append('<');
                        break;
                    case "gt":
                        sb.Append('>');
                        break;
                    case "amp":
                        sb.Append('&');
                        break;
                    case "apos":
                        sb.Append('\'');
                        break;
                    case "quot":
                        sb.Append('"');
                        break;
                    default:
                        if (name.StartsWith('#'))
                        {
                            sb.Append(CharacterReference(name, i));
                        }
                        else
                        {
                            isRaw = true;
                            _result.EntityReferences.Add((name, i));
                        }

                        break;
                }

                i = semi + 1;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>The text of a character reference, given its name without the '&amp;' and ';' (for example <c>#x41</c>).</summary>
    private string CharacterReference(string name, int offset)
    {
        bool ok = name.Length >= 2 && name[0] == '#';
        int code = 0;
        if (ok)
        {
            ok = name[1] == 'x'
                ? int.TryParse(name.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code)
                : int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out code);
        }

        if (!ok || code < 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF))
        {
            Fail($"the character reference '&{name};' is not a character", offset);
        }

        return char.ConvertFromUtf32(code);
    }

    private static string NormalizeLineEnds(string text)
    {
        return text.Contains('\r', StringComparison.Ordinal)
            ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            : text;
    }

    private bool AllSpace(int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (!IsSpace(_text[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\r' or '\n';

    private bool StartsAt(int at, string s)
    {
        return at >= 0 && at + s.Length <= _text.Length && string.CompareOrdinal(_text, at, s, 0, s.Length) == 0;
    }

    private void SkipSpace()
    {
        _pos = SkipSpaceAt(_pos);
    }

    private int SkipSpaceAt(int i)
    {
        while (i < _text.Length && IsSpace(_text[i]))
        {
            i++;
        }

        return i;
    }

    private string ReadName()
    {
        int end = NameEndAt(_pos);
        if (end == _pos)
        {
            Fail("a name was expected", _pos);
        }

        string name = _text[_pos..end];
        _pos = end;
        return name;
    }

    /// <summary>
    /// The end of the name that starts at <paramref name="i"/>. A name runs up to whitespace, '=', '/', '&gt;', a quote, '&lt;',
    /// '?', '[' or ']'. None of those can be part of an XML name, so the text is already well-formed when they appear.
    /// </summary>
    private int NameEndAt(int i)
    {
        while (i < _text.Length && !IsNameDelimiter(_text[i]))
        {
            i++;
        }

        return i;
    }

    private static bool IsNameDelimiter(char c)
    {
        return IsSpace(c) || c is '=' or '/' or '>' or '"' or '\'' or '<' or '?' or '[' or ']';
    }

    private void Expect(char c, int after = 0)
    {
        if (_pos + after >= _text.Length || _text[_pos + after] != c)
        {
            Fail($"'{c}' was expected", _pos + after);
        }
    }

    private int IndexOfOrFail(char c, int from)
    {
        int index = from <= _text.Length ? _text.IndexOf(c, from) : -1;
        if (index < 0)
        {
            Fail($"'{c}' was not found", from);
        }

        return index;
    }

    private int IndexOfOrFail(string s, int from)
    {
        int index = from <= _text.Length ? _text.IndexOf(s, from, StringComparison.Ordinal) : -1;
        if (index < 0)
        {
            Fail($"'{s}' was not found", from);
        }

        return index;
    }

    /// <summary>Reports a scanner mismatch at an offset of the text, in the form the parse pipeline uses for every mismatch.</summary>
    private void Fail(string what, int offset)
    {
        throw ScannerMismatch(_source, what, offset);
    }

    internal static CfgeditException ScannerMismatch(SourceText source, string what, int offset)
    {
        LinePosition at = source.GetPosition(Math.Min(offset, source.Text.Length));
        return new CfgeditException($"internal parse mismatch: scanner: {what} (please report this)")
        {
            Line = at.Line,
            Column = at.Column,
        };
    }
}
