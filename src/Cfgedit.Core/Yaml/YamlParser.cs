using System.Text;
using Shared.Core;

namespace Cfgedit.Core.Yaml;

/// <summary>
/// Builds the <see cref="YamlDocument"/> tree from the text. It is recursive descent over the characters, with the indentation of
/// the enclosing block passed down, because YAML's meaning depends on columns. The parser is split by concern: this file has the
/// stream, documents, directives, trivia and properties; <c>YamlParser.Block.cs</c> the block structure; <c>YamlParser.Flow.cs</c>
/// the flow collections; and <c>YamlParser.Scalars.cs</c> the scalars and the block scalar bodies.
/// </summary>
internal sealed partial class YamlParser
{
    private const int MaxDepth = Cfgedit.Core.Json.JsonDocument.MaxDepth;
    private const int MaxImplicitKeyLength = 1024;
    private const string SecondaryHandlePrefix = "tag:yaml.org,2002:";

    private readonly SourceText _source;
    private readonly string _s;
    private int _pos;
    private int _depth;

    // Anchor name to whether its node is complete. An alias to an anchor that is still open would be recursive: refused.
    private readonly Dictionary<string, bool> _anchors = new(StringComparer.Ordinal);
    private Dictionary<string, string> _tagHandles = NewTagHandles();
    private readonly HashSet<string> _declaredHandles = new(StringComparer.Ordinal);

    private YamlParser(SourceText source)
    {
        _source = source;
        _s = source.Text;
    }

    /// <exception cref="CfgeditException">The text is not valid YAML within the subset.</exception>
    internal static YamlDocument Parse(SourceText source)
    {
        var parser = new YamlParser(source);
        parser.CheckCharacters();
        return new YamlDocument(source, parser.ParseStream());
    }

    /// <summary>The properties read before a node: its anchor and tag, and the tokens and spaces that spell them.</summary>
    private readonly struct Props
    {
        internal Props(List<YamlNode> parts, YamlToken? anchor, YamlToken? tag)
        {
            Parts = parts;
            Anchor = anchor;
            Tag = tag;
        }

        internal List<YamlNode> Parts { get; }

        internal YamlToken? Anchor { get; }

        internal YamlToken? Tag { get; }

        internal bool Any => Parts.Count > 0;
    }

    private static Props NoProps() => new(new List<YamlNode>(), null, null);

