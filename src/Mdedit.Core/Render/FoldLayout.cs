using System.Collections.Immutable;

namespace Mdedit.Core.Render;

/// <summary>
/// The fold state of a document resolved per block, for <see cref="DocumentLayout"/>: which blocks are hidden, which head is folded
/// and how many lines it hides, which block each hidden block's position is drawn at, and which unit each head is.
/// </summary>
internal sealed class FoldLayout
{
    private FoldLayout(
        ImmutableArray<FoldUnit> units,
        ImmutableArray<int> unitOfBlock,
        ImmutableArray<int> redirect,
        ImmutableArray<bool> hidden,
        ImmutableArray<int?> foldedLines)
    {
        Units = units;
        UnitOfBlock = unitOfBlock;
        Redirect = redirect;
        Hidden = hidden;
        FoldedLines = foldedLines;
    }

    /// <summary>The fold units of the document, in order of their head blocks.</summary>
    public ImmutableArray<FoldUnit> Units { get; }

    /// <summary>For each block, the index in <see cref="Units"/> of the unit it is the head of, or -1.</summary>
    public ImmutableArray<int> UnitOfBlock { get; }

    /// <summary>
    /// For each block whose position is drawn at a folded head, the index of that head block (the outermost folded unit that
    /// hides it, or the code block itself when it is a folded code unit); -1 otherwise.
    /// </summary>
    public ImmutableArray<int> Redirect { get; }

    /// <summary>Whether each block is hidden by a folded unit that contains it.</summary>
    public ImmutableArray<bool> Hidden { get; }

    /// <summary>For each folded head, the number of lines its fold hides; null for every other block.</summary>
    public ImmutableArray<int?> FoldedLines { get; }

    /// <summary>Resolves a fold state against the units of a document.</summary>
    /// <param name="blockCount">The number of blocks in the document.</param>
    /// <param name="units">The fold units of the document, in order of their head blocks.</param>
    /// <param name="folded">The folded unit keys, or null when nothing is folded.</param>
    public static FoldLayout Of(int blockCount, ImmutableArray<FoldUnit> units, IReadOnlySet<int>? folded)
    {
        var unitOfBlock = new int[blockCount];
        var redirect = new int[blockCount];
        var hidden = new bool[blockCount];
        var foldedLines = new int?[blockCount];
        Array.Fill(unitOfBlock, -1);
        Array.Fill(redirect, -1);

        for (int index = 0; index < units.Length; index++)
        {
            unitOfBlock[units[index].HeadBlock] = index;
        }

        // Units come in order of their heads, so an outer unit claims the blocks it hides before the units inside it.
        foreach (FoldUnit unit in units)
        {
            if (folded is null || !folded.Contains(unit.Start))
            {
                continue;
            }

            foldedLines[unit.HeadBlock] = unit.HiddenLines;
            for (int block = unit.HeadBlock + 1; block < unit.EndBlock; block++)
            {
                hidden[block] = true;
                if (redirect[block] < 0)
                {
                    redirect[block] = unit.HeadBlock;
                }
            }

            if (unit.Kind == FoldUnitKind.Code && redirect[unit.HeadBlock] < 0)
            {
                redirect[unit.HeadBlock] = unit.HeadBlock;
            }
        }

        return new FoldLayout(
            units,
            ImmutableArray.Create(unitOfBlock),
            ImmutableArray.Create(redirect),
            ImmutableArray.Create(hidden),
            ImmutableArray.Create(foldedLines));
    }
}
