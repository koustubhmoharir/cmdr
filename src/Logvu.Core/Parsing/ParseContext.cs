namespace Logvu.Core.Parsing;

/// <summary>Settings that parsing needs from the outside.</summary>
/// <param name="Zone">The zone in which timestamps without an offset are interpreted.</param>
public sealed record ParseContext
{
    /// <summary>Creates the context.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="Zone"/> is <c>null</c>.</exception>
    public ParseContext(TimeZoneInfo Zone)
    {
        ArgumentNullException.ThrowIfNull(Zone);
        this.Zone = Zone;
    }

    /// <summary>The zone in which timestamps without an offset are interpreted.</summary>
    public TimeZoneInfo Zone { get; }
}
