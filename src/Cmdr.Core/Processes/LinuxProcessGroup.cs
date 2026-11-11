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

            string stat;
            try
            {
                stat = File.ReadAllText(Path.Combine(dir, "stat"));
            }
            catch (IOException)
            {
                continue; // Exited during the scan.
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            int close = stat.LastIndexOf(')');
            if (close < 0)
            {
                continue;
            }

            string[] fields = stat.Substring(close + 1).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 3)
            {
                continue;
            }

            // fields[0] is state (field 3), fields[1] is ppid (field 4), fields[2] is pgrp (field 5).
            if (!int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int pgrp) || pgrp != _pgid)
            {
                continue;
            }

            char state = fields[0][0];
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

    private void Signal(int signal)
    {
        if (LinuxNative.Kill(-_pgid, signal) == 0)
        {
            return;
        }

        int error = Marshal.GetLastPInvokeError();
        if (error != ErrorNoSuchProcess)
        {
            throw new Win32Exception(error);
        }
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
