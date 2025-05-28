namespace SudokuSolver;

public partial class Solver
{
    // Public constants
    public readonly int WIDTH;
    public readonly int HEIGHT;
    public readonly int MAX_VALUE;
    public readonly uint ALL_VALUES_MASK;
    public readonly int NUM_CELLS;
    public readonly int NUM_CANDIDATES;
    public readonly int[][][] combinations;

    // Public metadata
    public string Title { get; init; }
    public string Author { get; init; }
    public string Rules { get; init; }
    public Dictionary<string, object> customInfo;

    // Logical solver options
    public bool DisableTuples { get; set; } = false;
    public bool DisablePointing { get; set; } = false;
    public bool DisableFishes { get; set; } = false;
    public bool DisableWings { get; set; } = false;
    public bool DisableAIC { get; set; } = false;
    public bool DisableContradictions { get; set; } = false;
    public bool DisableFindShortestContradiction { get; set; } = false;

    // Private data
    private uint[] board;
    private int[] regions = null;
    private List<int>[] weakLinks;
    private int totalWeakLinks = 0;
    private readonly List<Constraint> constraints;
    private readonly List<Constraint> enforceConstraints;

    // Private state
    private bool isInSetValue = false;
    private bool isBruteForcing = false;
    private bool isInvalid;
    private int unsetCellsCount;
    private readonly List<int> pendingNakedSingles;

    // Private lookups
    private (int, int)[] candidateToCellAndValueLookup;
    private (int, int, int)[] candidateToCoordValueLookup;
    // Returns whether two cells cannot be the same value for a specific value
    // i0, j0, i1, j0, value or 0 for any value
    private bool[,,,,] seenMap;

    /// <summary>
    /// Groups which cannot contain more than one of the same digit.
    /// This will at least contain all rows, columns, and boxes.
    /// Will also contain any groups from constraints (such as killer cages).
    /// </summary>
    public List<SudokuGroup> Groups { get; }
    private List<SudokuGroup> maxValueGroups = null;
    private List<SudokuGroup> smallGroupsBySize = null;

    /// <summary>
    /// Maps a cell to the list of groups which contain that cell.
    /// </summary>
    public List<SudokuGroup>[] CellToGroupsLookup { get; }
}
