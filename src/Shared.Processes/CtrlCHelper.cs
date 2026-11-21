using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Shared.Processes;

/// <summary>
/// Sends Ctrl+C to a console process on Windows. Runs inside a separate helper process started as
/// <c>cmdr --send-ctrl-c &lt;pid&gt;</c>, because cmdr itself must stay attached to the user's console for the TUI:
/// attaching to the target's console would detach cmdr from its own. The helper's console is discarded when it exits.
/// </summary>
/// <remarks>
/// Exit codes: 0 success, 1 unexpected exception, 2 <c>AttachConsole</c> failed, 3 <c>GenerateConsoleCtrlEvent</c> failed,
/// 4 <c>SetConsoleCtrlHandler</c> failed.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class CtrlCHelper
{
    private const int ExitOk = 0;
    private const int ExitUnexpected = 1;
    private const int ExitAttachFailed = 2;
    private const int ExitSignalFailed = 3;
    private const int ExitHandlerFailed = 4;

    /// <summary>Attaches to the console of <paramref name="pid"/> and delivers Ctrl+C to every process on it.</summary>
    /// <returns>The helper's exit code (see remarks on the type).</returns>
    public static int Run(int pid) => Run(pid, Console.Error);

    /// <summary>
    /// The error writer is taken before <c>FreeConsole</c>, because the console standard handles are invalid afterwards.
    /// Only a redirected stderr (a pipe, as cmdr always provides) remains usable.
    /// </summary>
    public static int Run(int pid, TextWriter error)
    {
        try
        {
            // Detach from cmdr's own console. Failure only means there was no console to detach from.
            WindowsNative.FreeConsole();

            if (WindowsNative.AttachConsole((uint)pid) == 0)
            {
                error.WriteLine($"AttachConsole failed: {Describe(Marshal.GetLastPInvokeError())}");
                return ExitAttachFailed;
            }

            // Ignore Ctrl+C in this process so the event generated below does not terminate the helper.
            if (WindowsNative.SetConsoleCtrlHandler(0, add: 1) == 0)
            {
                error.WriteLine($"SetConsoleCtrlHandler failed: {Describe(Marshal.GetLastPInvokeError())}");
                return ExitHandlerFailed;
            }

            if (WindowsNative.GenerateConsoleCtrlEvent(WindowsNative.CtrlCEvent, 0) == 0)
            {
                error.WriteLine($"GenerateConsoleCtrlEvent failed: {Describe(Marshal.GetLastPInvokeError())}");
                return ExitSignalFailed;
            }

            WindowsNative.FreeConsole();
            return ExitOk;
        }
        catch (Exception ex)
        {
            error.WriteLine($"Ctrl+C helper failed: {ex.Message}");
            return ExitUnexpected;
        }
    }

    private static string Describe(int win32Error) => new Win32Exception(win32Error).Message;
}
