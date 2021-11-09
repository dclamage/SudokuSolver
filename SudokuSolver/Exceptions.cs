namespace SudokuSolver;

public class WrongLengthGivensException : Exception
{
    public WrongLengthGivensException()
    {
    }

    public WrongLengthGivensException(string message)
        : base(message)
    {
    }

    public WrongLengthGivensException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
