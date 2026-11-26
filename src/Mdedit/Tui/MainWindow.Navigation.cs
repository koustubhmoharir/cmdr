using Mdedit.Core.Render;
using Shared.Tui;
using Terminal.Gui.Input;

namespace Mdedit.Tui;

/// <summary>
/// The links of the window, in view mode: the message line shows the link under the caret, Tab and Shift+Tab move between links, Enter and a
/// Ctrl+click follow one, and Alt+Left goes back to the file that a followed link left.
/// </summary>
/// <remarks>
/// <para>
/// <b>Following.</b> An anchor goes to the heading in this window (see <see cref="HeadingAnchors"/>). A URL is copied with OSC 52, as Copy is,
/// and mdedit opens nothing itself. A file is loaded first (a load error is shown and the window stays), then the unsaved-changes flow of
/// Ctrl+O runs (Cancel keeps the window); on success the window pushes the place it leaves on the back stack, sets <see cref="SwitchTo"/> and
/// <see cref="GoToFragment"/>, and stops. The host shows the file in a new window, which starts at the fragment's heading.
/// </para>
/// <para>
/// <b>Back.</b> Alt+Left takes the place on top of the back stack. The file is loaded from disk first; a load error is shown and the entry is
/// dropped. Otherwise the unsaved-changes flow runs, and on success the window stops with <see cref="SwitchTo"/> and <see cref="RestoreTo"/>
/// set: the new window starts with the caret and the top line where the entry had them, as far as the file's text allows.
/// </para>
/// <para>
/// <b>The stack.</b> It is shared with the host for one run of documents (see <see cref="BackStack"/>). Ctrl+O clears it when a file is chosen
/// and the document is left; a quit or a load error does not.
/// </para>
/// </remarks>
internal sealed partial class MainWindow
{
    /// <summary>The message after Tab or Shift+Tab wraps from the last link to the first.</summary>
    internal const string WrappedToFirstLink = "Wrapped to the first link";

    /// <summary>The message after Shift+Tab wraps from the first link to the last.</summary>
    internal const string WrappedToLastLink = "Wrapped to the last link";

    /// <summary>The message after Tab or Shift+Tab in a document with no link.</summary>
    internal const string NoLinks = "No links";

    /// <summary>The message after Alt+Left with no file to go back to.</summary>
    internal const string NoPreviousFile = "No previous file";

    /// <summary>The message after Enter or Ctrl+click on a link with no destination.</summary>
    internal const string EmptyLink = "Empty link";

    /// <summary>The arrow before the URL of the caret's link on the message line.</summary>
    internal const string LinkArrow = "→ ";

    /// <summary>The message for a link's fragment that names no heading of the document.</summary>
    internal static string NoHeading(string fragment) => $"No heading #{fragment}";

    // Loads a file for a followed link or a Back, as the picker loads one: the file, or the message why it does not load.
    private readonly Func<string, (OpenedFile? Value, string? Error)> _loadFile;

    // The places that followed file links left, shared with the host for one run of documents.
    private readonly BackStack _back;

    /// <summary>
    /// The heading (the fragment of the link) that the window goes to when a followed file link opens a new window, or null. Set with
    /// <see cref="SwitchTo"/> and read by the host.
    /// </summary>
    public string? GoToFragment { get; private set; }

    /// <summary>
    /// The place that a Back returns to, when Alt+Left opened the window's successor (see <see cref="SwitchTo"/>), or null. The host gives it to
    /// the next window as its restore point.
    /// </summary>
    public BackEntry? RestoreTo { get; private set; }

    /// <summary>The message line's left part: the link's URL while the caret is on a link, else the heading path.</summary>
    internal string LeftStatus() => _view.CaretLink is { } link ? LinkArrow + link.Url : HeadingPath();

    /// <summary>
    /// The window's link keys in view mode: Tab and Shift+Tab (with the message of a wrap), Enter on a link, and Alt+Left. Returns false for a
    /// key it does not bind. The view has already taken the keys it moves the caret with.
    /// </summary>
    private bool HandleLinkKey(Key key)
    {
        if (key == Key.Tab)
        {
            ShowLinkMove(_view.MoveToLink(forward: true), forward: true);
            return true;
        }

        if (key == Key.Tab.WithShift)
        {
            ShowLinkMove(_view.MoveToLink(forward: false), forward: false);
            return true;
        }

        if (key == Key.Enter && _view.CaretLink is { } link)
        {
            FollowLink(link);
            return true;
        }

        if (key == Key.CursorLeft.WithAlt)
        {
            GoBack();
            return true;
        }

        return false;
    }

    /// <summary>Shows the message of a Tab or Shift+Tab that wrapped or found no link. A move that did not wrap shows the link's URL, as the status does.</summary>
    private void ShowLinkMove(LinkMove move, bool forward)
    {
        switch (move)
        {
            case LinkMove.NoLinks:
                ShowMessage(NoLinks, error: true);
                break;
            case LinkMove.Wrapped:
                ShowMessage(forward ? WrappedToFirstLink : WrappedToLastLink, error: false);
                break;
        }
    }

