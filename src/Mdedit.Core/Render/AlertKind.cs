namespace Mdedit.Core.Render;

/// <summary>The kind of an alert, as written in its first line: <c>[!NOTE]</c>, <c>[!TIP]</c> and so on.</summary>
public enum AlertKind
{
    /// <summary>An alert of kind <c>NOTE</c>.</summary>
    Note,

    /// <summary>An alert of kind <c>TIP</c>.</summary>
    Tip,

    /// <summary>An alert of kind <c>IMPORTANT</c>.</summary>
    Important,

    /// <summary>An alert of kind <c>WARNING</c>.</summary>
    Warning,

    /// <summary>An alert of kind <c>CAUTION</c>.</summary>
    Caution,
}
