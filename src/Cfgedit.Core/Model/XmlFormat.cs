using Cfgedit.Core.Text;
using Cfgedit.Core.Xml;

namespace Cfgedit.Core.Model;

/// <summary>The XML format.</summary>
public sealed class XmlFormat : IConfigFormat
{
    /// <inheritdoc/>
    public string Name => "xml";

    /// <inheritdoc/>
    public ConfigDocument Parse(SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return FromSyntax(XmlDocumentSyntax.Parse(source));
    }

    /// <summary>Parses a file's bytes. The XML parser decodes them itself.</summary>
    internal ConfigDocument ParseBytes(ReadOnlySpan<byte> bytes, string? path)
    {
        return FromSyntax(XmlDocumentSyntax.Parse(bytes, path));
    }

    /// <inheritdoc/>
    /// <exception cref="NotImplementedException">Always, until the edit milestone.</exception>
    public IReadOnlyList<TextEdit> Plan(ConfigDocument document, EditOp op)
    {
        throw new NotImplementedException("Edit planning is not implemented yet.");
    }

    /// <inheritdoc/>
    public LogicalNode ToLogical(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Syntax is not XmlDocumentSyntax syntax)
        {
            throw new ArgumentException("The document was not parsed as XML.", nameof(document));
        }

        return XmlModelBuilder.BuildLogical(syntax);
    }

    private ConfigDocument FromSyntax(XmlDocumentSyntax syntax)
    {
        return new ConfigDocument(this, syntax.Source, XmlModelBuilder.BuildTree(syntax), syntax);
    }
}
