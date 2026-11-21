namespace Cfgedit.Core.Model;

/// <summary>
/// One step of a <see cref="NodePath"/>. Steps are records, so two steps with the same fields are equal. Each one's
/// <c>ToString</c> is the form shown in the detail line and in test failures.
/// </summary>
public abstract record PathStep;

/// <summary>A JSON member: its decoded key, and the ordinal among members with the same key before it.</summary>
public sealed record MemberStep(string Key, int Ordinal) : PathStep
{
    /// <inheritdoc/>
    public override string ToString()
    {
        return $"Member({DisplayText.Quote(Key)}, {Ordinal})";
    }
}

/// <summary>A JSON array item: its index among the items of its array.</summary>
public sealed record ItemStep(int Index) : PathStep
{
    /// <inheritdoc/>
    public override string ToString()
    {
        return $"Item({Index})";
    }
}

/// <summary>A comment: its index among the comments of its parent. Used by JSON and XML.</summary>
public sealed record CommentStep(int Index) : PathStep
{
    /// <inheritdoc/>
    public override string ToString()
    {
        return $"Comment({Index})";
    }
}

/// <summary>An XML element: its name, and the ordinal among elements of the same name before it.</summary>
public sealed record ElementStep(string Name, int Ordinal) : PathStep
{
    /// <inheritdoc/>
    public override string ToString()
    {
        return $"Element({DisplayText.Quote(Name)}, {Ordinal})";
    }
}

/// <summary>An XML text or CDATA node: its index among the non-whitespace text and CDATA nodes of its parent.</summary>
public sealed record TextStep(int Index) : PathStep
{
    /// <inheritdoc/>
    public override string ToString()
    {
        return $"Text({Index})";
    }
}

/// <summary>An XML processing instruction: its index among the processing instructions of its parent.</summary>
public sealed record PIStep(int Index) : PathStep
{
    /// <inheritdoc/>
    public override string ToString()
    {
        return $"PI({Index})";
    }
}

/// <summary>The XML declaration. There is at most one, directly under the document node.</summary>
public sealed record DeclarationStep : PathStep
{
    /// <inheritdoc/>
    public override string ToString()
    {
        return "Declaration";
    }
}

/// <summary>The XML document type declaration. There is at most one, directly under the document node.</summary>
public sealed record DoctypeStep : PathStep
{
    /// <inheritdoc/>
    public override string ToString()
    {
        return "Doctype";
    }
}
