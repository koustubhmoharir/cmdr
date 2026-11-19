using Logvu.Core.Config;

namespace Logvu.Core.Parsing;

/// <summary>Settings that parsing needs from the outside.</summary>
/// <param name="Zone">The zone in which timestamps without an offset are interpreted.</param>
public sealed record ParseContext
{
    /// <summary>Creates the context.</summary>
    /// <param name="Zone">The zone in which timestamps without an offset are interpreted.</param>
    /// <param name="columns">The extra columns whose values a row carries, or <c>null</c> for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="Zone"/> is <c>null</c>.</exception>
    public ParseContext(TimeZoneInfo Zone, IReadOnlyList<ColumnSpec>? columns = null)
    {
        ArgumentNullException.ThrowIfNull(Zone);
        this.Zone = Zone;
        Columns = columns ?? [];
    }

    /// <summary>The zone in which timestamps without an offset are interpreted.</summary>
    public TimeZoneInfo Zone { get; }

    /// <summary>
    /// The extra columns whose values a row built with this context carries, in configuration order. Empty when none are configured or when
    /// the rows are not displayed.
    /// </summary>
    public IReadOnlyList<ColumnSpec> Columns { get; }
}
