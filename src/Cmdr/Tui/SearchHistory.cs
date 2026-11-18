namespace Cmdr.Tui;

/// <summary>
/// The search terms of this session, oldest first, with the navigation of the find field's ↑ and ↓ keys. Pure: it holds text only,
/// and knows nothing of the bar. Empty texts are not kept, a text that is already there moves to the newest place, and only the
/// newest <see cref="MaxEntries"/> are kept.
/// </summary>
/// <remarks>
/// A navigation starts with <see cref="Older"/>, which remembers the text the field had as the draft. <see cref="Newer"/> steps
/// back toward the newest entry and then returns the draft, which ends the navigation. <see cref="Add"/> and <see cref="Reset"/>
/// end a navigation too; the draft is then forgotten.
/// </remarks>
internal sealed class SearchHistory
{
    /// <summary>The number of terms kept. Adding one more drops the oldest.</summary>
    public const int MaxEntries = 50;

    private readonly List<string> _entries = new();
    private int _position = -1;
    private string _draft = string.Empty;

    /// <summary>The number of terms kept.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Remembers the text as the newest term. Empty text is ignored. A text already kept moves to the newest place. Ends a navigation
    /// in progress, even when the text is ignored.
    /// </summary>
    public void Add(string text)
    {
        Reset();
        if (text.Length == 0)
        {
            return;
        }

        _entries.Remove(text);
        _entries.Add(text);
        if (_entries.Count > MaxEntries)
        {
            _entries.RemoveAt(0);
        }
    }

    /// <summary>
    /// Steps to the next older term. On the first step of a navigation, <paramref name="current"/> is remembered as the draft and the
    /// newest term is returned. At the oldest term, returns null and the position stays there. Returns null when there are no terms.
    /// </summary>
    /// <param name="current">The text in the field when the navigation is started. Ignored by the later steps.</param>
    public string? Older(string current)
    {
        if (_entries.Count == 0)
        {
            return null;
        }

        if (_position < 0)
        {
            _draft = current;
            _position = _entries.Count - 1;
            return _entries[_position];
        }

        if (_position == 0)
        {
            return null;
        }

        _position--;
        return _entries[_position];
    }

    /// <summary>
    /// Steps to the next newer term. Past the newest term, returns the draft remembered by <see cref="Older"/> and ends the navigation.
    /// Returns null when no navigation is in progress.
    /// </summary>
    public string? Newer()
    {
        if (_position < 0)
        {
            return null;
        }

        if (_position < _entries.Count - 1)
        {
            _position++;
            return _entries[_position];
        }

        string draft = _draft;
        Reset();
        return draft;
    }

    /// <summary>Ends a navigation in progress and forgets its draft. The terms are kept.</summary>
    public void Reset()
    {
        _position = -1;
        _draft = string.Empty;
    }
}
