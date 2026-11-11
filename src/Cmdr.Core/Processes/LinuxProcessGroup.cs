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

    /// <summary>Creates a handle for the process group <paramref name="pgid"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pgid"/> is not positive.</exception>
    public LinuxProcessGroup(int pgid)
    {
        // A non-positive value would address the caller's own group or every process.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pgid);
        _pgid = pgid;
    }

    /// <inheritdoc />
    /// <exception cref="Win32Exception">The signal could not be delivered for a reason other than the group being gone.</exception>
    public void Interrupt() => Signal(SigInt);

    /// <inheritdoc />
    /// <exception cref="Win32Exception">The signal could not be delivered for a reason other than the group being gone.</exception>
    public void Kill() => Signal(SigKill);

    /// <inheritdoc />
    /// <remarks>
    /// Scans <c>/proc/[pid]/stat</c>. The command name (field 2) may contain spaces and parentheses, so fields
    /// are parsed after the last ')'. Processes that exit during the scan are skipped.
    /// </remarks>
    public int CountLiveProcesses()
    {
        int count = 0;
        foreach (string dir in Directory.EnumerateDirectories("/proc"))
        {
            string name = Path.GetFileName(dir);
            if (name.Length == 0 || !IsAllDigits(name))
            {
                continue;
            }

            if (!TryReadStat(Path.Combine(dir, "stat"), out char state, out int pgrp) || pgrp != _pgid)
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

    /// <inheritdoc />
    public void Dispose()
    {
        // Nothing to release: the group is identified by its id only. Does not signal anything.
    }

    /// <summary>
    /// Returns the process group id of <paramref name="pid"/> from <c>/proc/[pid]/stat</c>, or null if the process is gone.
    /// </summary>
    internal static int? ReadProcessGroupId(int pid) =>
        TryReadStat($"/proc/{pid}/stat", out _, out int pgrp) ? pgrp : null;

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

    /// <summary>
    /// Parses state (field 3) and pgrp (field 5) from a stat file. The command name (field 2) may contain spaces and
    /// parentheses, so fields are read after the last ')'. Returns false if the process vanished or the file is malformed.
    /// </summary>
    private static bool TryReadStat(string statPath, out char state, out int pgrp)
    {
        state = '\0';
        pgrp = 0;

        string stat;
        try
        {
            stat = File.ReadAllText(statPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // Exited during the read, or not readable.
        }

        int close = stat.LastIndexOf(')');
        if (close < 0)
        {
            return false;
        }

        string[] fields = stat.Substring(close + 1).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // fields[0] is state (field 3), fields[1] is ppid (field 4), fields[2] is pgrp (field 5).
        if (fields.Length < 3 || fields[0].Length == 0
            || !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out pgrp))
        {
            return false;
        }

        state = fields[0][0];
        return true;
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
