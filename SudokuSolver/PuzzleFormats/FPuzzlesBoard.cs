using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SudokuSolver.PuzzleFormats
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
        string[] negative,
        FPuzzlesArrowEntry[] arrow,
        FPuzzlesKillerCageEntry[] killercage,
        FPuzzlesLittleKillerSumEntry[] littlekillersum,
        FPuzzlesCell[] odd,
        FPuzzlesCell[] even,
        FPuzzlesCell[] minimum,
        FPuzzlesCell[] maximum,
        FPuzzlesCells[] extraregion,
        FPuzzlesLines[] thermometer,
        FPuzzlesLines[] palindrome,
        FPuzzlesCells[] difference,
        FPuzzlesCells[] xv,
        FPuzzlesCells[] ratio,
        FPuzzlesClone[] clone,
        FPuzzlesQuadruple[] quadruple
    );

    public record FPuzzlesGridEntry(int value, bool given, int[] centerPencilMarks);

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

    public record FPuzzlesCell(string cell);

    public record FPuzzlesCells(string[] cells, string value = "");

    public record FPuzzlesLines(string[][] lines);

    public record FPuzzlesClone(string[] cells, string[] cloneCells);

    public record FPuzzlesQuadruple(string[] cells, int[] values);
}
