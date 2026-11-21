namespace Cfgedit.Core.Json;

/// <summary>
/// The key of an object member. <see cref="Name"/> is decoded. <see cref="Quote"/> is the quote the key was written with, or
/// null for a bare identifier key (JSON5 only).
/// </summary>
public sealed class JsonKey
{
    internal JsonKey(int start, int end, string name, char? quote)
    {
        Start = start;
        End = end;
        Name = name;
        Quote = quote;
    }

    /// <summary>The offset of the first character of the key, its opening quote if it has one.</summary>
    public int Start { get; }

    /// <summary>The offset just after the key, its closing quote included.</summary>
    public int End { get; }

    /// <summary>The decoded name.</summary>
    public string Name { get; }

    /// <summary>The quote the key was written with: <c>"</c> or <c>'</c>. Null for a bare identifier key.</summary>
    public char? Quote { get; }
}
