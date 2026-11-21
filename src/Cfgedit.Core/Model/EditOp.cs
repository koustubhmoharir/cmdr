namespace Cfgedit.Core.Model;

/// <summary>
/// An edit operation on a document. The operation types are added with the edit milestone; this base type holds their
/// place in the <see cref="IConfigFormat.Plan"/> signature.
/// </summary>
public abstract record EditOp;
