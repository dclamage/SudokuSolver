using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SudokuSolver;

/// <summary>
/// Container for computing Alternating Inference Chain logic
/// 1. Finds all bivalue, bilocal, and ALS strong links
/// 2. Discovers all possible chains that provide new information
/// 3. Determines eliminations from all single chains
/// 4. Finds all cell forcing chains
/// 5. Finds all region forcing chains
/// 6. Reports the "best" one to use based on outcome
/// </summary>
internal class AICSolver
{
    // From the parent solver
    private readonly Solver solver;
    private List<LogicalStepDesc> logicalStepDescs;
    private readonly int WIDTH;
    private readonly int HEIGHT;
    private readonly int MAX_VALUE;
    private readonly int NUM_CANDIDATES;
    private readonly uint[,] board;
    private readonly SortedSet<int>[] weakLinks;

    // Tracking the best result so far
    private List<int> bestChain = null;
    private List<int> bestChainElims = null;
    private List<List<int>> bestChainForcingChains = null;
    private int bestChainDifficulty = 0;
    private int bestChainDirectSingles = 0;
    private int bestChainSinglesAfterBasics = 0;
    private string bestChainDescPrefix = null;

    // Discovered links
    private readonly Dictionary<int, StrongLinkDesc>[] strongLinks;
    // a - b = c - d: If a is true, then d is false
    private readonly Dictionary<(int, int), List<int>> discoveredWeakLinks = new();
    // a = b - c = d: If a is false then d is true
    private readonly Dictionary<(int, int), List<int>> discoveredStrongLinks = new();
    // a - b = c: If a is true then c is true
    private readonly Dictionary<(int, int), List<int>> discoveredWeakToStrongLinks = new();
    private SortedSet<int>[] discoveredWeakLinksLookup = null;
    private SortedSet<int>[] discoveredWeakToStrongLinksLookup = null;

    private readonly struct StrongLinkDesc
    {
        public readonly string humanDesc;
        public readonly List<(int, int)> alsCells;

        public StrongLinkDesc(string humanDesc, IEnumerable<(int, int)> alsCells = null)
        {
            this.humanDesc = humanDesc;
            this.alsCells = alsCells != null ? new(alsCells) : null;
        }

        public static StrongLinkDesc Empty => new(string.Empty, null);
    }

    public AICSolver(Solver solver, List<LogicalStepDesc> logicalStepDescs)
    {
        this.solver = solver;
        this.logicalStepDescs = logicalStepDescs;
        WIDTH = solver.WIDTH;
        HEIGHT = solver.HEIGHT;
        MAX_VALUE = solver.MAX_VALUE;
        NUM_CANDIDATES = solver.NUM_CANDIDATES;
        board = solver.Board;
        weakLinks = solver.WeakLinks;
        strongLinks = FindStrongLinks();

        // Seed the discovered links with the initial direct ones
        for (int cand0 = 0; cand0 < NUM_CANDIDATES; cand0++)
        {
            if (!IsCandIndexValid(cand0))
            {
                continue;
            }

            foreach (int cand1 in weakLinks[cand0])
            {
                if (IsCandIndexValid(cand1))
                {
                    discoveredWeakLinks.Add((cand0, cand1), new() { cand0, cand1 });
                }
            }

            foreach (int cand1 in strongLinks[cand0].Keys)
            {
                if (IsCandIndexValid(cand1))
                {
                    discoveredStrongLinks.Add((cand0, cand1), new() { cand0, cand1 });
                }
            }
        }
    }

