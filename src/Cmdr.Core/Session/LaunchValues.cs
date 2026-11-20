namespace Cmdr.Core.Session;

/// <summary>
/// The values that a start carries to every command it launches: the values of the input placeholders, and the values that the
/// environment scripts produced (see <see cref="ScriptValues"/>). Made once when an action or a restart is prepared, and passed
/// unchanged through its sequence runs and branches down to each launch.
/// </summary>
/// <param name="Inputs">The input values. Callers pass a private copy.</param>
/// <param name="Scripts">The environment script values the launches see; a command whose owner has no values fails to start.</param>
internal sealed record LaunchValues(IReadOnlyDictionary<string, string> Inputs, ScriptValues Scripts)
{
    /// <summary>No input values and no script values.</summary>
    public static LaunchValues Empty { get; } = new(new Dictionary<string, string>(), ScriptValues.Empty);
}
