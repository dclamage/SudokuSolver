namespace SudokuSolverISS;

/// <summary>
/// Carries the best custom branching candidate found so far.
/// Score starts as the normal cell-branching score; custom finder must beat it.
/// </summary>
struct CustomCandidateResult
{
    public double Score;    // threshold: custom finder must exceed this
    public uint   Value;    // value bit to try in Cell
    public int    Cell;     // chosen cell (higher conflict score of the bivalue pair); -1 = none
    public int    AltCell;  // the other cell in the bivalue pair (for continuation)
}

/// <summary>
/// For a single house (row / column / box), scans for values that appear
/// in exactly 2 non-singleton cells.  Score = maxConflict * 0.5 (mirrors ISS
/// CandidateFinders.House._scoreValue).
///
/// When found and score beats the current result threshold, updates result
/// with the cell that has the higher conflict score (tried first by ISS).
/// </summary>
sealed class HouseCandidateFinder(int[] houseCells)
{
    public readonly int[] HouseCells = houseCells;

    public void MaybeFindCandidate(uint[] grid, int[] scores, ref CustomCandidateResult result)
    {
        for (int k = 0; k < G.SIZE; k++)
        {
            uint bit = 1u << k;
            int  cnt = 0, c0 = -1, c1 = -1, maxCS = 0;

            foreach (int c in HouseCells)
            {
                uint v = grid[c];
                if ((v & bit) == 0 || G.IsSingleton(v)) continue;
                if (++cnt > 2) goto nextValue;
                c0  = c1; c1 = c;
                int cs = scores[c];
                if (cs > maxCS) maxCS = cs;
            }

            if (cnt != 2) goto nextValue;

            // ISS formula: score = maxConflict * 0.5; replace result on >=.
            double score = maxCS * 0.5;
            if (score < result.Score) goto nextValue;

            result.Score = score;
            result.Value = bit;
            // Prefer the cell with higher conflict score (tried first).
            if (scores[c1] >= scores[c0]) { result.Cell = c1; result.AltCell = c0; }
            else                          { result.Cell = c0; result.AltCell = c1; }

            nextValue:;
        }
    }
}
