using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

/// <summary>Shared builders for session tests.</summary>
internal static class SessionTestHelpers
{
    /// <summary>A command running <c>sh -c script</c>.</summary>
    public static CommandSpec Sh(string name, string script) =>
        new() { Name = name, Program = "sh", Args = new List<string> { "-c", script } };

    /// <summary>A long-running command that exits on Ctrl+C (SIGINT). Used instead of a shell loop, which may ignore SIGINT.</summary>
    public static CommandSpec Sleeper(string name) =>
        new() { Name = name, Program = "sleep", Args = new List<string> { "30" } };

    /// <summary>Settings for AddGroup/UpdateGroup: no cwd or env.</summary>
    public static GroupSettings Settings(string name, GroupMode mode = GroupMode.Parallel, string? description = null) =>
        new(name, description, mode, null, null);

    /// <summary>A group definition with the given commands.</summary>
    public static GroupSpec Group(string name, GroupMode mode, params CommandSpec[] commands) =>
        new() { Name = name, Mode = mode, Items = commands.Cast<ItemSpec>().ToList() };

    /// <summary>Writes a configuration into the temp directory and loads it, so the session starts clean (not dirty).</summary>
    public static CmdrSession OpenWith(TempDir dir, params GroupSpec[] groups)
    {
        string path = dir.File("cmdr.json");
        ConfigStore.Save(new CmdrConfig { Groups = groups.ToList() }, path);
        return CmdrSession.Load(path);
    }

    /// <summary>Loads a session from a configuration file that does not exist (an empty session).</summary>
    public static CmdrSession OpenEmpty(TempDir dir) => CmdrSession.Load(dir.File("cmdr.json"));

    /// <summary>
    /// Force-kills everything still active and disposes the session. Callers use it in finally blocks, so it disposes first
    /// and then fails the test (xUnit assertion) only if commands are still active after two bounded kill rounds. Such a
    /// failure replaces an exception already in flight from the test body; the leftover names are the more useful report.
    /// </summary>
    public static void Cleanup(CmdrSession session)
    {
        string[] leftovers = Array.Empty<string>();
        try
        {
            leftovers = KillUntilIdle(session);
        }
        finally
        {
            session.Dispose();
        }

        if (leftovers.Length > 0)
        {
            Assert.Fail($"Commands still active after force kill (test cleanup): {string.Join(", ", leftovers)}.");
        }
    }

    /// <summary>
    /// Kills, waits up to 5 s, and repeats once for commands started in between. Returns the names of commands still active,
    /// or an empty array when none are left.
    /// </summary>
    private static string[] KillUntilIdle(CmdrSession session)
    {
        for (int round = 0; round < 2; round++)
        {
            session.ForceKillAll();
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (session.ActiveEntries.Count > 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
            }

            if (session.ActiveEntries.Count == 0)
            {
                return Array.Empty<string>();
            }
        }

        return session.ActiveEntries.Select(e => e.Name).ToArray();
    }
}
