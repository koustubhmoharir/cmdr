namespace Cfgedit.Core.Json;

/// <summary>A token: its kind and its span in the text, from <see cref="Start"/> for <see cref="Length"/> UTF-16 units.</summary>
/// <param name="Kind">The kind of token.</param>
/// <param name="Start">The offset of the first character.</param>
/// <param name="Length">The number of UTF-16 units. Zero only for <see cref="JsonTokenKind.EndOfFile"/>.</param>
public readonly record struct JsonToken(JsonTokenKind Kind, int Start, int Length)
{
    /// <summary>The offset just after the token.</summary>
    public int End => Start + Length;
}
