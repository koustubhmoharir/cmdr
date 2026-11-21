namespace Cfgedit.Core.Xml;

/// <summary>
/// A node of the XML concrete syntax tree. <see cref="Start"/> and <see cref="End"/> are offsets into the text of the
/// document's <see cref="XmlDocumentSyntax.Source"/>: Start is inclusive and End is exclusive.
/// </summary>
public abstract class XmlNodeSyntax
{
    internal XmlNodeSyntax(int start, int end)
    {
        Start = start;
        End = end;
    }

    /// <summary>The offset of the node's first character.</summary>
    public int Start { get; }

    /// <summary>The offset just after the node's last character.</summary>
    public int End { get; }
}

/// <summary>The XML declaration, <c>&lt;?xml ...?&gt;</c>, at the start of the document.</summary>
public sealed class XmlDeclarationSyntax : XmlNodeSyntax
{
    internal XmlDeclarationSyntax(int start, int end, string version, string? encoding, string? standalone)
        : base(start, end)
    {
        Version = version;
        Encoding = encoding;
        Standalone = standalone;
    }

    /// <summary>The value of the version pseudo-attribute.</summary>
    public string Version { get; }

    /// <summary>The value of the encoding pseudo-attribute, or null when it is absent.</summary>
    public string? Encoding { get; }

    /// <summary>The value of the standalone pseudo-attribute, or null when it is absent.</summary>
    public string? Standalone { get; }
}

/// <summary>The document type declaration, <c>&lt;!DOCTYPE name ...&gt;</c>.</summary>
public sealed class XmlDoctypeSyntax : XmlNodeSyntax
{
    internal XmlDoctypeSyntax(int start, int end, string name, int nameStart, string? internalSubset)
        : base(start, end)
    {
        Name = name;
        NameStart = nameStart;
        InternalSubset = internalSubset;
    }

    /// <summary>The name of the document type.</summary>
    public string Name { get; }

    /// <summary>The offset of the name. XmlReader reports the declaration at this position.</summary>
    public int NameStart { get; }

    /// <summary>The raw text between the <c>[</c> and the <c>]</c> of the internal subset, or null when there is none.</summary>
    public string? InternalSubset { get; }
}

/// <summary>An element. Its children are the nodes between its start tag and its end tag, in order.</summary>
public sealed class XmlElementSyntax : XmlNodeSyntax
{
    internal XmlElementSyntax(
        int start,
        int end,
        string name,
        int nameStart,
        IReadOnlyList<XmlAttributeSyntax> attributes,
        int startTagEnd,
        bool isEmpty,
        IReadOnlyList<XmlNodeSyntax> children,
        XmlEndTagSyntax? endTag)
        : base(start, end)
    {
        Name = name;
        NameStart = nameStart;
        Attributes = attributes;
        StartTagEnd = startTagEnd;
        IsEmpty = isEmpty;
        Children = children;
        EndTag = endTag;
    }

    /// <summary>The element name, with its prefix if it has one.</summary>
    public string Name { get; }

    /// <summary>The offset of the name.</summary>
    public int NameStart { get; }

    /// <summary>The attributes in document order. Attributes that a DTD supplies by default are not in the source and are not here.</summary>
    public IReadOnlyList<XmlAttributeSyntax> Attributes { get; }

    /// <summary>The offset just after the <c>&gt;</c> or <c>/&gt;</c> that ends the start tag.</summary>
    public int StartTagEnd { get; }

    /// <summary>True when the element is written as <c>&lt;a/&gt;</c>.</summary>
    public bool IsEmpty { get; }

    /// <summary>The child nodes, in order: elements, text runs, CDATA sections, comments and PIs.</summary>
    public IReadOnlyList<XmlNodeSyntax> Children { get; }

    /// <summary>The end tag, or null when the element is empty.</summary>
    public XmlEndTagSyntax? EndTag { get; }
}

/// <summary>An end tag, <c>&lt;/name&gt;</c>, with optional whitespace before the <c>&gt;</c>.</summary>
public sealed class XmlEndTagSyntax : XmlNodeSyntax
{
    internal XmlEndTagSyntax(int start, int end, string name, int nameStart)
        : base(start, end)
    {
        Name = name;
        NameStart = nameStart;
    }

