using System.Text;

namespace Cmdr.Tui;

/// <summary>Clipboard transport: the OSC 52 escape sequence, which terminals use to set the system clipboard. Pure.</summary>
internal static class Osc52
{
    /// <summary>Largest UTF-8 payload that is copied. Larger text is refused rather than sent.</summary>
    public const int MaxBytes = 1_000_000;

    /// <summary>True when the UTF-8 form of the text is within <see cref="MaxBytes"/>.</summary>
    public static bool Fits(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Encoding.UTF8.GetByteCount(text) <= MaxBytes;
    }

    /// <summary>ESC ] 52 ; c ; base64(UTF-8 text) BEL: sets the clipboard selection "c".</summary>
    public static string Build(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return "\u001b]52;c;" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) + "\u0007";
    }
}
