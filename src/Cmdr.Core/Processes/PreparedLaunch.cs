using System.Diagnostics;

namespace Cmdr.Core.Processes;

/// <summary>The result of <see cref="IProcessPlatform.Prepare"/>: how to start the process and what to do after a Ctrl+C.</summary>
/// <param name="StartInfo">Start info to launch the process with.</param>
/// <param name="InputAfterInterrupt">
/// Text written to stdin after the Ctrl+C signal was delivered successfully, or null for none. Used on Windows to
/// answer the "Terminate batch job (Y/N)?" prompt of cmd.exe.
/// </param>
/// <param name="Notice">
/// A system line written to the output once the process has started, or null for none. Used when the platform could not
/// do something the launch asked for and the user needs to know it.
/// </param>
/// <param name="NullStdin">
/// True when the platform must start the process with a null standard input handle while cmdr's own is unrelated (Windows
/// console stdin). Only <see cref="IProcessPlatform.Launch"/> acts on it.
/// </param>
internal sealed record PreparedLaunch(ProcessStartInfo StartInfo, string? InputAfterInterrupt, string? Notice = null, bool NullStdin = false);
