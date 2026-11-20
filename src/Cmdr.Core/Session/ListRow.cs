namespace Cmdr.Core.Session;

/// <summary>A row of the command list: a file row, a group header or a command.</summary>
public abstract record ListRow;

/// <summary>
/// The top-level row of a configuration file: the groups and commands of the file are listed below it. Every file of the session
/// has one, also a file without groups.
/// </summary>
/// <param name="Source">The file shown by this row.</param>
public sealed record FileRow(ConfigSource Source) : ListRow;

/// <summary>The header row of a group.</summary>
/// <param name="Group">The group shown by this row.</param>
public sealed record GroupRow(GroupEntry Group) : ListRow;

/// <summary>A command row.</summary>
/// <param name="Entry">The command shown by this row.</param>
public sealed record CommandRow(CommandEntry Entry) : ListRow;
