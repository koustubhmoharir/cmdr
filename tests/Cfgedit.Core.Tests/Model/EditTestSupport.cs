using Cfgedit.Core;
using Cfgedit.Core.Model;

namespace Cfgedit.Core.Tests.Model;

/// <summary>Helpers for the edit tests: applying an operation to text, and writing paths by hand.</summary>
internal static class EditTestSupport
{
    public static readonly IConfigFormat Strict = Formats.FromName("json")!;
    public static readonly IConfigFormat Jsonc = Formats.FromName("jsonc")!;
    public static readonly IConfigFormat Json5 = Formats.FromName("json5")!;
    public static readonly IConfigFormat Xml = Formats.FromName("xml")!;
    public static readonly IConfigFormat Env = Formats.FromName("env")!;
    public static readonly IConfigFormat Toml = new TomlFormat();

    /// <summary>The path of the root value of a JSON document.</summary>
    public static readonly NodePath RootValue = At(Item(0));

    public static NodePath At(params PathStep[] steps) => new(steps);

    public static PathStep Member(string key, int ordinal = 0) => new MemberStep(key, ordinal);

    public static PathStep Item(int index) => new ItemStep(index);

    public static PathStep Comment(int index) => new CommentStep(index);

    public static PathStep Element(string name, int ordinal = 0) => new ElementStep(name, ordinal);

    /// <summary>Applies an operation to text and returns the new text.</summary>
    public static string Edit(string text, IConfigFormat format, EditOp op)
    {
        return ModelTestSupport.Parse(text, format).Apply(op).Document.Source.Text;
    }

    /// <summary>Applies an operation that must fail, and returns the exception.</summary>
    public static CfgeditException Fails(string text, IConfigFormat format, EditOp op)
    {
        return Assert.Throws<CfgeditException>(() => ModelTestSupport.Parse(text, format).Apply(op));
    }

    /// <summary>Applies an operation and returns the new text, with the focus path.</summary>
    public static (string Text, NodePath? Focus) EditWithFocus(string text, IConfigFormat format, EditOp op)
    {
        EditResult result = ModelTestSupport.Parse(text, format).Apply(op);
        return (result.Document.Source.Text, result.Focus);
    }
}
