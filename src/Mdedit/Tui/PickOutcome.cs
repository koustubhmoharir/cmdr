namespace Mdedit.Tui;

/// <summary>What the picker shown with Ctrl+O did: a file chosen, the picker left with Esc, or the application quit with Ctrl+Q.</summary>
internal enum PickOutcome
{
    /// <summary>Esc: the picker was left and the document stays.</summary>
    Back,

    /// <summary>A file was opened in the picker.</summary>
    Chosen,

    /// <summary>Ctrl+Q in the picker: the application quits.</summary>
    Quit,
}
