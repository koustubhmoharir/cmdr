namespace Mdedit.Core.Render;

/// <summary>
/// The syntax token a code cell belongs to, for colouring. Code colouring (the second part of M2a) adds the token kinds;
/// until then every cell has <see cref="None"/>.
/// </summary>
public enum CodeToken
{
    /// <summary>No token: the cell is drawn in the plain code colour.</summary>
    None,
}
