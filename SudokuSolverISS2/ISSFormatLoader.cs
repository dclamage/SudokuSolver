namespace SudokuSolverISS;

/// <summary>
/// Parses the ISS URL constraint string format: .Type~arg1~arg2~...
/// Supports Arrow and Given constraint types.
/// </summary>
static class ISSFormatLoader
{
    /// <summary>
    /// Parses an ISS constraint string into a Puzzle.
    /// The format is a sequence of dot-prefixed, tilde-delimited constraints:
    ///   .Arrow~R1C2~R2C1~R3C1  → circle at R1C2, arrow cells R2C1 R3C1
    ///   .Given~R1C1~5          → given digit 5 at R1C1
    /// </summary>
    public static FPuzzlesLoader.Puzzle Load(string issConstraints)
    {
        uint[] grid = new uint[G.NUM_CELLS];
        for (int i = 0; i < G.NUM_CELLS; i++) grid[i] = G.ALL_VALUES;
        var arrows = new List<(int circle, int[] arrow)>();

        // Split on '.' — first token will be empty (leading dot), skip it.
        var tokens = issConstraints.Split('.');
        foreach (var token in tokens)
        {
            if (string.IsNullOrEmpty(token)) continue;
            var parts = token.Split('~');
            if (parts.Length == 0) continue;

            string type = parts[0];

            if (type == "Arrow" && parts.Length >= 3)
            {
                int circle = ParseCell(parts[1]);
                int[] arrowPath = new int[parts.Length - 2];
                for (int j = 2; j < parts.Length; j++)
                    arrowPath[j - 2] = ParseCell(parts[j]);
                arrows.Add((circle, arrowPath));
            }
            else if ((type == "Given" || type == "") && parts.Length >= 3)
            {
                int cell = ParseCell(parts[1]);
                if (int.TryParse(parts[2], out int val) && val >= 1 && val <= G.SIZE)
                    grid[cell] = G.ValueBit(val);
            }
            // Other constraint types (Size, LittleKiller, etc.) are silently ignored.
        }

        return new FPuzzlesLoader.Puzzle(grid, arrows);
    }

    private static int ParseCell(string s) => FPuzzlesLoader.ParseCell(s, G.SIZE);
}