    private static Dictionary<string, string> NewTagHandles()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["!"] = "!",
            ["!!"] = SecondaryHandlePrefix,
        };
    }

    // ----- Characters, positions and errors -----

    private int Length => _s.Length;

    private static bool IsBreak(char c) => c is '\n' or '\r';

    private static bool IsBlank(char c) => c is ' ' or '\t';

    private static bool IsFlowIndicator(char c) => c is ',' or '[' or ']' or '{' or '}';

    /// <summary>The characters that cannot start a plain scalar, besides <c>-</c>, <c>?</c> and <c>:</c>, which can under conditions.</summary>
    private static bool IsIndicator(char c) => c is ',' or '[' or ']' or '{' or '}' or '#' or '&' or '*' or '!' or '|' or '>' or '\'' or '"' or '%' or '@' or '`';

    private bool IsBlankOrBreakOrEnd(int i) => i >= Length || IsBlank(_s[i]) || IsBreak(_s[i]);

    private bool AtLineStart(int i) => i == 0 || IsBreak(_s[i - 1]);

    private int LineStart(int i)
    {
        while (i > 0 && !IsBreak(_s[i - 1]))
        {
            i--;
        }

        return i;
    }

    /// <summary>The column of an offset: the number of characters before it on its line.</summary>
    private int Column(int i) => i - LineStart(i);

    private int BreakEnd(int i) => _s[i] == '\r' && i + 1 < Length && _s[i + 1] == '\n' ? i + 2 : i + 1;

    private int SkipBlanks(int i)
    {
        while (i < Length && IsBlank(_s[i]))
        {
            i++;
        }

        return i;
    }

    /// <summary>True when a document marker, <c>---</c> or <c>...</c>, starts at the offset, which must be at column 0.</summary>
    private bool IsDocumentMarkerAt(int i)
    {
        if (i + 3 > Length || !AtLineStart(i))
        {
            return false;
        }

        char c = _s[i];
        if ((c != '-' && c != '.') || _s[i + 1] != c || _s[i + 2] != c)
        {
            return false;
        }

        return i + 3 == Length || IsBlank(_s[i + 3]) || IsBreak(_s[i + 3]);
    }

    /// <summary>True when the line from the offset holds only blanks before its line break or the end of the text, and at least one is a tab.</summary>
    private bool IsTabOnlyLine(int i)
    {
        int end = SkipBlanks(i);
        return (end >= Length || IsBreak(_s[end])) && HasTabBetween(i, end);
    }

    private bool IsDocumentStartAt(int i) => i < Length && _s[i] == '-' && IsDocumentMarkerAt(i);

    private bool IsDocumentEndAt(int i) => i < Length && _s[i] == '.' && IsDocumentMarkerAt(i);

    private bool IsSeqEntryAt(int i) => i < Length && _s[i] == '-' && IsBlankOrBreakOrEnd(i + 1);

    private bool IsExplicitKeyAt(int i) => i < Length && _s[i] == '?' && IsBlankOrBreakOrEnd(i + 1);

    /// <summary>
    /// Characters that cannot be in YAML at all: C0 controls other than tab and the line breaks, DEL, C1 controls other than
    /// U+0085, U+FFFE and U+FFFF, and lone surrogates. A surrogate pair is a valid character.
    /// </summary>
    private static bool IsPrintable(char c)
    {
        return c is '\t' or '\n' or '\r' or '\u0085'
            || (c >= 0x20 && c <= 0x7E)
            || (c >= 0xA0 && c <= 0xD7FF)
            || (c >= 0xE000 && c <= 0xFFFD);
    }

    private void CheckCharacters()
    {
        for (int i = 0; i < Length; i++)
        {
            char c = _s[i];
            if (char.IsHighSurrogate(c) && i + 1 < Length && char.IsLowSurrogate(_s[i + 1]))
            {
                i++;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                throw Fail(i, "a lone surrogate is not allowed in YAML");
            }

            if (!IsPrintable(c))
            {
                throw Fail(i, $"character U+{(int)c:X4} is not allowed in YAML");
            }
        }
    }

    private CfgeditException Fail(int offset, string message)
    {
        LinePosition at = _source.GetPosition(offset);
        return new CfgeditException(message)
        {
            Line = at.Line,
            Column = at.Column,
        };
    }

    private void EnterDepth()
    {
        if (++_depth > MaxDepth)
        {
            throw Fail(_pos, $"nesting is deeper than {MaxDepth} levels");
        }
    }

    private void ExitDepth() => _depth--;

    private YamlToken Token(YamlTokenKind kind, int start, int end) => new(start, end, kind, _s.Substring(start, end - start));

    private YamlToken Whitespace(int start, int end) => Token(YamlTokenKind.Whitespace, start, end);

    private YamlToken LineBreakToken(int start, int end) => Token(YamlTokenKind.LineBreak, start, end);

    private bool HasTabBetween(int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (_s[i] == '\t')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when a character other than blanks comes before the offset on its line.</summary>
    private bool HasContentBefore(int i)
    {
        for (int k = LineStart(i); k < i; k++)
        {
            if (!IsBlank(_s[k]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads a comment at the <c>#</c>, up to the end of the line. The line break is not consumed.</summary>
    private YamlComment ReadComment()
    {
        int start = _pos;
        if (start > 0 && !IsBlank(_s[start - 1]) && !IsBreak(_s[start - 1]))
        {
            throw Fail(start, "a comment must be preceded by a space");
        }

        int end = start;
        while (end < Length && !IsBreak(_s[end]))
        {
            end++;
        }

        _pos = end;
        return new YamlComment(start, end, _s.Substring(start + 1, end - start - 1), HasContentBefore(start));
    }

    // ----- Trivia -----

    /// <summary>
    /// Reads spaces, tabs, comments and line breaks in block context, adding them to <paramref name="parts"/>, and stops at the
    /// next content. A tab in the indentation of a line is refused.
    /// </summary>
    private void ReadTrivia(List<YamlNode> parts)
    {
        while (_pos < Length)
        {
            char c = _s[_pos];
            if (IsBlank(c))
            {
                int end = SkipBlanks(_pos);
                if (AtLineStart(_pos) && end < Length && !IsBreak(_s[end]) && _s[end] != '#' && HasTabBetween(_pos, end))
                {
                    throw Fail(_pos, "tabs are not allowed for indentation");
                }

                parts.Add(Whitespace(_pos, end));
                _pos = end;
            }
            else if (c == '#')
            {
                parts.Add(ReadComment());
            }
            else if (IsBreak(c))
            {
                int end = BreakEnd(_pos);
                parts.Add(LineBreakToken(_pos, end));
                _pos = end;
            }
            else
            {
                return;
            }
        }
    }

    /// <summary>
    /// The offset of the next content after blanks, breaks and comments, without consuming anything. The tab check of
    /// <see cref="ReadTrivia"/> applies here too, so a tab in an indentation is refused whichever way it is read.
    /// </summary>
    private int PeekContent(int i)
    {
        while (i < Length)
        {
            char c = _s[i];
            if (IsBlank(c))
            {
                int end = SkipBlanks(i);
                if (AtLineStart(i) && end < Length && !IsBreak(_s[end]) && _s[end] != '#' && HasTabBetween(i, end))
                {
                    throw Fail(i, "tabs are not allowed for indentation");
                }

                i = end;
            }
            else if (c == '#')
            {
                while (i < Length && !IsBreak(_s[i]))
                {
                    i++;
                }
            }
            else if (IsBreak(c))
            {
                i = BreakEnd(i);
            }
            else
            {
                return i;
            }
        }

        return Length;
    }

    /// <summary>
    /// Ends a line after a node: blanks, an optional comment and the line break are consumed. Nothing is consumed when the position
    /// is already at the start of a line, because the node ended there. Other text on the line is refused.
    /// </summary>
    private void ConsumeLineTail(List<YamlNode> parts)
    {
        if (AtLineStart(_pos))
        {
            return;
        }

        int end = SkipBlanks(_pos);
        if (end > _pos)
        {
            parts.Add(Whitespace(_pos, end));
            _pos = end;
        }

        if (_pos >= Length)
        {
            return;
        }

        if (_s[_pos] == '#')
        {
            parts.Add(ReadComment());
        }

        if (_pos >= Length)
        {
            return;
        }

        if (IsBreak(_s[_pos]))
        {
            int breakEnd = BreakEnd(_pos);
            parts.Add(LineBreakToken(_pos, breakEnd));
            _pos = breakEnd;
            return;
        }

        throw Fail(_pos, "unexpected text after the value");
    }

    // ----- Stream and documents -----

    private List<YamlNode> ParseStream()
    {
        var items = new List<YamlNode>();
        while (true)
        {
            ReadTrivia(items);
            if (_pos >= Length)
            {
                break;
            }

            items.Add(ParseDocument());
        }

        return items;
    }

    private YamlDocumentNode ParseDocument()
    {
        var parts = new List<YamlNode>();
        var directives = new List<YamlDirective>();
        _tagHandles = NewTagHandles();
        _declaredHandles.Clear();
        _anchors.Clear();

        bool yamlSeen = false;
        while (_pos < Length && _s[_pos] == '%' && Column(_pos) == 0)
        {
            YamlDirective directive = ParseDirective(ref yamlSeen);
            directives.Add(directive);
            parts.Add(directive);
            ConsumeLineTail(parts);
            ReadTrivia(parts);
        }

        if (directives.Count > 0 && !IsDocumentStartAt(_pos))
        {
            throw Fail(_pos, "a directive must be followed by '---'");
        }

        YamlToken? startMarker = null;
        YamlToken? endMarker = null;
        YamlValueNode? root = null;
        if (IsDocumentStartAt(_pos))
        {
            startMarker = Token(YamlTokenKind.DocumentStart, _pos, _pos + 3);
            parts.Add(startMarker);
            _pos += 3;
            int afterMarker = SkipBlanks(_pos);
            if (afterMarker < Length && !IsBreak(_s[afterMarker]) && _s[afterMarker] != '#')
            {
                if (afterMarker > _pos)
                {
                    parts.Add(Whitespace(_pos, afterMarker));
                }

                _pos = afterMarker;
                root = ParseInline(-1, compactAllowed: false, mappingValue: false, parts);
                parts.Add(root);
                ConsumeLineTail(parts);
            }
            else
            {
                ConsumeLineTail(parts);
                ReadTrivia(parts);
                if (_pos < Length && !IsDocumentMarkerAt(_pos))
                {
                    root = ParseInline(-1, compactAllowed: true, mappingValue: false, parts);
                    parts.Add(root);
                    ConsumeLineTail(parts);
                }
            }
        }
        else if (_pos < Length && !IsDocumentMarkerAt(_pos))
        {
            root = ParseInline(-1, compactAllowed: true, mappingValue: false, parts);
            parts.Add(root);
            ConsumeLineTail(parts);
        }

        ReadTrivia(parts);
        if (IsDocumentEndAt(_pos))
        {
            endMarker = Token(YamlTokenKind.DocumentEnd, _pos, _pos + 3);
            parts.Add(endMarker);
            _pos += 3;
            ConsumeLineTail(parts);
        }
        else if (_pos < Length && !IsDocumentStartAt(_pos))
        {
            if (Column(_pos) == 0 && _s[_pos] == '%')
            {
                throw Fail(_pos, "a directive must follow '...'");
            }

            throw Fail(_pos, "unexpected text after the document");
        }

        return new YamlDocumentNode(parts, directives, startMarker, root, endMarker);
    }

    private YamlDirective ParseDirective(ref bool yamlSeen)
    {
        int start = _pos;
        int nameEnd = start + 1;
        while (nameEnd < Length && IsNameChar(_s[nameEnd]))
        {
            nameEnd++;
        }

        string name = _s.Substring(start + 1, nameEnd - start - 1);
        if (name.Length == 0)
        {
            throw Fail(start, "a directive needs a name");
        }

        var parts = new List<YamlNode> { Token(YamlTokenKind.DirectiveName, start, nameEnd) };
        _pos = nameEnd;
        int argsStart = SkipBlanks(_pos);
        if (argsStart > _pos)
        {
            parts.Add(Whitespace(_pos, argsStart));
        }

        int argsEnd = argsStart;
        for (int i = argsStart; i < Length && !IsBreak(_s[i]); i++)
        {
            if (_s[i] == '#' && (i == argsStart || IsBlank(_s[i - 1])))
            {
                break;
            }

            if (!IsBlank(_s[i]))
            {
                argsEnd = i + 1;
            }
        }

        string arguments = _s.Substring(argsStart, argsEnd - argsStart);
        if (argsEnd > argsStart)
        {
            parts.Add(Token(YamlTokenKind.DirectiveArguments, argsStart, argsEnd));
        }

        _pos = argsEnd;
        switch (name)
        {
            case "YAML":
                CheckYamlDirective(start, argsStart, arguments, ref yamlSeen);
                break;
            case "TAG":
                ParseTagDirective(start, arguments);
                break;
            default:
                throw Fail(start, $"the %{name} directive is not supported");
        }

        return new YamlDirective(parts, name, arguments);
    }

    private void CheckYamlDirective(int start, int argsStart, string version, ref bool yamlSeen)
    {
        if (yamlSeen)
        {
            throw Fail(start, "duplicate %YAML directive");
        }

        yamlSeen = true;
        if (version == "1.2")
        {
            return;
        }

        if (version.Length > 0 && version.Split('.') is [var major, var minor]
            && major.All(char.IsAsciiDigit) && minor.All(char.IsAsciiDigit) && major.Length > 0 && minor.Length > 0)
        {
            throw Fail(argsStart, $"YAML version {version} is not supported");
        }

        throw Fail(argsStart, "a %YAML directive needs a version such as 1.2");
    }

    private void ParseTagDirective(int start, string arguments)
    {
        int space = arguments.IndexOfAny([' ', '\t']);
        if (space < 0)
        {
            throw Fail(start, "a %TAG directive needs a handle and a prefix");
        }

        string handle = arguments[..space];
        string prefix = arguments[space..].TrimStart(' ', '\t');
        bool namedHandle = handle.Length > 2 && handle[0] == '!' && handle[^1] == '!';
        if (handle is not ("!" or "!!") && !namedHandle)
        {
            throw Fail(start, $"'{handle}' is not a tag handle");
        }

        if (namedHandle && !handle[1..^1].All(IsWordChar))
        {
            throw Fail(start, $"'{handle}' is not a tag handle");
        }

        if (prefix.Length == 0)
        {
            throw Fail(start, "a %TAG directive needs a prefix");
        }

        if (!_declaredHandles.Add(handle))
        {
            throw Fail(start, $"duplicate %TAG handle {handle}");
        }

        _tagHandles[handle] = prefix;
    }

    // ----- Properties -----

    /// <summary>
    /// Reads anchors and tags in either order. The spaces between them are read only when content follows on the same line, so
    /// spaces before a line break are left to the caller.
    /// </summary>
    private Props ReadProperties()
    {
        var parts = new List<YamlNode>();
        YamlToken? anchor = null;
        YamlToken? tag = null;
        while (_pos < Length && _s[_pos] is '&' or '!')
        {
            if (_s[_pos] == '&')
            {
                if (anchor is not null)
                {
                    throw Fail(_pos, "a node can have only one anchor");
                }

                anchor = ReadAnchor();
                parts.Add(anchor);
            }
            else
            {
                if (tag is not null)
                {
                    throw Fail(_pos, "a node can have only one tag");
                }

                tag = ReadTag();
                parts.Add(tag);
            }

            int end = SkipBlanks(_pos);
            if (end > _pos && end < Length && !IsBreak(_s[end]) && _s[end] != '#')
            {
                parts.Add(Whitespace(_pos, end));
                _pos = end;
            }
            else
            {
                break;
            }
        }

        return new Props(parts, anchor, tag);
    }

    private YamlToken ReadAnchor()
    {
        int start = _pos;
        int end = start + 1;
        while (end < Length && IsAnchorChar(_s[end]))
        {
            end++;
        }

        if (end == start + 1)
        {
            throw Fail(start, "an anchor needs a name");
        }

        _pos = end;
        string name = _s.Substring(start + 1, end - start - 1);
        _anchors[name] = false;
        return Token(YamlTokenKind.Anchor, start, end);
    }

    private YamlToken ReadTag()
    {
        int start = _pos;
        int end;
        if (start + 1 < Length && _s[start + 1] == '<')
        {
            int close = _s.IndexOf('>', start + 2);
            if (close < 0 || close == start + 2)
            {
                throw Fail(start, "a verbatim tag needs the form !<uri>");
            }

            for (int i = start + 2; i < close; i++)
            {
                if (!IsUriChar(_s[i]) && _s[i] != '%')
                {
                    throw Fail(i, $"'{_s[i]}' is not allowed in a verbatim tag");
                }
            }

            end = close + 1;
        }
        else
        {
            int suffixStart;
            string handle;
            if (start + 1 < Length && _s[start + 1] == '!')
            {
                handle = "!!";
                suffixStart = start + 2;
            }
            else
            {
                int w = start + 1;
                while (w < Length && IsWordChar(_s[w]))
                {
                    w++;
                }

                if (w < Length && _s[w] == '!' && w > start + 1)
                {
                    handle = _s.Substring(start, w + 1 - start);
                    suffixStart = w + 1;
                }
                else
                {
                    handle = "!";
                    suffixStart = start + 1;
                }
            }

            end = suffixStart;
            while (end < Length && IsTagChar(_s[end]))
            {
                if (_s[end] == '%')
                {
                    if (end + 2 >= Length || !char.IsAsciiHexDigit(_s[end + 1]) || !char.IsAsciiHexDigit(_s[end + 2]))
                    {
                        throw Fail(end, "a tag's % must be followed by two hex digits");
                    }

                    end += 3;
                }
                else
                {
                    end++;
                }
            }

            if (handle != "!" && end == suffixStart)
            {
                throw Fail(start, $"the tag {handle} needs a suffix");
            }

            if (handle != "!" && !_tagHandles.ContainsKey(handle))
            {
                throw Fail(start, $"tag handle {handle} is not defined");
            }
        }

        _pos = end;
        return Token(YamlTokenKind.Tag, start, end);
    }

    private YamlAlias ParseAlias()
    {
        int start = _pos;
        int end = start + 1;
        while (end < Length && IsAnchorChar(_s[end]))
        {
            end++;
        }

        if (end == start + 1)
        {
            throw Fail(start, "an alias needs a name");
        }

        string name = _s.Substring(start + 1, end - start - 1);
        if (!_anchors.TryGetValue(name, out bool complete))
        {
            throw Fail(start, $"alias *{name} is not defined");
        }

        if (!complete)
        {
            throw Fail(start, $"alias *{name} refers to an anchor that contains it, which is not supported");
        }

        _pos = end;
        return new YamlAlias([Token(YamlTokenKind.Alias, start, end)], name);
    }

    /// <summary>Marks the anchor of a node that is now complete, so that an alias to it is allowed.</summary>
    private void CloseAnchor(Props props)
    {
        if (props.Anchor is not null)
        {
            _anchors[props.Anchor.Text[1..]] = true;
        }
    }

    private static bool IsNameChar(char c) => !IsBlank(c) && !IsBreak(c);

    private static bool IsAnchorChar(char c) => !IsBlank(c) && !IsBreak(c) && !IsFlowIndicator(c);

    private static bool IsWordChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '-';

    private static bool IsUriChar(char c)
    {
        return char.IsAsciiLetterOrDigit(c) || "-#;/?:@&=+$_.~*'()[]".Contains(c) || c == ',' || c == '!';
    }

    private static bool IsTagChar(char c) => IsUriChar(c) && c != '!' && !IsFlowIndicator(c) || c == '%';
}