    /// <summary>
    /// Follows a link by its destination: an anchor, a file (or a URL, which is copied). Nothing happens in edit mode, where links are text.
    /// </summary>
    /// <param name="link">The link.</param>
    private void FollowLink(LinkInfo link)
    {
        if (_editing)
        {
            return;
        }

        switch (LinkTargets.Classify(link.Url))
        {
            case LinkTarget.Anchor anchor:
                FollowAnchor(anchor.Fragment);
                break;
            case LinkTarget.File file:
                FollowFile(file.Path, file.Fragment);
                break;
            case LinkTarget.External external:
                CopyLink(external.Url);
                break;
        }
    }

    /// <summary>Goes to the heading that an anchor names, in this window. A heading that does not exist is reported.</summary>
    /// <param name="fragment">The fragment after the <c>#</c>, as written.</param>
    private void FollowAnchor(string fragment)
    {
        if (HeadingAnchors.Find(HeadingAnchors.Of(_view.Document), fragment) is not { } start)
        {
            ShowMessage(NoHeading(fragment), error: true);
            return;
        }

        _view.GoTo(start, start);
    }

    /// <summary>
    /// Opens a file of a link in a new window: the file is loaded, the unsaved-changes flow runs, and the window stops with the file to show.
    /// A path that is not a file is reported as not found, with the path as the link wrote it.
    /// </summary>
    /// <param name="path">The path of the link, relative to this file's folder unless it is absolute.</param>
    /// <param name="fragment">The fragment of the link (the heading to go to), or null.</param>
    private void FollowFile(string path, string? fragment)
    {
        string full = Path.GetFullPath(path, Path.GetDirectoryName(CurrentPath)!);
        if (!File.Exists(full))
        {
            ShowMessage($"Not found: {path}", error: true);
            return;
        }

        (OpenedFile? file, string? error) = _loadFile(full);
        if (file is null)
        {
            ShowMessage(error ?? "the file cannot be read", error: true);
            return;
        }

        if (CloseOrKeep("Open") != CloseOutcome.Closed)
        {
            return;
        }

        _back.Push(new BackEntry(CurrentPath, _view.CursorOffset, _view.TopOffset));
        SwitchTo = file;
        GoToFragment = fragment;
        Stop();
    }

    /// <summary>
    /// Alt+Left: the file on top of the back stack is loaded and the window stops for it, with the caret and the top line restored. With an
    /// empty stack, or when the file does not load (the entry is then dropped), the message says so.
    /// </summary>
    private void GoBack()
    {
        if (_back.Peek() is not { } entry)
        {
            ShowMessage(NoPreviousFile, error: true);
            return;
        }

        (OpenedFile? file, string? error) = _loadFile(entry.Path);
        if (file is null)
        {
            _back.Pop();
            ShowMessage(error ?? "the file cannot be read", error: true);
            return;
        }

        if (CloseOrKeep("Open") != CloseOutcome.Closed)
        {
            return;
        }

        _back.Pop();
        SwitchTo = file;
        RestoreTo = entry;
        Stop();
    }

    /// <summary>
    /// Enter on a link, or a Ctrl+click: the link is copied when it is a URL. Copying is done as Copy is: OSC 52 when the text fits, else the
    /// message says it is too large. An empty destination is reported.
    /// </summary>
    /// <param name="url">The URL of the link.</param>
    private void CopyLink(string url)
    {
        if (url.Length == 0)
        {
            ShowMessage(EmptyLink, error: true);
            return;
        }

        if (!Osc52.Fits(url))
        {
            ShowMessage(TooLargeToCopy, error: true);
            return;
        }

        WriteRaw(Osc52.Build(url));
        ShowMessage($"Copied link: {url}", error: false);
    }

    /// <summary>The full path of the file the window shows.</summary>
    private string CurrentPath => Path.GetFullPath(_opened.Path);

    /// <summary>
    /// Places the window at the start it was given: the restore point of a Back (its caret and top line, clamped to the text), else the
    /// heading of a link's fragment. A large file in edit mode has no rendered headings, so a fragment does nothing there.
    /// </summary>
    /// <param name="restore">The place a Back returns to, or null.</param>
    /// <param name="fragment">The fragment of the link that opened the window, or null.</param>
    /// <param name="textLength">The length of the file's text.</param>
    private void ApplyStart(BackEntry? restore, string? fragment, int textLength)
    {
        if (restore is not null)
        {
            PlaceStart(
                Math.Clamp(restore.CaretOffset, 0, textLength),
                Math.Clamp(restore.TopOffset, 0, textLength));
            return;
        }

        if (fragment is null || _editing)
        {
            return;
        }

        if (HeadingAnchors.Find(HeadingAnchors.Of(_view.Document), fragment) is { } start)
        {
            PlaceStart(start, start);
        }
        else
        {
            ShowMessage(NoHeading(fragment), error: true);
        }
    }

    // Puts the caret at an offset and the top line at another: the view in view mode, the editor's caret in edit mode.
    private void PlaceStart(int caret, int top)
    {
        if (_editing)
        {
            _editor.PlaceCaret(caret, null);
        }
        else
        {
            _view.GoTo(caret, top);
        }
    }
}
