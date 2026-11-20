using Cmdr.Core.Config;

namespace Cmdr.Core.Session;

/// <summary>
/// The opened file's own top-level settings: its <c>env</c>, <c>envScript</c>, <c>baseEnv</c> and <c>secret</c>. Null or empty
/// means the setting is not written. Applied with <see cref="Session.UpdateFileSettings"/>; the values are copied.
/// </summary>
/// <param name="Env">The top-level environment overlay, or null.</param>
/// <param name="EnvScript">The top-level environment script, or null.</param>
/// <param name="BaseEnv">The names imported from the base file, or null.</param>
/// <param name="Secret">The top-level secret names, or null.</param>
public sealed record FileSettings(
    IReadOnlyDictionary<string, string?>? Env,
    EnvScriptSpec? EnvScript,
    IReadOnlyList<string>? BaseEnv,
    IReadOnlyList<string>? Secret);
