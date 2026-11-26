namespace Mdedit.Core.Render;

/// <summary>
/// A run of a code block's text that has one token, as a colorizer reports it. The span covers the UTF-16 characters
/// <c>[Start, Start + Length)</c> of the code.
/// </summary>
/// <param name="Start">The index of the first character of the span in the code, in UTF-16 units.</param>
/// <param name="Length">The number of UTF-16 units the span covers.</param>
/// <param name="Token">The token of the span.</param>
public readonly record struct CodeSpan(int Start, int Length, CodeToken Token);
