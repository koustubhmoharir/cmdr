namespace Logvu.Core.Timeline;

/// <summary>A key action in the timeline. Each one except <see cref="End"/> and <see cref="TogglePause"/> is a manual navigation.</summary>
public enum TimelineCommand
{
    /// <summary>Selects the previous row, shifting the viewport at its top edge.</summary>
    Up,

    /// <summary>Selects the next row, shifting the viewport at its bottom edge.</summary>
    Down,

    /// <summary>Shifts the viewport one row toward older rows, keeping the selection visible.</summary>
    ShiftUp,

    /// <summary>Shifts the viewport one row toward newer rows, keeping the selection visible.</summary>
    ShiftDown,

    /// <summary>Moves the viewport up by one viewport of rows, keeping the selected screen row.</summary>
    PageUp,

    /// <summary>Moves the viewport down by one viewport of rows, keeping the selected screen row.</summary>
    PageDown,

    /// <summary>Resumes Follow and selects the newest row.</summary>
    End,

    /// <summary>Pauses Follow, or resumes it from Paused.</summary>
    TogglePause,
}