    public LogicResult FindAIC()
    {
        LogicResult findChainsResult = FindChains();
        if (findChainsResult != LogicResult.Changed)
        {
            return findChainsResult;
        }

        PopulateLinkLookups();

        // Find cell forcing chains
        for (int i = 0; i < HEIGHT; i++)
        {
            for (int j = 0; j < WIDTH; j++)
            {
                uint mask = board[i, j];
                if (IsValueSet(mask) || ValueCount(mask) <= 1)
                {
                    continue;
                }

                List<int> srcCandidates = new(ValueCount(mask) - 1);
                int minVal = MinValue(mask);
                int maxVal = MaxValue(mask);
                for (int v = minVal; v <= maxVal; v++)
                {
                    if (HasValue(mask, v))
                    {
                        srcCandidates.Add(CandidateIndex(i, j, v));
                    }
                }
                if (FindForcingChains($"Cell Forcing Chain ({CellName(i, j)}): ", srcCandidates) == LogicResult.Invalid)
                {
                    return ApplyBestChain();
                }
            }
        }

        // Find region forcing chains
        foreach (var group in solver.Groups)
        {
            for (int v = 1; v <= MAX_VALUE; v++)
            {
                var cellsMustContain = group.CellsMustContain(solver, v);
                if (cellsMustContain == null || cellsMustContain.Count <= 1)
                {
                    continue;
                }

                List<int> srcCandidates = new(cellsMustContain.Count);
                foreach (var cell in cellsMustContain)
                {
                    srcCandidates.Add(CandidateIndex(cell, v));
                }

                if (FindForcingChains($"Region Forcing Chain ({group}): ", srcCandidates) == LogicResult.Invalid)
                {
                    return ApplyBestChain();
                }
            }
        }

        return ApplyBestChain();
    }

    private LogicResult FindChains()
    {
        // Keep track of all dangling chains to process
        Queue<List<int>> chainQueue = new();

        // Seed the chain stack with all candidates which have a strong link
        for (int candIndex = 0; candIndex < NUM_CANDIDATES; candIndex++)
        {
            if (IsCandIndexValid(candIndex) && strongLinks[candIndex].Count > 0)
            {
                chainQueue.Enqueue(new() { candIndex });
            }
        }

        if (chainQueue.Count == 0)
        {
            return LogicResult.None;
        }

        // Find AIC, DNL, CNL
        while (chainQueue.Count > 0)
        {
            var chain = chainQueue.Dequeue();

            // Append a strong link to each weak link and see if this causes eliminations.
            foreach (int strongIndexEnd in strongLinks[chain[^1]].Keys)
            {
                if (!IsCandIndexValid(strongIndexEnd))
                {
                    continue;
                }

                // Reject any strong links to repeated nodes, unless it's the first node that's repeated
                if (chain.IndexOf(strongIndexEnd) > 0)
                {
                    continue;
                }

                bool isDNL = chain[0] == strongIndexEnd;

                if (!isDNL)
                {
                    // Don't use this chain if none of the newly formed strong links in this chain are new
                    // 0 = 1 - 2 = 3 - 4 = 5 - 6 [= strongIndexEnd]
                    bool hasNewInformation = chain.Count < 3;
                    for (int i0 = 0; i0 < chain.Count - 1; i0 += 2)
                    {
                        int candStart = chain[i0];
                        if (chain.Count >= 3)
                        {
                            if (!discoveredStrongLinks.ContainsKey((candStart, strongIndexEnd)))
                            {
                                discoveredStrongLinks.Add((candStart, strongIndexEnd), chain.Skip(i0).Append(strongIndexEnd).ToList());
                                hasNewInformation = true;
                            }
                        }
                        foreach (int candBefore in weakLinks[candStart])
                        {
                            if (!discoveredWeakToStrongLinks.ContainsKey((candBefore, strongIndexEnd)))
                            {
                                List<int> weakToStrongChain = new(chain.Count + 2);
                                weakToStrongChain.Add(candBefore);
                                weakToStrongChain.AddRange(chain);
                                weakToStrongChain.Add(strongIndexEnd);
                                discoveredWeakToStrongLinks.Add((candBefore, strongIndexEnd), weakToStrongChain);
                                hasNewInformation = true;
                            }
                        }
                    }
                    if (!hasNewInformation)
                    {
                        continue;
                    }
                }

                List<int> newChain = new(chain.Count + 2);
                newChain.AddRange(chain);
                newChain.Add(strongIndexEnd);

                var chainElims = CalcStrongElims(newChain);
                if (chainElims.Count > 0)
                {
                    if (!CheckBestChain(newChain, chainElims.ToList(), isDNL ? "DNL: " : "AIC: "))
                    {
                        return ApplyBestChain();
                    }
                }

                if (isDNL)
                {
                    continue;
                }

                // Add a placeholder weak link
                newChain.Add(-1);

                // Check for a CNL
                if (weakLinks[strongIndexEnd].Contains(newChain[0]))
                {
                    newChain[^1] = newChain[0];

                    chainElims = CalcStrongElims(newChain);
                    chainElims.UnionWith(CalcWeakToStrongElims(newChain));
                    chainElims.UnionWith(CalcStrongToWeakElims(strongLinks, newChain));
                    if (chainElims.Count > 0)
                    {
                        if (!CheckBestChain(newChain, chainElims.ToList(), "CNL: "))
                        {
                            return ApplyBestChain();
                        }
                    }
                }

                // Add all chain continuations
                foreach (int weakIndexEnd in weakLinks[strongIndexEnd])
                {
                    if (IsCandIndexValid(weakIndexEnd) && !newChain.Contains(weakIndexEnd))
                    {
                        newChain[^1] = weakIndexEnd;

                        if (newChain.Count >= 3)
                        {
                            // Don't use this chain if none of the newly formed weak links in this chain are new
                            // 0 = 1 - 2 = 3 - 4 = 5 - 6 = 7 - weakIndexEnd
                            for (int i0 = 0; i0 < newChain.Count - 1; i0 += 2)
                            {
                                int candStart = newChain[i0];
                                foreach (int candBefore in weakLinks[candStart])
                                {
                                    if (!discoveredWeakLinks.ContainsKey((candBefore, weakIndexEnd)))
                                    {
                                        List<int> weakChain = new(newChain.Count + 1);
                                        weakChain.Add(candBefore);
                                        weakChain.AddRange(newChain);
                                        discoveredWeakLinks.Add((candBefore, weakIndexEnd), weakChain);
                                    }
                                }
                            }
                        }
                        chainQueue.Enqueue(new(newChain));

                        newChain[^1] = -1;
                    }
                }
            }
        }

        return LogicResult.Changed;
    }

