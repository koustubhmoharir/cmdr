using Cfgedit.Core.Xml;
using Shared.Core;

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
    /// <exception cref="CfgeditException">The operation is invalid or not supported for XML.</exception>
    public IReadOnlyList<TextEdit> Plan(ConfigDocument document, EditOp op)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(op);
        if (document.Syntax is not XmlDocumentSyntax)
        {
            throw new ArgumentException("The document was not parsed as XML.", nameof(document));
        }

        return new XmlEditPlanner(document).Plan(op);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Every reparse error is the user's except an internal parse mismatch, which is cfgedit's own bug.
    /// </remarks>
    public bool IsUserError(CfgeditException reparseError)
    {
        ArgumentNullException.ThrowIfNull(reparseError);
        return !reparseError.Message.StartsWith("internal parse mismatch", StringComparison.Ordinal);
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
