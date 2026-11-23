using System.Text;

namespace Cfgedit.Core.Yaml;

/// <summary>
/// The block structure: block mappings and sequences, the values after an indicator, and the implicit keys. The indentation of the
/// enclosing block is <c>n</c> (-1 at the top of a document); a node on a later line must be indented more than <c>n</c>, and a
/// block collection's entries sit at the column of its first token.
/// </summary>
internal sealed partial class YamlParser
{
    /// <summary>Detects duplicate keys within one mapping. Strings are compared by their text; other keys are refused beyond one.</summary>
    private sealed class KeyRegistry(YamlParser parser)
    {
        private readonly HashSet<string> _strings = new(StringComparer.Ordinal);
        private bool _hasNonString;

        internal void Add(YamlScalar key)
        {
            if (key.Kind is YamlCoreKind.String)
            {
                if (!_strings.Add(key.Value))
                {
                    throw parser.Fail(key.Start, $"duplicate key '{key.Value}'");
                }

                return;
            }

            if (_hasNonString)
            {
                throw parser.Fail(key.Start, "a mapping with more than one key that is not a string is not supported");
            }

            _hasNonString = true;
        }
    }

    /// <summary>True when the offset is the end of a line: the end of the text, a line break, or a comment.</summary>
    private bool AtLineEnd(int i) => i >= Length || IsBreak(_s[i]) || _s[i] == '#';

    /// <summary>True when a <c>:</c> that ends a key follows the offset on the same line.</summary>
    private bool ColonFollows(int i)
    {
        int k = SkipBlanks(i);
        return k < Length && _s[k] == ':' && IsBlankOrBreakOrEnd(k + 1);
    }

    /// <summary>
    /// True when an implicit key starts at the offset: a plain or quoted scalar on one line, followed by <c>:</c>, after any
    /// properties. Collections, aliases and block scalars are not looked at here; the caller parses them as values and refuses a
    /// colon after them.
    /// </summary>
    private bool IsKeyAt(int i)
    {
        int j = i;
        while (j < Length && _s[j] is '&' or '!')
        {
            while (j < Length && !IsBlankOrBreakOrEnd(j))
            {
                j++;
            }

            int k = SkipBlanks(j);
            if (k >= Length || IsBreak(_s[k]) || _s[k] == '#')
            {
                return false;
            }

            j = k;
        }

        if (j >= Length)
        {
            return false;
        }

        char c = _s[j];
        if (c is '"' or '\'')
        {
            int end = ScanFlowScalar(j, c == '"' ? YamlScalarStyle.DoubleQuoted : YamlScalarStyle.SingleQuoted, -1, false, false, new StringBuilder());
            return ColonFollows(end);
        }

        if (!IsPlainFirst(j, inFlow: false))
        {
            return false;
        }

        int plainEnd = ScanFlowScalar(j, YamlScalarStyle.Plain, -1, false, singleLine: true, new StringBuilder());
        return ColonFollows(plainEnd);
    }

    /// <summary>
    /// True when a plain scalar can start at the offset. A plain scalar cannot start with an indicator; <c>-</c>, <c>?</c> and <c>:</c>
    /// can, when a safe character follows them.
    /// </summary>
    private bool IsPlainFirst(int i, bool inFlow)
    {
        if (i >= Length)
        {
            return false;
        }

        char c = _s[i];
        if (IsBlank(c) || IsBreak(c))
        {
            return false;
        }

        if (c is '-' or '?' or ':')
        {
            if (!IsBlankOrBreakOrEnd(i + 1))
            {
                return !(inFlow && IsFlowIndicator(_s[i + 1]));
            }

            return false;
        }

        return !IsIndicator(c) && !(inFlow && IsFlowIndicator(c));
    }

    /// <summary>
    /// Parses what follows an indicator on the same line (a <c>:</c> or a <c>-</c>), or on a later line. Returns null for an empty
    /// value. Separators and trivia before the value are added to <paramref name="parts"/>. A value on a later line must be indented
    /// more than <paramref name="n"/>; a mapping value may also be a block sequence at the same column (an indentless sequence).
    /// </summary>
    private YamlValueNode? ParseBlockValue(int n, bool mappingValue, List<YamlNode> parts)
    {
        int p = SkipBlanks(_pos);
        if (p < Length && !IsBreak(_s[p]) && _s[p] != '#')
        {
            if (p > _pos)
            {
                // A block collection after a tab would start at a column that depends on the tab's width, so it is refused.
                if (HasTabBetween(_pos, p) && (IsSeqEntryAt(p) || IsKeyAt(p)))
                {
                    throw Fail(p, "tabs are not allowed for indentation");
                }

                parts.Add(Whitespace(_pos, p));
            }

            _pos = p;
            return ParseInline(n, compactAllowed: !mappingValue, mappingValue, parts);
        }

        int next = PeekContent(p);
        if (next >= Length || IsDocumentMarkerAt(next))
        {
            return null;
        }

        int col = Column(next);
        bool indentlessSequence = mappingValue && col == n && IsSeqEntryAt(next);
        if (col <= n && !indentlessSequence)
        {
            return null;
        }

        ReadTrivia(parts);
        return ParseInline(n, compactAllowed: true, mappingValue, parts);
    }

