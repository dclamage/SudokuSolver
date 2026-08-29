namespace SudokuSolver;

public partial class Solver
{
    // Dense candidate x candidate weak-link bitmatrix: row `c` is the set of candidates weakly
    // linked to `c`, wlMatrixWords ulongs wide, indexed [c * wlMatrixWords + (target >> 6)].
    //
    // The fourth view of the same relation, and the only one that answers "is c adjacent to any
    // member of this set" in one pass. weakLinks answers it with one binary search per member;
    // wlGrouped* and cf* are folded on a cell endpoint and cannot answer it at all. Built only when
    // some constraint declares it needs it (see Constraint.WantsWeakLinkMatrix), because it is
    // ~70 KB and ~0.1 ms per compile at 9x9 and most puzzles have no consumer. Shared by reference
    // across clones and invalidated exactly where wlGroupedOffsets is; null means "use the lists".
    private ulong[] wlMatrix;
    private int wlMatrixWords;

    /// <summary>
    /// Number of values above which the weak-link matrix is not built. Quadratic in the candidate
    /// count, so 9x9 is 70 KB and 16x16 would be 2 MB; the same ceiling <c>cfCanFire</c> uses.
    /// </summary>
    private const int WeakLinkMatrixMaxValue = 12;

    /// <summary>
    /// Whether the matrix may be built at all. <c>SUDOKU_WL_MATRIX=0</c> forces every caller onto
    /// the list path.
    /// </summary>
    /// <remarks>
    /// The matrix is an alternative way to answer a question the lists already answer, so the two
    /// arms must agree exactly and differ only in time. This switch is what lets that be checked
    /// from one build rather than two, which keeps the comparison off separately-JITted binaries.
    /// </remarks>
    internal static readonly bool WeakLinkMatrixEnabled =
        Environment.GetEnvironmentVariable("SUDOKU_WL_MATRIX") != "0";

    /// <summary>Whether the weak-link matrix is available. When false, callers must use the lists.</summary>
    public bool HasWeakLinkMatrix => wlMatrix != null;

    /// <summary>Row width in <see cref="ulong"/>s, for a caller sizing its own candidate bitset.</summary>
    public int WeakLinkMatrixWords => wlMatrixWords;

    /// <summary>
    /// Whether <paramref name="candIndex"/> is weakly linked to any candidate whose bit is set in
    /// <paramref name="candidateSet"/>. Only valid when <see cref="HasWeakLinkMatrix"/>.
    /// </summary>
    /// <remarks>
    /// One AND per word against the caller's set, rather than one binary search per member of it.
    /// The caller owns the set and can maintain it incrementally — setting one bit per member added
    /// is the whole update cost, which is what makes this cheaper than indexing the answer the other
    /// way round (a per-candidate blocked count, measured as no better than the binary searches
    /// because a backtracking caller pays the maintenance on every assign and undo).
    /// </remarks>
    public bool IsWeakLinkToAny(int candIndex, ReadOnlySpan<ulong> candidateSet)
    {
        int words = wlMatrixWords;
        int rowBase = candIndex * words;
        ulong any = 0;
        for (int w = 0; w < words; w++)
        {
            any |= wlMatrix[rowBase + w] & candidateSet[w];
        }
        return any != 0;
    }

    /// <summary>
    /// Compiles the weak-link matrix if any constraint wants it and it is not already compiled.
    /// </summary>
    /// <remarks>
    /// Called both from the setup fixpoint — so the logical solver, which never compiles the
    /// brute-force tables, still gets it — and from <see cref="CompileGroupedWeakLinks"/>, which is
    /// reached only when <see cref="AddWeakLink"/> has invalidated the derived views. That is what
    /// keeps the matrix from going stale: it is rebuilt on the same signal as its siblings rather
    /// than cached against a guess about when links stop changing.
    ///
    /// Deliberately owned by the solver rather than by the constraint that asked for it. It is
    /// derived from this board's links, so a constraint instance holding it would hand the wrong
    /// answer to any other board it were added to, and a constraint is supposed to be given a board
    /// rather than to remember one.
    /// </remarks>
    internal void CompileWeakLinkMatrix()
    {
        if (weakLinks == null || wlMatrix != null || MAX_VALUE > WeakLinkMatrixMaxValue || !WeakLinkMatrixEnabled)
        {
            return;
        }

        bool wanted = false;
        foreach (Constraint constraint in constraints)
        {
            if (constraint.WantsWeakLinkMatrix)
            {
                wanted = true;
                break;
            }
        }

        if (!wanted)
        {
            return;
        }

        int words = (NUM_CANDIDATES + 63) >> 6;
        ulong[] matrix = new ulong[NUM_CANDIDATES * words];
        for (int candIndex = 0; candIndex < NUM_CANDIDATES; candIndex++)
        {
            int rowBase = candIndex * words;
            List<int> links = weakLinks[candIndex];
            for (int i = 0; i < links.Count; i++)
            {
                int target = links[i];
                matrix[rowBase + (target >> 6)] |= 1UL << (target & 63);
            }
        }

        // One reference write publishes it. Two searches sharing this solver's links could reach
        // here concurrently; both build the same content, so the loser's array is simply dropped.
        wlMatrixWords = words;
        wlMatrix = matrix;
    }
}
