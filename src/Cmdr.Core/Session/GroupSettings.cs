using Cmdr.Core.Config;

namespace Cmdr.Core.Session;

/// <summary>The editable settings of a group. The items and the parent are not part of it.</summary>
/// <param name="Name">Display name; unique among its siblings.</param>
/// <param name="Description">Optional description.</param>
/// <param name="Mode">How the items are started.</param>
/// <param name="Cwd">Working directory as written in the config (relative to the parent's), or null.</param>
/// <param name="Env">Environment overlay as written in the config, or null. Copied when applied.</param>
/// <param name="Expanded">Whether the group starts expanded when the configuration is loaded. Does not change the list's collapse state.</param>
public sealed record GroupSettings(
    string Name,
    string? Description,
    GroupMode Mode,
    string? Cwd,
    IReadOnlyDictionary<string, string?>? Env,
    bool Expanded = true);