    private bool CheckBestChain(List<int> chain, List<int> chainElims, string chainDescPrefix, List<List<int>> forcingChains = null)
    {
        if (chainElims.Count == 0)
        {
            return true;
        }

        // Apply the eliminations to a board clone
        Solver directSinglesSolver = solver.Clone(willRunNonSinglesLogic: false);
        foreach (int elimCandIndex in chainElims)
        {
            var (i, j, v) = solver.CandIndexToCoord(elimCandIndex);
            if (!directSinglesSolver.ClearValue(i, j, v))
            {
                bestChain = new(chain);
                bestChainElims = new(chainElims);
                bestChainForcingChains = forcingChains != null ? new(forcingChains) : null;
                bestChainDifficulty = 0;
                bestChainDirectSingles = 0;
                bestChainSinglesAfterBasics = 0;
                bestChainDescPrefix = chainDescPrefix;
                return false;
            }
        }
        if (directSinglesSolver.ApplySingles() == LogicResult.Invalid)
        {
            bestChain = new(chain);
            bestChainElims = new(chainElims);
            bestChainForcingChains = forcingChains != null ? new(forcingChains) : null;
            bestChainDifficulty = 0;
            bestChainDirectSingles = 0;
            bestChainSinglesAfterBasics = 0;
            bestChainDescPrefix = chainDescPrefix;
            return false;
        }

        Solver singlesAfterBasicsSolver = directSinglesSolver.Clone(willRunNonSinglesLogic: true);
        singlesAfterBasicsSolver.SetToBasicsOnly();
        if (singlesAfterBasicsSolver.ConsolidateBoard() == LogicResult.Invalid)
        {
            bestChain = new(chain);
            bestChainElims = new(chainElims);
            bestChainForcingChains = forcingChains != null ? new(forcingChains) : null;
            bestChainDifficulty = 0;
            bestChainDirectSingles = 0;
            bestChainSinglesAfterBasics = 0;
            bestChainDescPrefix = chainDescPrefix;
            return false;
        }

        int difficulty = forcingChains != null ? forcingChains.Sum(l => l.Count) : chain.Count;
        int numDirectSingles = directSinglesSolver.NumSetValues;
        int numSinglesAfterBasics = singlesAfterBasicsSolver.NumSetValues;
        (int, int, int, int) chainVals = (numSinglesAfterBasics, numDirectSingles, chainElims.Count, -difficulty);
        (int, int, int, int) bestChainVals = bestChain != null ? (bestChainSinglesAfterBasics, bestChainDirectSingles, bestChainElims.Count, -bestChainDifficulty) : default;
        if (bestChain == null || chainVals.CompareTo(bestChainVals) > 0)
        {
            bestChain = new(chain);
            bestChainElims = new(chainElims);
            bestChainForcingChains = forcingChains != null ? new(forcingChains) : null;
            bestChainDifficulty = difficulty;
            bestChainDirectSingles = numDirectSingles;
            bestChainSinglesAfterBasics = numSinglesAfterBasics;
            bestChainDescPrefix = chainDescPrefix;
        }

        return true;
    }

