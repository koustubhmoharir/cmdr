using Cmdr.Core.Config;
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

    /// <summary>A group definition with the given commands.</summary>
    public static GroupSpec Group(string name, GroupMode mode, params CommandSpec[] commands) =>
        new() { Name = name, Mode = mode, Commands = commands.ToList() };

    /// <summary>Writes a configuration into the temp directory and loads it, so the session starts clean (not dirty).</summary>
    public static CmdrSession OpenWith(TempDir dir, params GroupSpec[] groups)
    {
        string path = dir.File("cmdr.json");
        ConfigStore.Save(new CmdrConfig { Groups = groups.ToList() }, path);
        return CmdrSession.Load(path);
    }

    /// <summary>Loads a session from a configuration file that does not exist (an empty session).</summary>
    public static CmdrSession OpenEmpty(TempDir dir) => CmdrSession.Load(dir.File("cmdr.json"));

    /// <summary>Force-kills everything still active, waits briefly for leftovers, and disposes the session. Safe in finally blocks.</summary>
    public static void Cleanup(CmdrSession session)
    {
        try
        {
            session.ForceKillAll();
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (session.ActiveEntries.Count > 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
            }
        }
        finally
        {
            session.Dispose();
        }
    }
}
