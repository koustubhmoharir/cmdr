using System.Text;
using Cfgedit.Core;
using Cfgedit.Core.Json;
using Cfgedit.Core.Model;
using Cfgedit.Core.Text;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// Verification in <see cref="ConfigDocument.Apply"/>. A planner hook (an internal seam of <see cref="JsonFormat"/>) makes the
/// planner wrong on purpose, and the verification must catch it as an internal error.
/// </summary>
public sealed class EditVerificationTests
{
    private static IConfigFormat WithHook(Func<IReadOnlyList<TextEdit>, IReadOnlyList<TextEdit>> change)
    {
        return new JsonFormat(JsonDialect.Json, (op, edits) => change(edits));
    }

    private static readonly NodePath FirstItem = At(Item(0), Item(0));

    [Fact]
    public void A_stray_character_in_the_planned_text_fails_the_reparse()
    {
        IConfigFormat format = WithHook(edits => edits.Append(new TextEdit(3, 3, "x")).ToList());
        ConfigDocument document = ModelTestSupport.Parse("[1]", format);

        CfgeditException ex = Assert.Throws<CfgeditException>(() => document.Apply(new SetPart(FirstItem, PartKind.Value, -1, "2")));

        Assert.StartsWith("internal edit error: the edited text does not parse: ", ex.Message, StringComparison.Ordinal);
        Assert.EndsWith(" (please report this)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_planned_edit_that_changes_another_value_fails_the_logical_comparison()
    {
        // The planner is replaced by an edit to the second item: the text still parses, but it is not what was asked.
        IConfigFormat format = WithHook(_ => [new TextEdit(4, 5, "7")]);
        ConfigDocument document = ModelTestSupport.Parse("[1, 2]", format);

        CfgeditException ex = Assert.Throws<CfgeditException>(() => document.Apply(new SetPart(FirstItem, PartKind.Value, -1, "5")));

        string expected = "Document\n  Array\n    Scalar scalar=Number value=\"5\"\n    Scalar scalar=Number value=\"2\"";
        string actual = "Document\n  Array\n    Scalar scalar=Number value=\"1\"\n    Scalar scalar=Number value=\"7\"";
        Assert.Equal(
            "internal edit error: the edited text does not match the operation.\nExpected:\n" + expected + "\nActual:\n" + actual + " (please report this)",
            ex.Message);
    }

    [Fact]
    public void Overlapping_planned_edits_are_an_internal_error()
    {
        IConfigFormat format = WithHook(_ => [new TextEdit(0, 2, string.Empty), new TextEdit(1, 3, string.Empty)]);
        ConfigDocument document = ModelTestSupport.Parse("[1, 2]", format);

        CfgeditException ex = Assert.Throws<CfgeditException>(() => document.Apply(new SetPart(FirstItem, PartKind.Value, -1, "5")));

        Assert.Equal("internal edit error: the planned edits overlap (please report this)", ex.Message);
    }

    [Fact]
    public void A_correct_planner_passes_verification_through_the_hook_seam()
    {
        IConfigFormat format = new JsonFormat(JsonDialect.Json, null);
        ConfigDocument document = ModelTestSupport.Parse("[1, 2]", format);

        EditResult result = document.Apply(new SetPart(FirstItem, PartKind.Value, -1, "5"));

        Assert.Equal("[5, 2]", result.Document.Source.Text);
    }

    [Fact]
    public void A_lone_surrogate_in_a_comment_cannot_be_written_and_is_refused_with_a_user_message()
    {
        // A string value would be escaped; a comment is written as it is, and UTF-8 cannot hold a lone surrogate.
        ConfigDocument document = ModelTestSupport.Parse("// x\n[1]", new JsonFormat(JsonDialect.Jsonc, null));

        CfgeditException ex = Assert.Throws<CfgeditException>(() => document.Apply(new SetPart(At(Comment(0)), PartKind.Comment, -1, "a\uD800b")));

        Assert.Equal("the text contains a character that utf-8 cannot represent", ex.Message);
    }
}
