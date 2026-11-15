using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Cmdr.Core.Processes;

/// <summary>
/// A process group on Linux, identified by its pgid (equal to the leader's pid when launched through setsid).
/// </summary>
internal sealed class LinuxProcessGroup : IProcessGroup
{
    private const int SigInt = 2;
    private const int SigKill = 9;
    private const int ErrorNoSuchProcess = 3; // ESRCH

    private readonly int _pgid;
    private volatile bool _detached;

    /// <summary>Creates a handle for the process group <paramref name="pgid"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pgid"/> is not positive.</exception>
    public LinuxProcessGroup(int pgid)
    {
        // A non-positive value would address the caller's own group or every process.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pgid);
        _pgid = pgid;
    }

    /// <inheritdoc />
    /// <remarks>Does nothing after <see cref="Detach"/>.</remarks>
    /// <exception cref="Win32Exception">The signal could not be delivered for a reason other than the group being gone.</exception>
    public void Interrupt()
    {
        if (!_detached)
        {
            Signal(SigInt);
        }
    }

    /// <inheritdoc />
    /// <remarks>Does nothing after <see cref="Detach"/>.</remarks>
    /// <exception cref="Win32Exception">The signal could not be delivered for a reason other than the group being gone.</exception>
    public void Kill()
    {
        if (!_detached)
        {
            Signal(SigKill);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Scans <c>/proc/[pid]/stat</c>. The command name (field 2) may contain spaces and parentheses, so fields
    /// are parsed after the last ')'. Processes that exit during the scan are skipped. A process whose stat cannot be
    /// read for lack of permission is unknown, not gone: the count throws <see cref="IOException"/> naming it.
    /// </remarks>
    /// <exception cref="IOException">The stat of a listed process is not readable (permission denied).</exception>
    public int CountLiveProcesses() => CountLiveProcesses(_pgid, "/proc", File.ReadAllText);

    /// <inheritdoc />
    /// <remarks>Marks the group as detached: after this, Interrupt and Kill do nothing. Dispose does not signal either.</remarks>
    public void Detach() => _detached = true;

    /// <inheritdoc />
    public void Dispose()
    {
        // Nothing to release: the group is identified by its id only. Does not signal anything.
    }

    /// <summary>
    /// Returns the process group id of <paramref name="pid"/> from <c>/proc/[pid]/stat</c>, or null if the process is gone
    /// or its stat cannot be read (the launch wait treats both as done).
    /// </summary>
    internal static int? ReadProcessGroupId(int pid) =>
        ReadStat($"/proc/{pid}/stat", File.ReadAllText, out _, out int pgrp) == StatRead.Ok ? pgrp : null;

    /// <summary>
    /// Counts the live processes in group <paramref name="pgid"/> among the numeric entries of <paramref name="procRoot"/>,
    /// reading each stat through <paramref name="readText"/>.
    /// </summary>
    /// <exception cref="IOException">The stat of a listed process is not readable (permission denied): the count is unknown.</exception>
    internal static int CountLiveProcesses(int pgid, string procRoot, Func<string, string> readText)
    {
        int count = 0;
        foreach (string dir in Directory.EnumerateDirectories(procRoot))
        {
            string name = Path.GetFileName(dir);
            if (name.Length == 0 || !IsAllDigits(name))
            {
                continue;
            }

            StatRead read = ReadStat(Path.Combine(dir, "stat"), readText, out char state, out int pgrp);
            if (read == StatRead.Denied)
            {
                throw new IOException($"Cannot read the stat of process {name}: permission denied.");
            }

            if (read != StatRead.Ok || pgrp != pgid)
            {
                continue;
            }

            if (state is 'Z' or 'X')
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private void Signal(int signal)
    {
        if (LinuxNative.Kill(-_pgid, signal) == 0)
        {
            return;
        }

        int error = Marshal.GetLastPInvokeError();
        if (error == ErrorNoSuchProcess)
        {
            // The group does not exist (yet): setsid may not have run. Signal the leader directly; ESRCH there
            // means it is gone too.
            if (LinuxNative.Kill(_pgid, signal) == 0)
            {
                return;
            }

            error = Marshal.GetLastPInvokeError();
            if (error == ErrorNoSuchProcess)
            {
                return;
            }
        }

        throw new Win32Exception(error);
    }

    /// <summary>The outcome of reading one stat file.</summary>
    internal enum StatRead
    {
        /// <summary>The state and pgrp were read.</summary>
        Ok,

        /// <summary>The process exited during the read (the file or its directory is gone, or the read failed with an I/O error).</summary>
        Vanished,

        /// <summary>The file exists but is not readable (permission denied).</summary>
        Denied,

        /// <summary>The file was read but its contents are not a stat line.</summary>
        Malformed,
    }

    /// <summary>
    /// Parses state (field 3) and pgrp (field 5) from a stat file. The command name (field 2) may contain spaces and
    /// parentheses, so fields are read after the last ')'.
    /// </summary>
    private static StatRead ReadStat(string statPath, Func<string, string> readText, out char state, out int pgrp)
    {
        state = '\0';
        pgrp = 0;

        string stat;
        try
        {
            stat = readText(statPath);
        }
        catch (UnauthorizedAccessException)
        {
            return StatRead.Denied;
        }
        catch (IOException)
        {
            return StatRead.Vanished;
        }

        int close = stat.LastIndexOf(')');
        if (close < 0)
        {
            return StatRead.Malformed;
        }

        string[] fields = stat.Substring(close + 1).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // fields[0] is state (field 3), fields[1] is ppid (field 4), fields[2] is pgrp (field 5).
        if (fields.Length < 3 || fields[0].Length == 0
            || !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out pgrp))
        {
            return StatRead.Malformed;
        }

        state = fields[0][0];
        return StatRead.Ok;
    }

    private static bool IsAllDigits(string text)
    {
        foreach (char c in text)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
