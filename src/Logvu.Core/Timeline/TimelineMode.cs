namespace Logvu.Core.Timeline;

/// <summary>Whether the timeline follows the newest rows or holds the reader's position.</summary>
public enum TimelineMode
{
    /// <summary>The newest row is selected on the bottom visible row, and arrivals keep it there.</summary>
    Follow,

    /// <summary>The position is held. Arrivals are counted, and only manual navigation or an explicit resume moves the view.</summary>
    Paused,
}