    private Dictionary<int, StrongLinkDesc>[] FindStrongLinks()
    {
        Dictionary<int, StrongLinkDesc>[] strongLinks = new Dictionary<int, StrongLinkDesc>[NUM_CANDIDATES];
        for (int candIndex = 0; candIndex < strongLinks.Length; candIndex++)
        {
            strongLinks[candIndex] = new();
        }

        void AddStrongLink(int cand0, int cand1, StrongLinkDesc desc)
        {
            if (cand0 != cand1)
            {
                if (!strongLinks[cand0].ContainsKey(cand1))
                {
                    strongLinks[cand0][cand1] = desc;
                }
                if (!strongLinks[cand1].ContainsKey(cand0))
                {
                    strongLinks[cand1][cand0] = desc;
                }
            }
        }

        // Add bivalue strong links
        for (int i = 0; i < HEIGHT; i++)
        {
            for (int j = 0; j < WIDTH; j++)
            {
                uint mask = board[i, j];
                if (!IsValueSet(mask) && ValueCount(mask) == 2)
                {
                    int v0 = MinValue(mask);
                    int v1 = MaxValue(mask);
                    int cand0 = CandidateIndex((i, j), v0);
                    int cand1 = CandidateIndex((i, j), v1);
                    AddStrongLink(cand0, cand1, StrongLinkDesc.Empty);
                }
            }
        }

        // Add bilocal strong links
        foreach (var group in solver.Groups)
        {
            if (group.Cells.Count == MAX_VALUE)
            {
                int[] valueCount = new int[MAX_VALUE];
                foreach (var (i, j) in group.Cells)
                {
                    uint mask = board[i, j];
                    for (int v = 1; v <= MAX_VALUE; v++)
                    {
                        if ((mask & ValueMask(v)) != 0)
                        {
                            valueCount[v - 1]++;
                        }
                    }
                }

                for (int v = 1; v <= MAX_VALUE; v++)
                {
                    if (valueCount[v - 1] == 2)
                    {
                        (int, int) cell0 = (-1, -1);
                        (int, int) cell1 = (-1, -1);
                        foreach (var (i, j) in group.Cells)
                        {
                            uint mask = board[i, j];
                            if ((mask & ValueMask(v)) != 0)
                            {
                                if (cell0.Item1 == -1)
                                {
                                    cell0 = (i, j);
                                }
                                else
                                {
                                    cell1 = (i, j);
                                    break;
                                }
                            }
                        }

                        int cand0 = CandidateIndex(cell0, v);
                        int cand1 = CandidateIndex(cell1, v);
                        AddStrongLink(cand0, cand1, StrongLinkDesc.Empty);
                    }
                }
            }
            else if (group.FromConstraint != null)
            {
                for (int v = 1; v <= MAX_VALUE; v++)
                {
                    var cells = group.FromConstraint.CellsMustContain(solver, v);
                    if (cells != null && cells.Count == 2)
                    {
                        int cand0 = CandidateIndex(cells[0], v);
                        int cand1 = CandidateIndex(cells[1], v);
                        string constraintName = group.FromConstraint.SpecificName;
                        StrongLinkDesc strongLinkDesc = new(constraintName);
                        AddStrongLink(cand0, cand1, strongLinkDesc);
                    }
                }
            }
        }

        // Add ALS (Almost Locked Set) strong links
        // These occur when n cells in the same group have n+1 total candidates,
        // and two of those candidates only appear once.
        // There is a strong link between those two candidates.
        // (If both were missing, then there would be n-1 candidates for n cells).
        foreach (var group in solver.Groups)
        {
            var unsetCells = group.Cells.Where(cell => !IsValueSet(board[cell.Item1, cell.Item2])).ToList();

            for (int alsSize = 2; alsSize < unsetCells.Count; alsSize++)
            {
                foreach (var combination in unsetCells.Combinations(alsSize))
                {
                    uint totalMask = 0;
                    foreach (var cell in combination)
                    {
                        totalMask |= board[cell.Item1, cell.Item2];
                    }

                    if (ValueCount(totalMask) != alsSize + 1)
                    {
                        continue;
                    }

                    List<int>[] candIndexPerValue = new List<int>[MAX_VALUE];
                    for (int v = 1; v <= MAX_VALUE; v++)
                    {
                        candIndexPerValue[v - 1] = new();
                    }
                    foreach (var (i, j) in combination)
                    {
                        uint mask = board[i, j];
                        for (int v = 1; v <= MAX_VALUE; v++)
                        {
                            if ((mask & ValueMask(v)) != 0)
                            {
                                int candIndex = CandidateIndex((i, j), v);
                                candIndexPerValue[v - 1].Add(candIndex);
                            }
                        }
                    }

                    List<int> singleValues = new();
                    for (int v = 1; v <= MAX_VALUE; v++)
                    {
                        if (candIndexPerValue[v - 1].Count == 1)
                        {
                            singleValues.Add(candIndexPerValue[v - 1][0]);
                        }
                    }

                    if (singleValues.Count > 1)
                    {
                        foreach (var candIndices in singleValues.Combinations(2))
                        {
                            int cand0 = candIndices[0];
                            int cand1 = candIndices[1];

                            string valSep = MAX_VALUE <= 9 ? string.Empty : ",";
                            StringBuilder alsDesc = new();
                            alsDesc.Append("ALS:");
                            alsDesc.Append(solver.CompactName(totalMask, combination));

                            string alsDescStr = alsDesc.ToString();
                            StrongLinkDesc strongLinkDesc = new(alsDescStr, combination);
                            AddStrongLink(cand0, cand1, strongLinkDesc);
                        }
                    }
                }
            }
        }

        return strongLinks;
    }

