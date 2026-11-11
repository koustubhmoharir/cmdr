namespace Cmdr.Core.Config;

/// <summary>Thrown when a configuration file cannot be read or parsed.</summary>
public sealed class ConfigException : Exception
{
    public ConfigException(string message) : base(message)
    {
    }

    public ConfigException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
