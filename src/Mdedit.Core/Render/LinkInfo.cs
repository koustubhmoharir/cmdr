namespace Mdedit.Core.Render;

/// <summary>
/// One link of a block, as the navigation needs it: where it is in the source and where it goes. The links of a block are in
/// <see cref="RenderBlock.Links"/>, in id order; a cell's <see cref="Cell.LinkId"/> is the index of its link there.
/// </summary>
/// <param name="Id">The link's id within its block, from 0, which is its index in <see cref="RenderBlock.Links"/>.</param>
/// <param name="Url">
/// The destination: Markdig's resolved URL for a link (a reference link gives its definition's URL). For an autolink, the address
/// as written between the angle brackets, not Markdig's <c>mailto:</c>-prefixed URL for an email address.
/// </param>
/// <param name="Start">The absolute source offset of the link's first character: the <c>[</c>, or the <c>&lt;</c> of an autolink.</param>
/// <param name="End">The absolute source offset after the link's last character (exclusive): after the <c>)</c>, <c>]</c> or <c>&gt;</c>.</param>
/// <param name="IsAutolink">Whether the link is an autolink: a bare URL in the text, or an address between angle brackets.</param>
public sealed record LinkInfo(int Id, string Url, int Start, int End, bool IsAutolink);
