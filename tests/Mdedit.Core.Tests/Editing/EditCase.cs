using Mdedit.Core.Editing;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Editing;

/// <summary>
/// Helpers for the rendered-view edit tests: each runs one operation on a source and returns the source and caret after it, or the refusal or
/// pending result, so that a test states the expected source and caret exactly.
/// </summary>
internal static class EditCase
{
    /// <summary>The source and caret after an edit that must be a replacement.</summary>
    /// <param name="Text">The source after the edit.</param>
    /// <param name="Caret">The caret's source offset after the edit.</param>
    public readonly record struct Outcome(string Text, int Caret);

    /// <summary>Types at a caret.</summary>
    public static Outcome Type(string source, int caret, string typed, ViewEditResult.Pending? pending = null) =>
        Apply(source, ViewEdit.Insert(RenderDocument.Parse(source), caret, typed, pending));

    /// <summary>Backspace at a caret.</summary>
    public static Outcome Backspace(string source, int caret) => Apply(source, ViewEdit.DeleteBackward(RenderDocument.Parse(source), caret));

    /// <summary>Delete at a caret.</summary>
    public static Outcome Delete(string source, int caret) => Apply(source, ViewEdit.DeleteForward(RenderDocument.Parse(source), caret));

    /// <summary>Replaces a selection, or deletes it when <paramref name="typed"/> is empty.</summary>
    public static Outcome Replace(string source, int start, int end, string typed) =>
        Apply(source, ViewEdit.ReplaceSelection(RenderDocument.Parse(source), start, end, typed));

    /// <summary>Pastes text at a caret or over a selection, which must be a replacement.</summary>
    public static Outcome Paste(string source, int start, int end, string text, ViewEditResult.Pending? pending = null) =>
        Apply(source, ViewEdit.Paste(RenderDocument.Parse(source), start, end, text, pending));

    /// <summary>Enter at a caret, with the pending block the view holds there, if any.</summary>
    public static ViewEditResult EnterAt(string source, int caret, ViewEditResult.Pending? pending = null) =>
        ViewEdit.Enter(RenderDocument.Parse(source), caret, pending);

    /// <summary>Enter at a caret, which must be a replacement; the source and caret after it.</summary>
    public static Outcome Enter(string source, int caret, ViewEditResult.Pending? pending = null) =>
        Apply(source, EnterAt(source, caret, pending));

    /// <summary>Enter over a selection, which must be a replacement; the source and caret after it.</summary>
    public static Outcome EnterOver(string source, int start, int end) =>
        Apply(source, ViewEdit.EnterOver(RenderDocument.Parse(source), start, end));

    /// <summary>The pending block an operation returns, which must be a pending result.</summary>
    public static ViewEditResult.Pending Held(ViewEditResult result) =>
        result as ViewEditResult.Pending ?? throw new Xunit.Sdk.XunitException($"Expected a pending block, got {Describe(result)}");

    /// <summary>The result of an operation, which must be a replacement.</summary>
    public static ViewEditResult.Replacement Edit(string source, ViewEditResult result) =>
        result as ViewEditResult.Replacement ?? throw new Xunit.Sdk.XunitException($"Expected a replacement, got {Describe(result)}");

    /// <summary>The refusal message of an operation, which must be a refusal.</summary>
    public static string Refusal(ViewEditResult result) =>
        result is ViewEditResult.Refusal refusal ? refusal.Message : throw new Xunit.Sdk.XunitException($"Expected a refusal, got {Describe(result)}");

    /// <summary>The source after an edit (given as its replacement) is the source with the replacement made.</summary>
    /// <param name="source">The source before the edit.</param>
    /// <param name="edit">The replacement.</param>
    public static Outcome Apply(string source, ViewEditResult.Replacement edit) =>
        new(string.Concat(source.AsSpan(0, edit.Start), edit.Text, source.AsSpan(edit.Start + edit.Length)), edit.Caret);

    /// <summary>The source after a result, which must be a replacement; the caret is returned with it.</summary>
    public static Outcome Apply(string source, ViewEditResult result)
    {
        ViewEditResult.Replacement edit = Edit(source, result);
        string text = string.Concat(source.AsSpan(0, edit.Start), edit.Text, source.AsSpan(edit.Start + edit.Length));
        return new Outcome(text, edit.Caret);
    }

    /// <summary>A readable description of a result, for failure messages.</summary>
    private static string Describe(ViewEditResult result) => result switch
    {
        ViewEditResult.Replacement edit => $"replacement {edit.Start}+{edit.Length} => \"{edit.Text}\" caret {edit.Caret}",
        ViewEditResult.Pending pending => $"pending at {pending.Offset} \"{pending.SourceText}\"",
        ViewEditResult.Refusal refusal => $"refusal \"{refusal.Message}\"",
        _ => result.GetType().Name,
    };
}
