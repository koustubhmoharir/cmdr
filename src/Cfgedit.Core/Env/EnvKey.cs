namespace Cfgedit.Core.Env;

/// <summary>
/// The rules for a .env key: an ASCII letter or underscore, then ASCII letters, digits, underscores, dots or hyphens.
/// </summary>
public static class EnvKey
{
    /// <summary>True when the key is a valid .env key, <c>[A-Za-z_][A-Za-z0-9_.-]*</c>.</summary>
    /// <param name="key">The key to check.</param>
    public static bool IsValid(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length == 0 || !IsStartChar(key[0]))
        {
            return false;
        }

        for (int i = 1; i < key.Length; i++)
        {
            if (!IsPartChar(key[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when the character can begin a key.</summary>
    internal static bool IsStartChar(char c)
    {
        return c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_';
    }

    /// <summary>True when the character can continue a key.</summary>
    internal static bool IsPartChar(char c)
    {
        return IsStartChar(c) || c is (>= '0' and <= '9') or '.' or '-';
    }
}
