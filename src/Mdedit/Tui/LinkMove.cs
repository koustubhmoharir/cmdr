namespace Mdedit.Tui;

/// <summary>What a Tab or Shift+Tab did in the view: the caret moved to a link, moved to a link after wrapping round the document's end, or there are no links.</summary>
internal enum LinkMove
{
    /// <summary>The document has no link that the caret can rest on.</summary>
    NoLinks,

    /// <summary>The caret went to the next (or previous) link in document order.</summary>
    Moved,

    /// <summary>The caret went to the first link (or the last one) because there was no further link in that direction.</summary>
    Wrapped,
}
