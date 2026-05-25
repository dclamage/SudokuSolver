namespace SudokuSolverISS;

using System.Text.Json.Nodes;

/// <summary>
/// Minimal f-puzzles JSON parser — extracts givens and arrow constraints.
/// Accepts the base64 string after "?load=" in an f-puzzles URL.
/// </summary>
static class FPuzzlesLoader
{
    public record Puzzle(uint[] Grid, List<(int circle, int[] arrow)> Arrows);

    public static Puzzle Load(string fpuzzlesBase64)
    {
        // f-puzzles uses lz-string (UTF-16) compression.  If the input already
        // looks like raw JSON (starts with '{'), skip decompression.
        string json;
        string trimmed = fpuzzlesBase64.TrimStart().TrimStart('=');
        if (trimmed.StartsWith('{'))
            json = trimmed;
        else
            json = LzString.Decompress(trimmed);

        var root  = JsonNode.Parse(json)!;
        int size  = root["size"]?.GetValue<int>() ?? G.SIZE;
        if (size != G.SIZE)
            throw new NotSupportedException($"Only {G.SIZE}×{G.SIZE} puzzles are supported.");

        // Build initial grid.
        // f-puzzles grid is a 9×9 2D array: grid[row][col].
        uint[] grid = new uint[G.NUM_CELLS];
        for (int i = 0; i < G.NUM_CELLS; i++) grid[i] = G.ALL_VALUES;

        var gridRows = root["grid"]?.AsArray();
        if (gridRows != null)
        {
            for (int r = 0; r < size && r < gridRows.Count; r++)
            {
                var row = gridRows[r]?.AsArray();
                if (row == null) continue;
                for (int c = 0; c < size && c < row.Count; c++)
                {
                    var cell = row[c];
                    int? val = cell?["value"]?.GetValue<int>();
                    bool given = cell?["given"]?.GetValue<bool>() ?? false;
                    if (given && val.HasValue && val.Value >= 1 && val.Value <= size)
                        grid[r * size + c] = G.ValueBit(val.Value);
                }
            }
        }

        // Parse arrow constraints.
        // f-puzzles arrow format: {"cells": ["R1C1"], "lines": [["R1C1","R1C2","R1C3"]]}
        // cells[0] is the circle; lines[0][1..] is the arrow path.
        var arrows = new List<(int, int[])>();
        var arrowArr = root["arrow"]?.AsArray();
        if (arrowArr != null)
        {
            foreach (var entry in arrowArr)
            {
                var circleCells = entry?["cells"]?.AsArray();
                var linesArr    = entry?["lines"]?.AsArray();
                if (circleCells == null || linesArr == null) continue;
                if (circleCells.Count == 0) continue;

                int circle = ParseCell(circleCells[0]!.GetValue<string>(), size);
                foreach (var lineNode in linesArr)
                {
                    var line = lineNode?.AsArray();
                    if (line == null || line.Count < 2) continue;
                    int[] arrowPath = new int[line.Count - 1];
                    for (int j = 1; j < line.Count; j++)
                        arrowPath[j - 1] = ParseCell(line[j]!.GetValue<string>(), size);
                    arrows.Add((circle, arrowPath));
                }
            }
        }

        return new Puzzle(grid, arrows);
    }

    // Parses "R1C1" (1-indexed) → 0-indexed cell index.
    internal static int ParseCell(string s, int size)
    {
        int r = -1, c = -1;
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] == 'R' || s[i] == 'r')
            {
                i++;
                int start = i;
                while (i < s.Length && char.IsDigit(s[i])) i++;
                r = int.Parse(s.AsSpan(start, i - start)) - 1;
            }
            else if (s[i] == 'C' || s[i] == 'c')
            {
                i++;
                int start = i;
                while (i < s.Length && char.IsDigit(s[i])) i++;
                c = int.Parse(s.AsSpan(start, i - start)) - 1;
            }
            else i++;
        }
        if (r < 0 || c < 0) throw new FormatException($"Cannot parse cell '{s}'");
        return r * size + c;
    }
}