    private void PopulateLinkLookups()
    {
        // Construct an updated weak link lookup
        discoveredWeakLinksLookup = new SortedSet<int>[NUM_CANDIDATES];
        for (int cand = 0; cand < NUM_CANDIDATES; cand++)
        {
            discoveredWeakLinksLookup[cand] = new();
        }
        foreach (var ((cand0, cand1), chain) in discoveredWeakLinks)
        {
            discoveredWeakLinksLookup[cand0].Add(cand1);
        }

        // Construct a weak to strong links lookup
        discoveredWeakToStrongLinksLookup = new SortedSet<int>[NUM_CANDIDATES];
        for (int cand = 0; cand < NUM_CANDIDATES; cand++)
        {
            discoveredWeakToStrongLinksLookup[cand] = new();
        }
        foreach (var ((cand0, cand1), chain) in discoveredWeakToStrongLinks)
        {
            discoveredWeakToStrongLinksLookup[cand0].Add(cand1);
        }
    }

    private LogicResult FindForcingChains(string desc, List<int> srcCandidates)
    {
        bool canHaveElims = true;
        bool canHaveTruths = true;
        SortedSet<int> elims = null;
        SortedSet<int> truths = null;
        foreach (int cand0 in srcCandidates)
        {
            if (canHaveElims)
            {
                SortedSet<int> curElims = discoveredWeakLinksLookup[cand0];
                if (curElims.Count == 0)
                {
                    elims = null;
                    canHaveElims = false;
                }
                else
                {
                    if (elims == null)
                    {
                        elims = new(curElims);
                    }
                    else
                    {
                        elims.IntersectWith(curElims);
                        if (elims.Count == 0)
                        {
                            elims = null;
                            canHaveElims = false;
                            if (!canHaveTruths)
                            {
                                return LogicResult.None;
                            }
                        }
                    }
                }
            }

            if (canHaveTruths)
            {
                SortedSet<int> curTruths = discoveredWeakToStrongLinksLookup[cand0];
                if (curTruths.Count == 0)
                {
                    truths = null;
                    canHaveTruths = false;
                }
                else
                {
                    if (truths == null)
                    {
                        truths = new(curTruths);
                    }
                    else
                    {
                        truths.IntersectWith(curTruths);
                        if (truths.Count == 0)
                        {
                            truths = null;
                            canHaveTruths = false;
                            if (!canHaveElims)
                            {
                                return LogicResult.None;
                            }
                        }
                    }
                }
            }
        }

        if (canHaveElims && elims != null && elims.Count > 0)
        {
            List<int> chainElims = elims.Where(IsCandIndexValid).ToList();
            if (chainElims.Count > 0)
            {
                foreach (int elim in chainElims)
                {
                    List<List<int>> forcingChains = new();
                    foreach (int cand0 in srcCandidates)
                    {
                        forcingChains.Add(discoveredWeakLinks[(cand0, elim)]);
                    }

                    if (!CheckBestChain(srcCandidates, new() { elim }, desc, forcingChains))
                    {
                        return ApplyBestChain();
                    }
                }
            }
        }

        if (canHaveTruths && truths != null && truths.Count > 0)
        {
            List<int> chainTruths = truths.Where(IsCandIndexValid).ToList();
            if (chainTruths.Count > 0)
            {
                foreach (int truth in chainTruths)
                {
                    List<List<int>> forcingChains = new();
                    foreach (int cand0 in srcCandidates)
                    {
                        forcingChains.Add(discoveredWeakToStrongLinks[(cand0, truth)]);
                    }

                    var (ti, tj, tv) = CandIndexToCoord(truth);
                    uint tmask = board[ti, tj];
                    List<int> cellElims = new(ValueCount(tmask) - 1);
                    int tminVal = MinValue(tmask);
                    int tmaxVal = MaxValue(tmask);
                    for (int v = tminVal; v <= tmaxVal; v++)
                    {
                        if (v != tv)
                        {
                            cellElims.Add(CandidateIndex(ti, tj, v));
                        }
                    }
                    if (!CheckBestChain(srcCandidates, cellElims, desc, forcingChains))
                    {
                        return ApplyBestChain();
                    }
                }
            }
        }

        return LogicResult.None;
    }

