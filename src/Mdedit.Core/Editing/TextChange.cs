namespace Mdedit.Core.Editing;

/// <summary>
/// The one replacement that turns one text into another: the characters the two texts share at their start and end are kept, and the
/// changed middle is replaced. Also the common prefix and suffix lengths the verification uses.
/// </summary>
internal static class TextChange
{
    /// <summary>The replacement that turns <paramref name="before"/> into <paramref name="after"/>, covering only the changed middle.</summary>
    /// <param name="before">The text before the change.</param>
    /// <param name="after">The text after the change.</param>
    /// <returns>The offset of the replaced text, its length in <paramref name="before"/>, and the text that replaces it.</returns>
    public static (int Start, int Length, string Text) Of(string before, string after)
    {
        int prefix = CommonPrefix(before, after);
        int suffix = Math.Min(CommonSuffix(before, after), Math.Min(before.Length, after.Length) - prefix);
        return (prefix, before.Length - prefix - suffix, after.Substring(prefix, after.Length - prefix - suffix));
    }

    /// <summary>The length of the longest common prefix of two strings.</summary>
    public static int CommonPrefix(string a, string b)
    {
        int length = 0;
        while (length < a.Length && length < b.Length && a[length] == b[length])
        {
            length++;
        }

        return length;
    }

    /// <summary>The length of the longest common suffix of two strings.</summary>
    public static int CommonSuffix(string a, string b)
    {
        int length = 0;
        while (length < a.Length && length < b.Length && a[a.Length - 1 - length] == b[b.Length - 1 - length])
        {
            length++;
        }

        return length;
    }
}
