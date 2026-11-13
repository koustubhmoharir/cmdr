using System.Text.Json.Serialization;

namespace Cmdr.Core.Config;

/// <summary>
/// Where a command's standard input comes from. Serialized as the <c>stdin</c> setting of a command.
/// </summary>
/// <remarks>
/// <see cref="Console"/> is for programs that refuse to run unless they read their own console (e.g. <c>dotnet watch</c> on
/// Windows ignores Ctrl+C when its stdin is redirected). It is honoured on Windows only; on Linux the command keeps its pipe.
/// </remarks>
[JsonConverter(typeof(StdinModeJsonConverter))]
public enum StdinMode
{
    /// <summary>cmdr holds a pipe to the command's stdin, written only by the <c>input</c> stop method. Serialized as "pipe".</summary>
    [JsonStringEnumMemberName("pipe")]
    Pipe,

    /// <summary>
    /// The command reads its own hidden console instead of a pipe, so the <c>input</c> stop method cannot be used and
    /// cmdr cannot answer a batch file's "Terminate batch job (Y/N)?" prompt. Serialized as "console".
    /// </summary>
    [JsonStringEnumMemberName("console")]
    Console,
}

/// <summary>String-only enum converter: rejects integer values such as <c>1</c>.</summary>
internal sealed class StdinModeJsonConverter : StrictEnumJsonConverter<StdinMode> { }
