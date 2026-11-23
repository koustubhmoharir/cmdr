namespace Cfgedit.Core.Yaml;

/// <summary>
/// Flow collections: <c>[ ... ]</c> and <c>{ ... }</c>, which may span lines and may have a trailing comma. Inside a flow collection
/// a line that starts after a break must be indented more than the enclosing block's indentation, and a document marker is refused.
/// </summary>
internal sealed partial class YamlParser
{
    private YamlValueNode ParseFlowCollection(int n, Props props)
    {
        int open = _pos;
        bool isMapping = _s[open] == '{';
        char close = isMapping ? '}' : ']';
        EnterDepth();
        var parts = new List<YamlNode>(props.Parts) { Token(YamlTokenKind.Indicator, open, open + 1) };
        _pos = open + 1;
        var entries = new List<YamlFlowMappingEntry>();
        var items = new List<YamlValueNode>();
        var keys = new KeyRegistry(this);
        while (true)
        {
            ReadFlowTrivia(parts, n);
            RequireMore(open, isMapping);
            if (_s[_pos] == close)
            {
                parts.Add(Token(YamlTokenKind.Indicator, _pos, _pos + 1));
                _pos++;
                break;
            }

            if (isMapping)
            {
                YamlFlowMappingEntry entry = ParseFlowMappingEntry(n);
                keys.Add(entry.Key);
                entries.Add(entry);
                parts.Add(entry);
            }
            else
            {
                YamlValueNode item = ParseFlowSequenceItem(n);
                items.Add(item);
                parts.Add(item);
            }

            ReadFlowTrivia(parts, n);
            RequireMore(open, isMapping);
            if (_s[_pos] == ',')
            {
                parts.Add(Token(YamlTokenKind.Indicator, _pos, _pos + 1));
                _pos++;
            }
            else if (_s[_pos] != close)
            {
                throw Fail(_pos, isMapping ? "expected ',' or '}' in a flow mapping" : "expected ',' or ']' in a flow sequence");
            }
        }

        ExitDepth();
        CloseAnchor(props);
        return isMapping
            ? new YamlFlowMapping(parts, props.Anchor, props.Tag, entries)
            : new YamlFlowSequence(parts, props.Anchor, props.Tag, items);
    }

    private void RequireMore(int open, bool isMapping)
    {
        if (_pos >= Length)
        {
            throw Fail(open, isMapping ? "unterminated flow mapping, expected '}'" : "unterminated flow sequence, expected ']'");
        }
    }

    /// <summary>
    /// Reads spaces, tabs, comments and line breaks inside a flow collection, adding them to <paramref name="parts"/>. After a line break
    /// the next content must be indented more than <paramref name="n"/>.
    /// </summary>
    private void ReadFlowTrivia(List<YamlNode> parts, int n)
    {
        bool crossed = false;
        while (_pos < Length)
        {
            char c = _s[_pos];
            if (IsBlank(c))
            {
                int end = SkipBlanks(_pos);
                parts.Add(Whitespace(_pos, end));
                _pos = end;
            }
            else if (IsBreak(c))
            {
                int end = BreakEnd(_pos);
                parts.Add(LineBreakToken(_pos, end));
                _pos = end;
                crossed = true;
            }
            else if (c == '#')
            {
                parts.Add(ReadComment());
            }
            else
            {
                break;
            }
        }

        if (crossed && _pos < Length)
        {
            if (IsDocumentMarkerAt(_pos))
            {
                throw Fail(_pos, "a document marker cannot be inside a flow collection");
            }

            if (SpacesFrom(LineStart(_pos)) <= n)
            {
                throw Fail(_pos, "bad indentation in a flow collection");
            }
        }
    }

    /// <summary>The offset after blanks and line breaks, without consuming anything.</summary>
    private int SkipFlowSpace(int i)
    {
        while (i < Length && (IsBlank(_s[i]) || IsBreak(_s[i])))
        {
            i = IsBreak(_s[i]) ? BreakEnd(i) : i + 1;
        }

        return i;
    }

    /// <summary>True when the offset ends a flow indicator's neighbourhood: the end, a blank, a break, or a flow indicator.</summary>
    private bool IsFlowSeparatorAfter(int i) => i >= Length || IsBlankOrBreakOrEnd(i) || IsFlowIndicator(_s[i]);

