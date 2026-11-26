using Shared.Core;

namespace Cfgedit.Core.Yaml;

/// <summary>
/// Constructs that cfgedit accepts, and that the YAML test suite reads as valid, but where YamlDotNet reads the text differently, so the
/// cross-check cannot verify them. They are refused with a message, as the TOML cross-check refuses what Tomlyn misreads. Each refusal is
/// narrow: it names one shape of text that YamlDotNet is known to misread (checked against the suite), and the rest of the file is still
/// read and checked.
/// </summary>
internal static class YamlLimits
{
    /// <summary>Refuses the shapes that YamlDotNet reads differently. Runs after cfgedit's own parse.</summary>
    /// <exception cref="Cfgedit.Core.CfgeditException">The text has one of the shapes.</exception>
    internal static void Check(YamlDocument syntax)
    {
        string text = syntax.Source.Text;
        foreach (YamlNode item in syntax.Items)
        {
            CheckNode(syntax, item, null, text);
        }
    }

    private static void CheckNode(YamlDocument syntax, YamlNode node, YamlNode? parent, string text)
    {
        switch (node)
        {
            case YamlToken { Kind: YamlTokenKind.Whitespace } token when IsTabLineInCollection(text, token, parent):
                throw Refuse(
                    syntax,
                    token.Start,
                    "cfgedit cannot verify a tab on a blank line between entries; YamlDotNet reads it differently");
            case YamlScalar { Style: YamlScalarStyle.Literal or YamlScalarStyle.Folded } block:
                CheckBlockScalar(syntax, block, text);
                break;
        }

        foreach (YamlNode part in node.Parts)
        {
            CheckNode(syntax, part, node, text);
        }
    }

    /// <summary>A whitespace token that is a whole line of a block collection, starting with a tab in the first column.</summary>
    private static bool IsTabLineInCollection(string text, YamlToken token, YamlNode? parent)
    {
        bool collection = parent is YamlBlockMapping or YamlBlockSequence;
        bool startsLine = token.Start == 0 || text[token.Start - 1] is '\n' or '\r';
        bool endsLine = token.End == text.Length || text[token.End] is '\n' or '\r';
        return collection && startsLine && endsLine && token.Text.StartsWith('\t');
    }

    /// <summary>
    /// A block scalar that runs to the end of the text and is made of blank lines with spaces, or that ends in spaces with no line break.
    /// YamlDotNet adds or drops line breaks at the end of the text in these cases.
    /// </summary>
    private static void CheckBlockScalar(YamlDocument syntax, YamlScalar block, string text)
    {
        if (block.End != text.Length)
        {
            return;
        }

        string body = string.Concat(block.Parts.OfType<YamlToken>().Where(token => token.Kind == YamlTokenKind.BlockBody).Select(token => token.Text));
        bool allBlank = body.Split('\n').All(line => line.Trim(' ', '\t', '\r').Length == 0);
        if (allBlank && body.Contains(' '))
        {
            throw Refuse(
                syntax,
                block.Start,
                "cfgedit cannot verify a block scalar of blank lines with spaces at the end of the text; YamlDotNet reads it differently");
        }

        if (text.Length > 0 && text[^1] is not ('\n' or '\r'))
        {
            int lastBreak = text.LastIndexOfAny(['\n', '\r']);
            string lastLine = text[(lastBreak + 1)..];
            if (lastLine.Length > 0 && lastLine.Trim(' ').Length == 0)
            {
                throw Refuse(
                    syntax,
                    block.Start,
                    "cfgedit cannot verify a block scalar that ends in spaces with no line break; YamlDotNet reads it differently");
            }

            // The end of the text counts as a line break for clip and keep (as yaml-test-suite has it), while YamlDotNet adds none.
            string header = block.Parts.OfType<YamlToken>().First(token => token.Kind == YamlTokenKind.BlockHeader).Text;
            if (!header.Contains('-', StringComparison.Ordinal))
            {
                throw Refuse(
                    syntax,
                    block.Start,
                    "cfgedit cannot verify a block scalar that ends the file with no line break (add one at the end of the file); YamlDotNet reads it differently");
            }
        }
    }

    private static CfgeditException Refuse(YamlDocument syntax, int offset, string message)
    {
        LinePosition at = syntax.Source.GetPosition(offset);
        return new CfgeditException(message) { Line = at.Line, Column = at.Column };
    }
}
