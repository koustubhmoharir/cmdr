using Logvu.Core.Parsing;

namespace Logvu.Core.Store;

/// <summary>A predicate over events, applied by <see cref="HistoryReader"/> to every event it reads. A page holds only the rows that match.</summary>
public interface IRowFilter
{
    /// <summary>Tells whether an event matches.</summary>
    /// <param name="row">The event's row.</param>
    /// <param name="ev">The parsed event, with its full message.</param>
    bool Matches(EventRow row, LogEvent ev);
}
