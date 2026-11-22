namespace Cfgedit.Core.Toml;

/// <summary>The form a key part was written in.</summary>
public enum TomlKeyForm
{
    /// <summary>A bare key: letters, digits, <c>-</c> and <c>_</c>, written as it is.</summary>
    Bare,

    /// <summary>A basic-quoted key, <c>"…"</c>, with escapes.</summary>
    Basic,

    /// <summary>A literal-quoted key, <c>'…'</c>, with no escapes.</summary>
    Literal,
}

/// <summary>
/// A key: one simple key, or several joined by dots (<c>a.b."c d"</c>). <see cref="Parts"/> are the simple keys in order, and
/// <see cref="Separators"/> are the spans between them, each holding the spaces, the dot and the spaces around it.
/// </summary>
public sealed class TomlKey : TomlNode
{
    internal TomlKey(int start, int end, IReadOnlyList<TomlKeyPart> parts, IReadOnlyList<TomlSpan> separators)
        : base(start, end)
    {
        Parts = parts;
        Separators = separators;
    }

    /// <summary>The simple keys, in order. There is at least one.</summary>
    public IReadOnlyList<TomlKeyPart> Parts { get; }

    /// <summary>The spans between consecutive parts: one fewer than <see cref="Parts"/>.</summary>
    public IReadOnlyList<TomlSpan> Separators { get; }
}

/// <summary>A simple key: a bare key or a quoted key. <see cref="Name"/> is the decoded name.</summary>
public sealed class TomlKeyPart : TomlNode
{
    internal TomlKeyPart(int start, int end, TomlKeyForm form, string raw, string name)
        : base(start, end)
    {
        Form = form;
        Raw = raw;
        Name = name;
    }

    /// <summary>The form the key was written in.</summary>
    public TomlKeyForm Form { get; }

    /// <summary>The exact source text of the key, quotes included for a quoted key.</summary>
    public string Raw { get; }

    /// <summary>The decoded name: the text itself for a bare key, the unescaped text for a quoted key.</summary>
    public string Name { get; }
}
