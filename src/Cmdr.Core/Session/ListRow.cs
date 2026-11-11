namespace Cmdr.Core.Session;

/// <summary>A row of the command list: either a group header or a command.</summary>
public abstract record ListRow;

/// <summary>The header row of a group.</summary>
/// <param name="Group">The group shown by this row.</param>
public sealed record GroupRow(GroupEntry Group) : ListRow;

/// <summary>A command row.</summary>
/// <param name="Entry">The command shown by this row.</param>
public sealed record CommandRow(CommandEntry Entry) : ListRow;
