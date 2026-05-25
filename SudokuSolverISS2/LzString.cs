namespace SudokuSolverISS;

using LZStringCSharp;

// Thin wrapper so FPuzzlesLoader doesn't need a using directive.
static class LzString
{
    public static string Decompress(string input) =>
        string.IsNullOrEmpty(input) ? "" : LZString.DecompressFromBase64(input) ?? "";
}
