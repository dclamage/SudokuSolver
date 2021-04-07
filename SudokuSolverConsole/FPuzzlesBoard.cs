using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SudokuSolverConsole
{
    static class FPuzzlesUtility
    {
        private static Regex parseCellRegex = new(@"R(\d+)C(\d+)");
        public static (int, int) ParseCell(string cell)
        {
            var match = parseCellRegex.Match(cell);
            if (!match.Success)
            {
                throw new Exception($"Cannot parse cell {cell}");
            }
            return (int.Parse(match.Groups[1].Value) - 1, int.Parse(match.Groups[2].Value) - 1);
        }
    }

    public record FPuzzlesBoard(
        int size,
        string title,
        string author,
        string ruleset,
        FPuzzlesGridEntry[][] grid,
        [property: JsonPropertyName("diagonal+")] bool diagonalp,
        [property: JsonPropertyName("diagonal-")] bool diagonaln,
        bool antiknight,
        bool antiking,
        bool disjointgroups,
        bool nonconsecutive,
        FPuzzlesArrowEntry[] arrow,
        FPuzzlesKillerCageEntry[] killercage,
        FPuzzlesLittleKillerSumEntry[] littlekillersum
    );

    public record FPuzzlesGridEntry(int value, bool given);

    public record FPuzzlesArrowEntry(
        string[][] lines,
        string[] cells
    );

    public record FPuzzlesKillerCageEntry(
        string[] cells,
        string value
    );

    public record FPuzzlesLittleKillerSumEntry(
        string cell,
        string direction,
        string value
    );
}
