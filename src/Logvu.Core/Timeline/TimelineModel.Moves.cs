namespace Logvu.Core.Timeline;

public sealed partial class TimelineModel
{
    /// <summary>
    /// Applies a queued command to the loaded rows. It returns <c>false</c> when the command needs a row beyond the window that is not
    /// loaded yet and the window does not reach that edge of history; the command then stays queued.
    /// </summary>
    private bool TryApply(TimelineCommand command) => command switch
    {
        TimelineCommand.Up => TryUp(),
        TimelineCommand.Down => TryDown(),
        TimelineCommand.ShiftUp => TryShiftUp(),
        TimelineCommand.ShiftDown => TryShiftDown(),
        TimelineCommand.PageUp => TryPageUp(),
        TimelineCommand.PageDown => TryPageDown(),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Not a navigation command."),
    };

    /// <summary>Selects the previous row. At the start of history with nothing older, it does nothing.</summary>
    private bool TryUp()
    {
        int target = _selected - 1;
        if (target >= 0)
        {
            _selected = target;
            return true;
        }

        return _atStart;
    }

    /// <summary>Selects the next row. At the end of history with nothing newer, it does nothing.</summary>
    private bool TryDown()
    {
        int target = _selected + 1;
        if (target < _rows.Count)
        {
            _selected = target;
            return true;
        }

        return _atEnd;
    }

    /// <summary>
    /// Shifts the viewport one row toward older rows. The selection stays while visible, and otherwise moves to the bottom row.
    /// </summary>
    private bool TryShiftUp()
    {
        if (_top == 0)
        {
            return _atStart;
        }

        _top--;
        _selected = Math.Min(_selected, _top + _height - 1);
        return true;
    }

    /// <summary>
    /// Shifts the viewport one row toward newer rows. The selection stays while visible, and otherwise moves to the top row.
    /// </summary>
    private bool TryShiftDown()
    {
        if (_top + _height < _rows.Count)
        {
            _top++;
            _selected = Math.Max(_selected, _top);
            return true;
        }

        return _atEnd;
    }

    /// <summary>
    /// Moves the viewport up by a viewport of rows, keeping the selected screen row. At the start of history, the selection moves to the
    /// first row instead.
    /// </summary>
    private bool TryPageUp()
    {
        if (_top > 0)
        {
            int screenRow = _selected - _top;
            _top = Math.Max(0, _top - _height);
            _selected = _top + screenRow;
            return true;
        }

        if (_atStart)
        {
            _selected = 0;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Moves the viewport down by a viewport of rows, keeping the selected screen row. At the end of history, the selection moves to the
    /// last row instead.
    /// </summary>
    private bool TryPageDown()
    {
        int maxTop = Math.Max(0, _rows.Count - _height);
        if (_top < maxTop)
        {
            int screenRow = _selected - _top;
            _top = Math.Min(maxTop, _top + _height);
            _selected = _top + screenRow;
            return true;
        }

        if (_atEnd)
        {
            _selected = LastIndex;
            return true;
        }

        return false;
    }
}
