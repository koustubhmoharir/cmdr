namespace Logvu.Core.Store;

/// <summary>The direction of a search through the timeline.</summary>
public enum SearchDirection
{
    /// <summary>Toward older events: the search scans the events strictly older than its start.</summary>
    Older,

    /// <summary>Toward newer events: the search scans the events strictly newer than its start.</summary>
    Newer,
}
