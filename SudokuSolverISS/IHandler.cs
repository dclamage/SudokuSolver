namespace SudokuSolverISS;

interface IHandler
{
    // Returns false on contradiction.
    bool EnforceConsistency(uint[] grid, HandlerAccumulator acc);
}
