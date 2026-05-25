namespace SudokuSolverISS;

/// <summary>
/// VSIDS conflict scores — direct port of ISS ConflictTracker.
/// scores[cell]++ on backtrack; decay >>1 every 2^14 increments.
/// valueScores[bitIndex]++ on backtrack; decay >>2 (more volatile).
/// </summary>
sealed class ConflictScores
{
    private const int DecayInterval = 1 << 14;

    public readonly int[] Scores;       // per-cell
    private readonly int[] _valueScores; // per-value (0-indexed bit position)
    private int _decayCountdown = DecayInterval;

    public ConflictScores()
    {
        Scores = new int[G.NUM_CELLS];
        _valueScores = new int[G.SIZE];
    }

    public void Increment(int cell, uint valueMask)
    {
        Scores[cell]++;
        _valueScores[BitOperations.TrailingZeroCount(valueMask)]++;
        if (--_decayCountdown == 0)
        {
            Decay();
            _decayCountdown = DecayInterval;
        }
    }

    // Returns (valueMask, score) for the highest-scored value.
    public (uint valueMask, int score) GetMaxValueScore()
    {
        int maxScore = 0;
        int maxIdx = 0;
        for (int i = 0; i < G.SIZE; i++)
            if (_valueScores[i] > maxScore) { maxScore = _valueScores[i]; maxIdx = i; }
        return (1u << maxIdx, maxScore);
    }

    private void Decay()
    {
        for (int i = 0; i < Scores.Length; i++)      Scores[i] >>= 1;
        for (int i = 0; i < _valueScores.Length; i++) _valueScores[i] >>= 2;
    }
}