    /// <summary>The name written in the end tag.</summary>
    public string Name { get; }

    /// <summary>The offset of the name.</summary>
    public int NameStart { get; }
}

/// <summary>
/// An attribute. <see cref="Start"/> is the offset of the name, and <see cref="End"/> is just after the closing quote.
/// </summary>
public sealed class XmlAttributeSyntax : XmlNodeSyntax
{
    internal XmlAttributeSyntax(
        int start, int end, string name, int valueStart, char quote, string rawValue, string value, bool isRaw)
        : base(start, end)
    {
        Name = name;
        ValueStart = valueStart;
        Quote = quote;
        RawValue = rawValue;
        Value = value;
        IsRaw = isRaw;
    }

    /// <summary>The attribute name, with its prefix if it has one.</summary>
    public string Name { get; }

    /// <summary>The offset of the opening quote.</summary>
    public int ValueStart { get; }

    /// <summary>The quote character that encloses the value: <c>"</c> or <c>'</c>.</summary>
    public char Quote { get; }

    /// <summary>The text between the quotes, exactly as written.</summary>
    public string RawValue { get; }

    /// <summary>
    /// The decoded value, with references expanded and literal tab, LF, CR and CRLF each replaced by a space, as XmlReader
    /// reports it. For a raw-mode value this is equal to <see cref="RawValue"/>.
    /// </summary>
    public string Value { get; }

    /// <summary>True when the value holds a reference to an entity other than the five predefined ones.</summary>
    public bool IsRaw { get; }
}

/// <summary>
/// A maximal run of character data and references between markup. Comments, PIs and CDATA sections split runs.
/// </summary>
public sealed class XmlTextSyntax : XmlNodeSyntax
{
    internal XmlTextSyntax(int start, int end, string raw, string value, bool isWhitespace, bool isRaw)
        : base(start, end)
    {
        Raw = raw;
        Value = value;
        IsWhitespace = isWhitespace;
        IsRaw = isRaw;
    }

    /// <summary>The text exactly as written.</summary>
    public string Raw { get; }

    /// <summary>
    /// The decoded value: CRLF and lone CR become LF, and references are expanded. For a raw-mode run this is equal to
    /// <see cref="Raw"/>.
    /// </summary>
    public string Value { get; }

    /// <summary>True when the run holds only XML whitespace (space, tab, CR, LF) and no references.</summary>
    public bool IsWhitespace { get; }

    /// <summary>True when the run holds a reference to an entity other than the five predefined ones.</summary>
    public bool IsRaw { get; }
}

/// <summary>A CDATA section, <c>&lt;![CDATA[...]]&gt;</c>.</summary>
public sealed class XmlCDataSyntax : XmlNodeSyntax
{
    internal XmlCDataSyntax(int start, int end, string content)
        : base(start, end)
    {
        Content = content;
    }

    /// <summary>The content, with CRLF and lone CR normalised to LF, as XmlReader reports it.</summary>
    public string Content { get; }
}

/// <summary>A comment, <c>&lt;!--...--&gt;</c>.</summary>
public sealed class XmlCommentSyntax : XmlNodeSyntax
{
    internal XmlCommentSyntax(int start, int end, string content)
        : base(start, end)
    {
        Content = content;
    }

    /// <summary>The content between the delimiters, with CRLF and lone CR normalised to LF.</summary>
    public string Content { get; }
}

/// <summary>A processing instruction, <c>&lt;?target data?&gt;</c>.</summary>
public sealed class XmlProcessingInstructionSyntax : XmlNodeSyntax
{
    internal XmlProcessingInstructionSyntax(int start, int end, string target, string data)
        : base(start, end)
    {
        Target = target;
        Data = data;
    }

    /// <summary>The target name.</summary>
    public string Target { get; }

    /// <summary>The data after the whitespace that follows the target, with CRLF and lone CR normalised to LF.</summary>
    public string Data { get; }
}