    // 0 = 1 - 2 = 3 - 4 = 5 - 6 = 7 - 8 = 9
    // 0 = 3, 0 = 5, 0 = 7, 0 = 9
    // 2 = 5, 2 = 7, 2 = 9
    // 4 = 7, 4 = 9
    // 6 = 9
    private HashSet<int> CalcStrongElims(List<int> chain)
    {
        HashSet<int> elims = new();
        for (int chainIndex0 = 0; chainIndex0 < chain.Count; chainIndex0 += 2)
        {
            int cand0 = chain[chainIndex0];
            for (int chainIndex1 = chainIndex0 + 1; chainIndex1 < chain.Count; chainIndex1 += 2)
            {
                int cand1 = chain[chainIndex1];
                elims.UnionWith(solver.CalcElims(cand0, cand1));
            }
        }
        return elims;
    }

    // 0 = 1 - 2 = 3 - 4 = 5 - 0
    // 1 - 2, 1 - 4, 1 - 0
    // 3 - 4, 3 - 0
    // 5 - 0
    private HashSet<int> CalcWeakToStrongElims(List<int> chain)
    {
        HashSet<int> elims = new();
        for (int chainIndex0 = 1; chainIndex0 < chain.Count; chainIndex0 += 2)
        {
            int cand0 = chain[chainIndex0];
            for (int chainIndex1 = chainIndex0 + 1; chainIndex1 < chain.Count; chainIndex1 += 2)
            {
                int cand1 = chain[chainIndex1];
                elims.UnionWith(solver.CalcElims(cand0, cand1));
            }
        }
        return elims;
    }