    /// <summary>
    /// Parses a node that starts at the current position, which is content (after any trivia). It may be a block collection that
    /// starts here: a compact one on the indicator's line, or one on a line of its own. Properties on the same line as a key belong
    /// to the key.
    /// </summary>
    /// <param name="n">The indentation of the enclosing block.</param>
    /// <param name="compactAllowed">True when a block collection may start here: on a line of its own, or after <c>- </c>.</param>
    /// <param name="mappingValue">True when the node is the value of a mapping entry.</param>
    /// <param name="parts">The parts of the enclosing node, which receive the trivia that comes before a node that starts on a later line.</param>
    private YamlValueNode ParseInline(int n, bool compactAllowed, bool mappingValue, List<YamlNode> parts)
    {
        int start = _pos;
        if (IsSeqEntryAt(start))
        {
            if (!compactAllowed)
            {
                throw Fail(start, "a block sequence cannot start on the same line as its key");
            }

            return ParseBlockSequence(Column(start), NoProps());
        }

        if (IsKeyAt(start))
        {
            if (!compactAllowed)
            {
                throw Fail(start, "mapping values are not allowed here");
            }

            return ParseBlockMapping(Column(start), NoProps());
        }

        Props props = ReadProperties();
        if (props.Any && AtLineEnd(SkipBlanks(_pos)))
        {
            return ParsePropertiesOnLine(n, mappingValue, props);
        }

        if (props.Any && IsSeqEntryAt(_pos))
        {
            throw Fail(_pos, "a block sequence cannot have properties on the same line");
        }

        YamlValueNode node = ParseNode(n, props, inFlow: false);
        RefuseColonAfterNode(node);
        return node;
    }

    /// <summary>
    /// Properties with nothing after them on their line. They belong to the value on the following lines, when there is one, and
    /// otherwise to an empty scalar.
    /// </summary>
    private YamlValueNode ParsePropertiesOnLine(int n, bool mappingValue, Props props)
    {
        int next = PeekContent(_pos);
        if (next < Length && !IsDocumentMarkerAt(next))
        {
            int col = Column(next);
            if (col > n || (mappingValue && col == n && IsSeqEntryAt(next)))
            {
                ReadTrivia(props.Parts);
                return ParseAtLine(n, props);
            }
        }

        CloseAnchor(props);
        return EmptyScalar(props);
    }

    /// <summary>Parses the node at the current position, which starts a line, after its properties were read.</summary>
    private YamlValueNode ParseAtLine(int n, Props props)
    {
        if (IsSeqEntryAt(_pos))
        {
            return ParseBlockSequence(Column(_pos), props);
        }

        if (IsKeyAt(_pos))
        {
            return ParseBlockMapping(Column(_pos), props);
        }

        YamlValueNode node = ParseNode(n, props, inFlow: false);
        RefuseColonAfterNode(node);
        return node;
    }

    private YamlScalar EmptyScalar(Props props)
    {
        return new YamlScalar(props.Parts, props.Anchor, props.Tag, YamlScalarStyle.Plain, string.Empty, isEmpty: true);
    }

    /// <summary>Refuses a colon after a node in a key position: such a node is a key, which the subset does not support.</summary>
    private void RefuseColonAfterNode(YamlValueNode node)
    {
        if (!ColonFollows(_pos))
        {
            return;
        }

        switch (node)
        {
            case YamlFlowMapping or YamlFlowSequence:
                throw Fail(node.Start, "a collection as a key is not supported");
            case YamlAlias:
                throw Fail(node.Start, "an alias as a key is not supported");
            case YamlScalar { Style: YamlScalarStyle.Literal or YamlScalarStyle.Folded }:
                throw Fail(node.Start, "a block scalar as a key is not supported");
            case YamlScalar when ContainsBreak(node.Start, node.End):
                throw Fail(node.Start, "a key that spans lines is not supported");
            default:
                throw Fail(_pos, "mapping values are not allowed here");
        }
    }

    private bool ContainsBreak(int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (IsBreak(_s[i]))
            {
                return true;
            }
        }

