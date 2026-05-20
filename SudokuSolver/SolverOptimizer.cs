namespace SudokuSolver;

// Derives implicit innie/outie sum constraints from killer cages overlapping Sudoku houses.
// Mirrors ISS optimizer.js `_makeInnieOutieSumHandlers`.
public partial class Solver
{
    private const int _optimizerMaxSumSize = 6;

    // Called from FinalizeConstraints after InitSeenMap, before InitCandidates loops.
    private void AddInnieCageConstraints()
    {
        // Collect killer cage pieces with positive sums
        var pieces = new List<(HashSet<int> cells, int sum)>();
        foreach (var constraint in constraints)
        {
            if (constraint is KillerCageConstraint kc && kc.sum > 0)
            {
                pieces.Add((new HashSet<int>(kc.cells.Select(c => CellIndex(c.Item1, c.Item2))), kc.sum));
            }
        }

        if (pieces.Count == 0) return;

        // Set of cell indices that appear in any killer cage (for MaxSumSize gate)
        var cellsWithSum = new HashSet<int>();
        foreach (var (cells, _) in pieces)
            foreach (int cell in cells)
                cellsWithSum.Add(cell);

        int houseSum = MAX_VALUE * (MAX_VALUE + 1) / 2;
        var seenKeys = new HashSet<string>();

        // Houses: rows fwd/rev, cols fwd/rev, boxes (one direction)
        var rowHouses = Groups
            .Where(g => g.GroupType == GroupType.Row && g.Cells.Count == MAX_VALUE)
            .OrderBy(g => g.Cells[0])
            .Select(g => new HashSet<int>(g.Cells))
            .ToList();

        var colHouses = Groups
            .Where(g => g.GroupType == GroupType.Column && g.Cells.Count == MAX_VALUE)
            .OrderBy(g => g.Cells[0])
            .Select(g => new HashSet<int>(g.Cells))
            .ToList();

        var boxHouses = Groups
            .Where(g => g.GroupType == GroupType.Region && g.Cells.Count == MAX_VALUE)
            .OrderBy(g => g.Cells[0])
            .Select(g => new HashSet<int>(g.Cells))
            .ToList();

        var allRegionSets = new List<List<HashSet<int>>>();
        if (rowHouses.Count >= 2)
        {
            allRegionSets.Add(rowHouses);
            var rev = new List<HashSet<int>>(rowHouses);
            rev.Reverse();
            allRegionSets.Add(rev);
        }
        if (colHouses.Count >= 2)
        {
            allRegionSets.Add(colHouses);
            var rev = new List<HashSet<int>>(colHouses);
            rev.Reverse();
            allRegionSets.Add(rev);
        }
        if (boxHouses.Count >= 2)
        {
            allRegionSets.Add(boxHouses);
        }

        foreach (var regionList in allRegionSets)
        {
            ProcessInnieOutieOverlap(regionList, pieces, cellsWithSum, houseSum, seenKeys);
        }
    }

    private void ProcessInnieOutieOverlap(
        List<HashSet<int>> regions,
        List<(HashSet<int> cells, int sum)> allPieces,
        HashSet<int> cellsWithSum,
        int houseSum,
        HashSet<string> seenKeys)
    {
        int numRegions = regions.Count;
        var superRegion = new HashSet<int>();
        var piecesRegion = new HashSet<int>();
        var remainingPieces = new List<(HashSet<int> cells, int sum)>(allPieces);
        var usedPieces = new List<(HashSet<int> cells, int sum)>();

        int i = 0;
        foreach (var region in regions)
        {
            i++;
            if (i == numRegions) break;

            foreach (int cell in region) superRegion.Add(cell);

            // Include pieces with >50% of cells inside the super-region
            for (int pi = remainingPieces.Count - 1; pi >= 0; pi--)
            {
                var (pCells, pSum) = remainingPieces[pi];
                int intersect = CountSetIntersection(superRegion, pCells);
                if (intersect * 2 > pCells.Count)
                {
                    remainingPieces.RemoveAt(pi);
                    foreach (int cell in pCells) piecesRegion.Add(cell);
                    usedPieces.Add((pCells, pSum));
                }
            }

            HandleInnieOutie(superRegion, piecesRegion, usedPieces, cellsWithSum, houseSum, seenKeys);
        }
    }

    private void HandleInnieOutie(
        HashSet<int> superRegion,
        HashSet<int> piecesRegion,
        List<(HashSet<int> cells, int sum)> usedPieces,
        HashSet<int> cellsWithSum,
        int houseSum,
        HashSet<string> seenKeys)
    {
        // diffA: cells in super-region not covered by any cage piece
        // diffB: cage cells outside the super-region
        var diffA = new List<int>();
        foreach (int c in superRegion)
            if (!piecesRegion.Contains(c)) diffA.Add(c);

        var diffB = new List<int>();
        foreach (int c in piecesRegion)
            if (!superRegion.Contains(c)) diffB.Add(c);

        // No cages assigned to this super-region: constraint would be the trivial
        // "sum of entire house = houseSum" which Sudoku already enforces.
        if (usedPieces.Count == 0) return;

        int sizeA = diffA.Count;
        int sizeB = diffB.Count;

        if (sizeA == 0 && sizeB == 0) return;
        if (sizeA + sizeB > MAX_VALUE) return;
        if (sizeA > 2 && sizeB > 2) return;

        bool allHaveSum = diffA.All(c => cellsWithSum.Contains(c)) &&
                          diffB.All(c => cellsWithSum.Contains(c));
        if (allHaveSum && sizeA + sizeB > _optimizerMaxSumSize) return;

        // sumDelta = Σ(cage_sums) - k * houseSum; constraint: Σ(diffB) - Σ(diffA) = sumDelta
        int k = superRegion.Count / MAX_VALUE;
        int sumDelta = usedPieces.Sum(p => p.sum) - k * houseSum;

        // Swap so that diffA is the smaller (or equal) side
        if (sizeA > sizeB)
        {
            (diffA, diffB) = (diffB, diffA);
            sumDelta = -sumDelta;
            (sizeA, sizeB) = (sizeB, sizeA);
        }

        diffA.Sort();
        diffB.Sort();

        // Deduplicate: key = sorted diffB | sorted diffA | sumDelta
        string key = string.Join(",", diffB) + "|" + string.Join(",", diffA) + "|" + sumDelta;
        if (!seenKeys.Add(key)) return;

        var posCells = diffB.Select(c => (c / WIDTH, c % WIDTH)).ToList();
        var negCells = diffA.Select(c => (c / WIDTH, c % WIDTH)).ToList();

        AddConstraint(new InnieCageConstraint(this, posCells, negCells, sumDelta));
    }

    private static int CountSetIntersection(HashSet<int> a, HashSet<int> b)
    {
        int count = 0;
        if (a.Count < b.Count)
        {
            foreach (int x in a)
                if (b.Contains(x)) count++;
        }
        else
        {
            foreach (int x in b)
                if (a.Contains(x)) count++;
        }
        return count;
    }
}
