namespace Cmdr.Core.Config;

/// <summary>A configuration file in an import chain (see <see cref="ConfigStore.LoadChain"/>).</summary>
/// <param name="Path">Full path of the file.</param>
/// <param name="Config">The file's own configuration. Its groups and settings are not merged with other files.</param>
/// <param name="IsBase">False only for the file that was opened; true for each base file it imports, directly or through another base.</param>
public sealed record ConfigFile(string Path, CmdrConfig Config, bool IsBase)
{
    /// <summary>The directory that contains <see cref="Path"/>.</summary>
    public string Dir => System.IO.Path.GetDirectoryName(Path)!;
}