    // For CNLs, all strong links convert to also be weak links.
    // If those weak links are part of an ALS, the other candidates
    // in the ALS must be present.
    private HashSet<int> CalcStrongToWeakElims(Dictionary<int, StrongLinkDesc>[] strongLinks, List<int> chain)
    {
        HashSet<int> elims = new();
        for (int chainIndex0 = 0; chainIndex0 < chain.Count; chainIndex0 += 2)
        {
            int cand0 = chain[chainIndex0];
            for (int chainIndex1 = chainIndex0 + 1; chainIndex1 < chain.Count; chainIndex1 += 2)
            {
                int cand1 = chain[chainIndex1];
                var (_, _, v0) = CandIndexToCoord(cand0);
                var (_, _, v1) = CandIndexToCoord(cand1);
                if (strongLinks[cand0].TryGetValue(cand1, out StrongLinkDesc strongLinkDescOut) && strongLinkDescOut.alsCells != null)
                {
                    uint totalMask = 0;
                    foreach (var cell in strongLinkDescOut.alsCells)
                    {
                        totalMask |= board[cell.Item1, cell.Item2];
                    }
                    uint clearMask = totalMask & ~ValueMask(v0) & ~ValueMask(v1) & ~valueSetMask;
                    solver.CalcElims(elims, clearMask, strongLinkDescOut.alsCells);
                }
            }
        }
        return elims;
    }

    private string DescribeChain(List<int> chain, bool firstLinkIsStrong = true)
    {
        StringBuilder chainDesc = new();
        bool strong = !firstLinkIsStrong;
        for (int ci = 0; ci < chain.Count; ci++, strong = !strong)
        {
            if (ci > 0)
            {
                if (strong)
                {
                    int candIndex0 = chain[ci - 1];
                    int candIndex1 = chain[ci];
                    if (strongLinks[candIndex0].TryGetValue(candIndex1, out StrongLinkDesc strongLinkDescOut) && !string.IsNullOrWhiteSpace(strongLinkDescOut.humanDesc))
                    {
                        chainDesc.Append($" = [{strongLinkDescOut.humanDesc}]");
                    }
                    else
                    {
                        chainDesc.Append($" = ");
                    }
                }
                else
                {
                    chainDesc.Append(" - ");
                }
            }

            if (ci + 1 < chain.Count)
            {
                int candIndex0 = chain[ci];
                int candIndex1 = chain[ci + 1];
                var (i0, j0, v0) = CandIndexToCoord(candIndex0);
                var (i1, j1, v1) = CandIndexToCoord(candIndex1);
                if (i0 == i1 && j0 == j1)
                {
                    chainDesc.Append($"({v0}{(strong ? "-" : $"=")}{v1}){CellName(i0, j0)}");
                    strong = !strong;
                    ci++;
                    continue;
                }
            }

            if (ci > 0)
            {
                int candIndex0 = chain[ci - 1];
                int candIndex1 = chain[ci];
                var (i0, j0, v0) = CandIndexToCoord(candIndex0);
                var (i1, j1, v1) = CandIndexToCoord(candIndex1);

                if (v0 == v1)
                {
                    chainDesc.Append(CellName(i1, j1));
                    continue;
                }
            }

            chainDesc.Append(solver.CandIndexDesc(chain[ci]));
        }
        return chainDesc.ToString();
    }

