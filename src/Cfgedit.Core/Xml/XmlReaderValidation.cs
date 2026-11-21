using System.Text.RegularExpressions;
using System.Xml;
using Cfgedit.Core.Text;

namespace Cfgedit.Core.Xml;

/// <summary>An attribute as XmlReader reports it.</summary>
internal sealed class ReaderAttribute
{
    public ReaderAttribute(string name, string value, LinePosition position, bool isDefault, char quote)
    {
        Name = name;
        Value = value;
        Position = position;
        IsDefault = isDefault;
        Quote = quote;
    }

    public string Name { get; }

    public string Value { get; }

    public LinePosition Position { get; }

    /// <summary>True when a DTD supplies the attribute's value. Such an attribute is not in the source.</summary>
    public bool IsDefault { get; }

    /// <summary>The quote character in the source (XmlTextReaderImpl, which XmlReader.Create returns here, reports it).</summary>
    public char Quote { get; }
}

/// <summary>A node as XmlReader reports it, in document order.</summary>
internal sealed record ReaderNode(
    XmlNodeType Kind,
    int Depth,
    string Name,
    string Value,
    LinePosition Position,
    bool IsEmptyElement,
    IReadOnlyList<ReaderAttribute> Attributes);

/// <summary>
/// Runs XmlReader over the decoded text and records every node. XmlReader is the authority on well-formedness and on the
/// values of the document; its errors are cfgedit's parse errors.
/// </summary>
internal static class XmlReaderValidation
{
    private static readonly Regex ErrorPositionSuffix = new(@" ?Line \d+, position \d+\.$", RegexOptions.CultureInvariant);

    /// <summary>Reads every node of the document.</summary>
    /// <exception cref="CfgeditException">
    /// The document is not well-formed. The message is XmlReader's, without its position suffix, and Line and Column say
    /// where it is.
    /// </exception>
    internal static IReadOnlyList<ReaderNode> Read(SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ReadNodes(source.Text);
    }

    private static List<ReaderNode> ReadNodes(string text)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = null,
            MaxCharactersFromEntities = 1_000_000,
            IgnoreWhitespace = false,
            IgnoreComments = false,
            IgnoreProcessingInstructions = false,
            CheckCharacters = true,
        };

        var nodes = new List<ReaderNode>();
        try
        {
            using XmlReader reader = XmlReader.Create(new StringReader(text), settings);
            var lineInfo = (IXmlLineInfo)reader;
            while (reader.Read())
            {
                LinePosition position = new(lineInfo.LineNumber, lineInfo.LinePosition);
                bool isEmpty = reader.IsEmptyElement;
                var attributes = new List<ReaderAttribute>();
                if (reader.NodeType == XmlNodeType.Element && reader.HasAttributes)
                {
                    reader.MoveToFirstAttribute();
                    do
                    {
                        attributes.Add(new ReaderAttribute(
                            reader.Name,
                            reader.Value,
                            new LinePosition(lineInfo.LineNumber, lineInfo.LinePosition),
                            reader.IsDefault,
                            reader.QuoteChar));
                    }
                    while (reader.MoveToNextAttribute());
                    reader.MoveToElement();
                }

                nodes.Add(new ReaderNode(
                    reader.NodeType,
                    reader.Depth,
                    reader.Name,
                    reader.Value,
                    position,
                    isEmpty,
                    attributes));
            }
        }
        catch (XmlException ex)
        {
            throw ToCfgeditException(ex);
        }

        return nodes;
    }

    private static CfgeditException ToCfgeditException(XmlException ex)
    {
        string message = ErrorPositionSuffix.Replace(ex.Message, string.Empty);
        return new CfgeditException(message, ex)
        {
            Line = ex.LineNumber > 0 ? ex.LineNumber : null,
            Column = ex.LinePosition > 0 ? ex.LinePosition : null,
        };
    }
}