        return false;
    }

    private YamlBlockMapping ParseBlockMapping(int col, Props props)
    {
        EnterDepth();
        var parts = new List<YamlNode>(props.Parts);
        var entries = new List<YamlMappingEntry>();
        var keys = new KeyRegistry(this);
        bool first = true;
        while (true)
        {
            if (!first)
            {
                int next = PeekContent(_pos);
                if (next >= Length || IsDocumentMarkerAt(next))
                {
                    break;
                }

                int column = Column(next);
                if (column < col)
                {
                    break;
                }

                if (column > col)
                {
                    throw Fail(next, "bad indentation of a mapping entry");
                }
            }

            first = false;
            ReadTrivia(parts);
            YamlMappingEntry entry = ParseMappingEntry(col);
            keys.Add(entry.Key);
            entries.Add(entry);
            parts.Add(entry);
            ConsumeLineTail(parts);
        }

        ExitDepth();
        CloseAnchor(props);
        return new YamlBlockMapping(parts, props.Anchor, props.Tag, entries);
    }

    private YamlMappingEntry ParseMappingEntry(int col)
    {
        var parts = new List<YamlNode>();
        if (IsExplicitKeyAt(_pos))
        {
            throw Fail(_pos, "complex keys (?) are not supported");
        }

        YamlScalar key = ParseKeyScalar(col);
        parts.Add(key);
        int afterKey = SkipBlanks(_pos);
        if (afterKey > _pos)
        {
            parts.Add(Whitespace(_pos, afterKey));
            _pos = afterKey;
        }

        if (_pos >= Length || _s[_pos] != ':' || !IsBlankOrBreakOrEnd(_pos + 1))
        {
            throw Fail(_pos, "expected ':' after the key");
        }

        YamlToken colon = Token(YamlTokenKind.Indicator, _pos, _pos + 1);
        parts.Add(colon);
        _pos++;
        YamlValueNode? value = ParseBlockValue(col, mappingValue: true, parts);
        if (value is not null)
        {
            parts.Add(value);
        }

        return new YamlMappingEntry(parts, key, colon, value);
    }

    /// <summary>Parses an implicit key: a plain or quoted scalar on one line, with any properties before it.</summary>
    private YamlScalar ParseKeyScalar(int col)
    {
        int start = _pos;
        Props props = ReadProperties();
        if (_pos >= Length || IsBreak(_s[_pos]) || _s[_pos] == '#')
        {
            throw Fail(start, "expected a mapping key");
        }

        char c = _s[_pos];
        if (c is '[' or '{' or '*' or '|' or '>')
        {
            YamlValueNode node = ParseNode(col, props, inFlow: false);
            if (ColonFollows(_pos))
            {
                RefuseColonAfterNode(node);
            }

            throw Fail(start, "expected a mapping key");
        }

        if (c == ':')
        {
            throw Fail(_pos, "an empty key is not supported");
        }

        if (c is not ('"' or '\'') && !IsPlainFirst(_pos, inFlow: false))
        {
            throw Fail(_pos, "expected a mapping key");
        }

        YamlScalarStyle style = c switch
        {
            '"' => YamlScalarStyle.DoubleQuoted,
            '\'' => YamlScalarStyle.SingleQuoted,
            _ => YamlScalarStyle.Plain,
        };
        var sb = new StringBuilder();
        int end = ScanFlowScalar(_pos, style, col, inFlow: false, singleLine: true, sb);
        if (end - _pos > MaxImplicitKeyLength)
        {
            throw Fail(_pos, $"an implicit key is longer than {MaxImplicitKeyLength} characters");
        }

        var parts = new List<YamlNode>(props.Parts) { Token(YamlTokenKind.Scalar, _pos, end) };
        _pos = end;
        CloseAnchor(props);
        return new YamlScalar(parts, props.Anchor, props.Tag, style, sb.ToString(), isEmpty: false);
    }

    private YamlBlockSequence ParseBlockSequence(int col, Props props)
    {
        EnterDepth();
        var parts = new List<YamlNode>(props.Parts);
        var items = new List<YamlSequenceItem>();
        bool first = true;
        while (true)
        {
            if (!first)
            {
                int next = PeekContent(_pos);
                if (next >= Length || IsDocumentMarkerAt(next))
                {
                    break;
                }

                int column = Column(next);
                if (column < col || (column == col && !IsSeqEntryAt(next)))
                {
                    break;
                }

                if (column > col)
                {
                    throw Fail(next, "bad indentation of a sequence entry");
                }
            }

            first = false;
            ReadTrivia(parts);
            YamlSequenceItem item = ParseSequenceItem(col);
            items.Add(item);
            parts.Add(item);
            ConsumeLineTail(parts);
        }

        ExitDepth();
        CloseAnchor(props);
        return new YamlBlockSequence(parts, props.Anchor, props.Tag, items);
    }

    private YamlSequenceItem ParseSequenceItem(int col)
    {
        var parts = new List<YamlNode>();
        YamlToken dash = Token(YamlTokenKind.Indicator, _pos, _pos + 1);
        parts.Add(dash);
        _pos++;
        YamlValueNode? value = ParseBlockValue(col, mappingValue: false, parts);
        if (value is not null)
        {
            parts.Add(value);
        }

        return new YamlSequenceItem(parts, dash, value);
    }
}
