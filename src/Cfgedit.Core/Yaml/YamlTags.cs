using System.Text;

namespace Cfgedit.Core.Yaml;

/// <summary>
/// Resolves YAML tags to their full names, as YamlDotNet reports them: <c>!!str</c> is <c>tag:yaml.org,2002:str</c>, a <c>%TAG</c>
/// handle is replaced by its prefix, <c>!&lt;uri&gt;</c> is the uri, and the non-specific tag <c>!</c> stays <c>!</c>. The handles are those
/// of one document: the defaults, and the <c>%TAG</c> directives of that document.
/// </summary>
internal static class YamlTags
{
    /// <summary>The prefix of the core tags, <c>tag:yaml.org,2002:</c>.</summary>
    internal const string CorePrefix = "tag:yaml.org,2002:";

    /// <summary>The tag handles of a document: <c>!</c> and <c>!!</c> by default, then each <c>%TAG</c> directive in order.</summary>
    internal static IReadOnlyDictionary<string, string> HandlesOf(YamlDocumentNode document)
    {
        var handles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["!"] = "!",
            ["!!"] = CorePrefix,
        };
        foreach (YamlDirective directive in document.Directives)
        {
            if (directive.Name != "TAG")
            {
                continue;
            }

            string arguments = directive.Arguments;
            int space = arguments.IndexOfAny([' ', '\t']);
            if (space > 0)
            {
                handles[arguments[..space]] = arguments[(space + 1)..].Trim(' ', '\t');
            }
        }

        return handles;
    }

    /// <summary>The full name of a tag as written, or null when the node has no tag.</summary>
    /// <param name="tag">The tag token, or null.</param>
    /// <param name="handles">The handles of the document the tag is in.</param>
    internal static string? Resolve(YamlToken? tag, IReadOnlyDictionary<string, string> handles)
    {
        if (tag is null)
        {
            return null;
        }

        string text = tag.Text;
        if (text == "!")
        {
            return "!";
        }

        if (text.StartsWith("!<", StringComparison.Ordinal))
        {
            return Decode(text[2..^1]);
        }

        string handle;
        string suffix;
        if (text.StartsWith("!!", StringComparison.Ordinal))
        {
            handle = "!!";
            suffix = text[2..];
        }
        else
        {
            int second = text.IndexOf('!', 1);
            if (second > 0)
            {
                handle = text[..(second + 1)];
                suffix = text[(second + 1)..];
            }
            else
            {
                handle = "!";
                suffix = text[1..];
            }
        }

        if (!handles.TryGetValue(handle, out string? prefix))
        {
            throw new CfgeditException($"tag handle {handle} is not defined");
        }

        return prefix + Decode(suffix);
    }

    /// <summary>Decodes the <c>%XX</c> escapes of a tag, as UTF-8 bytes.</summary>
    private static string Decode(string text)
    {
        if (text.IndexOf('%') < 0)
        {
            return text;
        }

        var bytes = new List<byte>(text.Length);
        int run = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '%' || i + 2 >= text.Length)
            {
                continue;
            }

            bytes.AddRange(Encoding.UTF8.GetBytes(text[run..i]));
            bytes.Add(Convert.ToByte(text.Substring(i + 1, 2), 16));
            i += 2;
            run = i + 1;
        }

        bytes.AddRange(Encoding.UTF8.GetBytes(text[run..]));
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
