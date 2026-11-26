using System.Runtime.ExceptionServices;
using Shared.Core;

namespace Cfgedit.Core.Xml;

/// <summary>
/// A parsed XML document: the concrete syntax tree of a source text. Every node is a span of the text, so the text between
/// nodes is whatever the source holds there.
/// </summary>
public sealed class XmlDocumentSyntax
{
    internal XmlDocumentSyntax(SourceText source, XmlScanResult scan)
    {
        Source = source;
        Declaration = scan.Declaration;
        Prolog = scan.Prolog.ToArray();
        Root = scan.Root ?? throw new InvalidOperationException("The scanner found no root element.");
        Epilog = scan.Epilog.ToArray();
    }

    /// <summary>The source text the tree was parsed from.</summary>
    /// <summary>
    /// The deepest element nesting accepted, the root being level 1. Deeper documents are a parse error, as for JSON
    /// (<c>JsonDocument.MaxDepth</c>), because the layers above the CST walk the tree one level per element.
    /// </summary>
    public const int MaxDepth = 256;

    public SourceText Source { get; }

    /// <summary>The XML declaration, or null when the document has none.</summary>
    public XmlDeclarationSyntax? Declaration { get; }

    /// <summary>The nodes before the root element, in order: comments, PIs, the document type and whitespace text.</summary>
    public IReadOnlyList<XmlNodeSyntax> Prolog { get; }

    /// <summary>The root element.</summary>
    public XmlElementSyntax Root { get; }

    /// <summary>The nodes after the root element, in order: comments, PIs and whitespace text.</summary>
    public IReadOnlyList<XmlNodeSyntax> Epilog { get; }

    /// <summary>
    /// Parses the bytes of a document. The bytes are decoded first, and the decoding is checked against XmlReader before the
    /// tree is built.
    /// </summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <param name="path">The file's path, carried by any error. Null when unknown.</param>
    /// <exception cref="CfgeditException">The bytes cannot be decoded, or the document is not well-formed. Path, Line and Column say where.</exception>
    public static XmlDocumentSyntax Parse(ReadOnlySpan<byte> bytes, string? path)
    {
        SourceText source = XmlDecoding.Decode(bytes, path);
        try
        {
            XmlDecoding.CheckEncodingAgreesWithXmlReader(bytes, source);
            return Parse(source, perturbedIndex: null);
        }
        catch (CfgeditException ex) when (ex.Path is null)
        {
            throw new CfgeditException(ex.Message, ex)
            {
                Path = path,
                Line = ex.Line,
                Column = ex.Column,
            };
        }
    }

    /// <summary>
    /// Parses a decoded document. The scanner builds the tree, and XmlReader reads the document while each of its nodes is
    /// compared with the tree's, in document order.
    /// </summary>
    /// <exception cref="CfgeditException">
    /// The document is not well-formed, it contains an entity with markup, or the tree and the reader disagree (an internal
    /// parse mismatch). Line and Column say where.
    /// </exception>
    public static XmlDocumentSyntax Parse(SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Parse(source, perturbedIndex: null);
    }

    /// <summary>
    /// The parse with a test hook. <paramref name="perturbedIndex"/> moves one CST node's offset by a character before the
    /// comparison, so that the mismatch path can be tested. It is test-only: every real parse passes null.
    /// </summary>
    internal static XmlDocumentSyntax Parse(SourceText source, int? perturbedIndex)
    {
        ArgumentNullException.ThrowIfNull(source);

        // The scanner runs first, but a failure is held back until XmlReader has read the whole document: a well-formedness
        // error is reported in preference to a scanner failure, as it always has been.
        XmlScanResult? scan = null;
        ExceptionDispatchInfo? scanFailure = null;
        try
        {
            scan = XmlScanner.Scan(source);
        }
        catch (CfgeditException ex)
        {
            scanFailure = ExceptionDispatchInfo.Capture(ex);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or InvalidOperationException)
        {
            // A scanner bug on text the reader accepts must still surface as cfgedit's internal mismatch error, never as a raw
            // runtime exception (on malformed text the reader's error is reported instead, below).
            scanFailure = ExceptionDispatchInfo.Capture(new CfgeditException(
                $"internal parse mismatch: scanner: {ex.GetType().Name}: {ex.Message} (please report this)", ex));
        }

        XmlDocumentSyntax? document = scan is null ? null : new XmlDocumentSyntax(source, scan);
        var comparison = new XmlCrossCheck.ReaderComparison(source, document, scan, perturbedIndex);
        XmlReaderValidation.Run(source, comparison);
        scanFailure?.Throw();
        comparison.ThrowFirstProblem();
        return document!;
    }
}
