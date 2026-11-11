using System.Diagnostics;

namespace Cmdr.Core.Processes;

/// <summary>The result of <see cref="IProcessPlatform.Prepare"/>: how to start the process and what to do after a Ctrl+C.</summary>
/// <param name="StartInfo">Start info to launch the process with.</param>
/// <param name="InputAfterInterrupt">
/// Text written to stdin after the Ctrl+C signal was delivered successfully, or null for none. Used on Windows to
/// answer the "Terminate batch job (Y/N)?" prompt of cmd.exe.
/// </param>
internal sealed record PreparedLaunch(ProcessStartInfo StartInfo, string? InputAfterInterrupt);
