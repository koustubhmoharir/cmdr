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
internal sealed record PreparedLaunch(ProcessStartInfo StartInfo, string? InputAfterInterrupt, string? Notice = null);
