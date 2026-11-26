using System.Text.RegularExpressions;
using System.Xml;
using Shared.Core;

namespace Cfgedit.Core.Xml;

/// <summary>An attribute as XmlReader reports it.</summary>
/// <param name="IsDefault">True when a DTD supplies the attribute's value. Such an attribute is not in the source.</param>
/// <param name="Quote">The quote character in the source (XmlTextReaderImpl, which XmlReader.Create returns here, reports it).</param>
internal readonly record struct ReaderAttribute(string Name, string Value, LinePosition Position, bool IsDefault, char Quote);

/// <summary>
/// Runs XmlReader over the decoded text, node by node. XmlReader is the authority on well-formedness and on the values of
/// the document; its errors are cfgedit's parse errors.
/// </summary>
internal static class XmlReaderValidation
{
    private static readonly Regex ErrorPositionSuffix = new(@" ?Line \d+, position \d+\.$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads every node of the document in order and hands each one to <paramref name="comparison"/>. The whole document is
    /// read even after the comparison has found a disagreement, because a well-formedness error takes precedence over it.
    /// </summary>
    /// <exception cref="CfgeditException">
    /// The document is not well-formed. The message is XmlReader's, without its position suffix, and Line and Column say
    /// where it is.
    /// </exception>
    internal static void Run(SourceText source, XmlCrossCheck.ReaderComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(comparison);
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

        // One list serves every element: the comparison reads it before the next node is read.
        var attributes = new List<ReaderAttribute>();
        try
        {
            using XmlReader reader = XmlReader.Create(new StringReader(source.Text), settings);
            var lineInfo = (IXmlLineInfo)reader;
            while (reader.Read())
            {
                LinePosition position = new(lineInfo.LineNumber, lineInfo.LinePosition);
                bool isEmpty = reader.IsEmptyElement;
                attributes.Clear();
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

                comparison.Node(reader.NodeType, reader.Name, reader.Value, position, isEmpty, attributes);
            }
        }
        catch (XmlException ex)
        {
            throw ToCfgeditException(ex);
        }
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