    /// <summary>
    /// True when a colon that separates a key from its value follows the offset. After a quoted key the colon needs no space
    /// (JSON style); after a plain key it needs a blank or a flow indicator, as a plain scalar would otherwise contain it.
    /// </summary>
    private bool IsFlowColonAt(int i, bool jsonLike)
    {
        int k = SkipFlowSpace(i);
        return k < Length && _s[k] == ':' && (jsonLike || IsFlowSeparatorAfter(k + 1));
    }

    private YamlValueNode ParseFlowSequenceItem(int n)
    {
        if (IsExplicitKeyAt(_pos))
        {
            throw Fail(_pos, "complex keys (?) are not supported");
        }

        Props props = ReadProperties();
        if (props.Any)
        {
            ReadFlowTrivia(props.Parts, n);
        }

        if (_pos >= Length)
        {
            throw Fail(_pos, "unterminated flow sequence, expected ']'");
        }

        char c = _s[_pos];
        if (c is ',' or ']' or '}')
        {
            if (props.Any)
            {
                CloseAnchor(props);
                return EmptyScalar(props);
            }

            throw Fail(_pos, "expected a value in a flow sequence");
        }

        YamlValueNode node = ParseNode(n, props, inFlow: true);
        bool jsonLike = node is YamlScalar { Style: YamlScalarStyle.SingleQuoted or YamlScalarStyle.DoubleQuoted };
        if (IsFlowColonAt(_pos, jsonLike))
        {
            throw Fail(node.Start, "a single-pair mapping in a flow sequence is not supported");
        }

        return node;
    }

    private YamlFlowMappingEntry ParseFlowMappingEntry(int n)
    {
        if (IsExplicitKeyAt(_pos))
        {
            throw Fail(_pos, "complex keys (?) are not supported");
        }

        int start = _pos;
        Props props = ReadProperties();
        if (props.Any)
        {
            ReadFlowTrivia(props.Parts, n);
        }

        if (_pos >= Length)
        {
            throw Fail(start, "unterminated flow mapping, expected '}'");
        }

        char c = _s[_pos];
        if (c == ':' && IsFlowSeparatorAfter(_pos + 1))
        {
            throw Fail(_pos, "an empty key is not supported");
        }

        if (c is ',' or '}')
        {
            throw props.Any
                ? Fail(_pos, "an empty key is not supported")
                : Fail(_pos, "expected a key in a flow mapping");
        }

        YamlValueNode keyNode = ParseNode(n, props, inFlow: true);
        if (keyNode is YamlFlowMapping or YamlFlowSequence)
        {
            throw Fail(keyNode.Start, "a collection as a key is not supported");
        }

        if (keyNode is YamlAlias)
        {
            throw Fail(keyNode.Start, "an alias as a key is not supported");
        }

        var key = (YamlScalar)keyNode;
        if (ContainsBreak(key.Start, key.End))
        {
            throw Fail(key.Start, "a key that spans lines is not supported");
        }

        var parts = new List<YamlNode> { key };
        bool jsonLike = key.Style is YamlScalarStyle.SingleQuoted or YamlScalarStyle.DoubleQuoted;
        if (!IsFlowColonAt(_pos, jsonLike))
        {
            return new YamlFlowMappingEntry(parts, key, null, null);
        }

        if (ContainsBreak(_pos, SkipFlowSpace(_pos)))
        {
            throw Fail(key.Start, "a key that spans lines is not supported");
        }

        ReadFlowTrivia(parts, n);
        YamlToken colon = Token(YamlTokenKind.Indicator, _pos, _pos + 1);
        parts.Add(colon);
        _pos++;
        ReadFlowTrivia(parts, n);
        YamlValueNode? value = ParseFlowValue(n);
        if (value is not null)
        {
            parts.Add(value);
        }

        return new YamlFlowMappingEntry(parts, key, colon, value);
    }

    /// <summary>The value of a flow mapping entry, which is null when the entry has none.</summary>
    private YamlValueNode? ParseFlowValue(int n)
    {
        Props props = ReadProperties();
        if (props.Any)
        {
            ReadFlowTrivia(props.Parts, n);
        }

        if (_pos >= Length || _s[_pos] is ',' or ']' or '}')
        {
            if (!props.Any)
            {
                return null;
            }

            CloseAnchor(props);
            return EmptyScalar(props);
        }

        return ParseNode(n, props, inFlow: true);
    }
}
