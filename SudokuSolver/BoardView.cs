namespace SudokuSolver;

public readonly struct BoardView(uint[] flat, int width, int height)
{
    private readonly uint[] _flat = flat;
    private readonly int _width = width, _height = height;

    public uint this[int cellIndex] => _flat[cellIndex];

    public uint this[int row, int col]
    {
        get
        {
            if ((uint)row >= (uint)_height || (uint)col >= (uint)_width)
                throw new IndexOutOfRangeException();
            return _flat[row * _width + col];
        }
    }

    public int GetLength(int dimension)
        => dimension == 0 ? _height
         : dimension == 1 ? _width
         : throw new ArgumentOutOfRangeException(nameof(dimension));

    public int Rank => 2;
}
