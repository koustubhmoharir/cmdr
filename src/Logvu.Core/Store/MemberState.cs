namespace Logvu.Core.Store;

/// <summary>Where a member stands in the catalog.</summary>
public enum MemberState
{
    /// <summary>The member has no complete event yet, so it has no place in the timeline.</summary>
    Pending,

    /// <summary>The member's first event has a time, and the member is in the timeline by it.</summary>
    Dated,

    /// <summary>The member is in the timeline, in the unknown-time group before all dated members.</summary>
    UnknownTime,

    /// <summary>The file is gone. Its rows stay, but it is no longer in the timeline and its disk reads report that.</summary>
    Expired,
}
