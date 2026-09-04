#nullable enable

using System.Security.Cryptography;
using System.Text;

namespace SudokuSolver.Logical;

/// <summary>Owns an isolated solver position and its stable native identifier mappings.</summary>
public sealed class LogicalPosition
{
    private readonly string[] _cellIds;
    private readonly IReadOnlyList<string> _readOnlyCellIds;
    private readonly Dictionary<string, int> _cellIndexById;
    private readonly string[] _valueIdsBySolverValue;
    private readonly IReadOnlyList<string> _readOnlyValueIdsBySolverValue;
    private readonly Dictionary<string, int> _solverValueByValueId;

    /// <summary>Initializes a logical position over an already finalized isolated solver.</summary>
    /// <param name="solver">The isolated projected solver.</param>
    /// <param name="cellIds">Stable cell identifiers in solver flat-board order.</param>
    /// <param name="valueIdsBySolverValue">Stable value identifiers in one-based solver-value order.</param>
    /// <param name="semanticHash">The authoritative semantic source hash.</param>
    public LogicalPosition(
        Solver solver,
        IEnumerable<string> cellIds,
        IEnumerable<string> valueIdsBySolverValue,
        string semanticHash)
    {
        Solver = solver ?? throw new ArgumentNullException(nameof(solver));
        _cellIds = ValidateMapping(cellIds, solver.NUM_CELLS, nameof(cellIds));
        _valueIdsBySolverValue = ValidateMapping(
            valueIdsBySolverValue,
            solver.MAX_VALUE,
            nameof(valueIdsBySolverValue));
        _readOnlyCellIds = Array.AsReadOnly(_cellIds);
        _readOnlyValueIdsBySolverValue = Array.AsReadOnly(_valueIdsBySolverValue);
        SemanticHash = string.IsNullOrWhiteSpace(semanticHash)
            ? throw new ArgumentException("A semantic hash is required.", nameof(semanticHash))
            : semanticHash;
        _cellIndexById = _cellIds
            .Select((id, index) => (id, index))
            .ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);
        _solverValueByValueId = _valueIdsBySolverValue
            .Select((id, index) => (id, value: index + 1))
            .ToDictionary(pair => pair.id, pair => pair.value, StringComparer.Ordinal);
    }

    /// <summary>Gets the solver exclusively owned by this position.</summary>
    public Solver Solver { get; }

    /// <summary>Gets the authoritative semantic identity of the source puzzle.</summary>
    public string SemanticHash { get; }

    /// <summary>Gets the deterministic precondition hash for the current value/candidate board.</summary>
    public string PositionHash => ComputePositionHash();

    /// <summary>Gets stable cell identifiers in current solver order.</summary>
    public IReadOnlyList<string> CellIds => _readOnlyCellIds;

    /// <summary>Gets stable value identifiers in one-based solver-value order.</summary>
    public IReadOnlyList<string> ValueIdsBySolverValue => _readOnlyValueIdsBySolverValue;

    /// <summary>Returns the stable cell identifier for one flat solver index.</summary>
    /// <param name="cellIndex">The flat solver cell index.</param>
    /// <returns>The stable cell identifier.</returns>
    public string GetCellId(int cellIndex) => _cellIds[cellIndex];

    /// <summary>Returns the flat solver index for one stable cell identifier.</summary>
    /// <param name="cellId">The stable cell identifier.</param>
    /// <returns>The flat solver cell index.</returns>
    public int GetCellIndex(string cellId)
        => _cellIndexById.TryGetValue(cellId, out int index)
            ? index
            : throw new ArgumentException($"Unknown logical cell ID {cellId}.", nameof(cellId));

    /// <summary>Returns the stable value identifier for one one-based solver value.</summary>
    /// <param name="solverValue">The one-based solver value.</param>
    /// <returns>The stable value identifier.</returns>
    public string GetValueId(int solverValue)
        => solverValue >= 1 && solverValue <= _valueIdsBySolverValue.Length
            ? _valueIdsBySolverValue[solverValue - 1]
            : throw new ArgumentOutOfRangeException(nameof(solverValue));

    /// <summary>Returns the one-based solver value for one stable value identifier.</summary>
    /// <param name="valueId">The stable value identifier.</param>
    /// <returns>The one-based solver value.</returns>
    public int GetSolverValue(string valueId)
        => _solverValueByValueId.TryGetValue(valueId, out int value)
            ? value
            : throw new ArgumentException($"Unknown logical value ID {valueId}.", nameof(valueId));

    /// <summary>Converts a solver candidate mask into stable value identifiers.</summary>
    /// <param name="mask">The solver mask.</param>
    /// <returns>Stable candidate identifiers in solver-value order.</returns>
    public IReadOnlyList<string> GetCandidateValueIds(uint mask)
    {
        List<string> values = [];
        for (int value = 1; value <= _valueIdsBySolverValue.Length; value++)
        {
            if ((mask & SolverUtility.ValueMask(value)) != 0)
            {
                values.Add(GetValueId(value));
            }
        }
        return values;
    }

    /// <summary>Creates an independent logical position with identical mappings and board state.</summary>
    /// <returns>The isolated clone.</returns>
    public LogicalPosition Clone()
        => new(Solver.Clone(willRunNonSinglesLogic: false), _cellIds, _valueIdsBySolverValue, SemanticHash);

    /// <summary>Returns the current board expressed entirely with stable identifiers.</summary>
    /// <returns>Cells in stable projected solver order.</returns>
    public IReadOnlyList<LogicalCellState> GetCells()
    {
        LogicalCellState[] cells = new LogicalCellState[_cellIds.Length];
        for (int index = 0; index < cells.Length; index++)
        {
            uint mask = Solver.FlatBoard[index];
            bool isSet = SolverUtility.IsValueSet(mask);
            cells[index] = new LogicalCellState
            {
                CellId = _cellIds[index],
                ValueId = isSet ? GetValueId(SolverUtility.GetValue(mask)) : null,
                CandidateValueIds = isSet ? [] : GetCandidateValueIds(mask),
            };
        }
        return cells;
    }

    internal static string ComputeStableHash(Action<BinaryWriter> writeCanonicalContent)
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, new UTF8Encoding(false), leaveOpen: true))
        {
            writeCanonicalContent(writer);
        }
        return $"sha256:{Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant()}";
    }

    private string ComputePositionHash()
        => ComputeStableHash(writer =>
        {
            writer.Write("logical-position-v1");
            writer.Write(SemanticHash);
            writer.Write(_cellIds.Length);
            for (int index = 0; index < _cellIds.Length; index++)
            {
                writer.Write(_cellIds[index]);
                writer.Write(Solver.FlatBoard[index]);
            }
            writer.Write(_valueIdsBySolverValue.Length);
            foreach (string valueId in _valueIdsBySolverValue)
            {
                writer.Write(valueId);
            }
        });

    private static string[] ValidateMapping(IEnumerable<string> values, int expectedCount, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);
        string[] result = values.ToArray();
        if (result.Length != expectedCount)
        {
            throw new ArgumentException(
                $"Logical mapping has {result.Length} entries; expected {expectedCount}.",
                parameterName);
        }
        if (result.Any(string.IsNullOrWhiteSpace)
            || result.Distinct(StringComparer.Ordinal).Count() != result.Length)
        {
            throw new ArgumentException("Logical mapping identifiers must be nonempty and unique.", parameterName);
        }
        return result;
    }
}