    private LogicResult ApplyBestChain()
    {
        if (bestChain == null)
        {
            return LogicResult.None;
        }

        // Form the description string
        if (logicalStepDescs != null)
        {
            if (bestChainForcingChains != null)
            {
                var stepDescription = new StringBuilder()
                    .Append(bestChainDescPrefix);

                List<(int i, int j, int v)> cells = bestChain.Select(CandIndexToCoord).ToList();
                var (ci, cj, cv) = cells[0];
                if (cells.All(x => x.i == ci && x.j == cj))
                {
                    stepDescription.Append(solver.ValueNames(board[ci, cj]) + CellName(ci, cj));
                }
                else if (cells.All(x => x.v == cv))
                {
                    stepDescription.Append($"{cv}{solver.CompactName(cells.Select(x => (x.i, x.j)).ToList())}");
                }
                else
                {
                    bool first = true;
                    foreach (var (i, j, v) in cells)
                    {
                        if (!first)
                        {
                            stepDescription.Append(", ");
                        }
                        stepDescription.Append($"{v}{CellName(i, j)}");
                        first = false;
                    }
                }

                stepDescription
                    .Append(" => ")
                    .Append(solver.DescribeElims(bestChainElims));

                List<LogicalStepDesc> subSteps = new();
                foreach (var chain in bestChainForcingChains)
                {
                    subSteps.Add(new(
                        desc: DescribeChain(chain, false),
                        sourceCandidates: chain,
                        elimCandidates: Enumerable.Empty<int>(),
                        sourceIsAIC: true));
                }

                logicalStepDescs.Add(new(
                    desc: stepDescription.ToString(),
                    sourceCandidates: bestChain,
                    elimCandidates: bestChainElims,
                    subSteps: subSteps));
            }
            else
            {
                var stepDescription = new StringBuilder()
                    .Append(bestChainDescPrefix)
                    .Append(DescribeChain(bestChain))
                    .Append(" => ")
                    .Append(solver.DescribeElims(bestChainElims));

                logicalStepDescs.Add(new(
                    desc: stepDescription.ToString(),
                    sourceCandidates: bestChain,
                    elimCandidates: bestChainElims,
                    sourceIsAIC: true));
            }
        }

        // Perform the eliminations
        foreach (int elimCandIndex in bestChainElims)
        {
            var (i, j, v) = CandIndexToCoord(elimCandIndex);
            if (!solver.ClearValue(i, j, v))
            {
                return LogicResult.Invalid;
            }
        }

        return LogicResult.Changed;
    }

    // Inline utility functions

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (int, int, int) CandIndexToCoord(int candIndex)
    {
        int v = (candIndex % MAX_VALUE) + 1;
        candIndex /= MAX_VALUE;

        int j = candIndex % WIDTH;
        candIndex /= WIDTH;

        int i = candIndex;
        return (i, j, v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsCandIndexValid(int candIndex)
    {
        var (i, j, v) = CandIndexToCoord(candIndex);
        uint mask = board[i, j];
        return !IsValueSet(mask) && HasValue(mask, v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int CandidateIndex(int i, int j, int v) => (i * WIDTH + j) * MAX_VALUE + v - 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int CandidateIndex((int, int) cell, int v) => (cell.Item1 * WIDTH + cell.Item2) * MAX_VALUE + v - 1;
}
