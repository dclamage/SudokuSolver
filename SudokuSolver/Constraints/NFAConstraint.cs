using System.Numerics;

namespace SudokuSolver.Constraints;

// Runtime representation of a compiled NFA for sequential constraint enforcement.
// Transition entry layout: [targetState: upper 16 bits, valueMask: lower 16 bits]
// The valueMask uses the same bit encoding as the solver's candidate masks (bit k = value k+1).
internal sealed class CompressedNFA
{
    public readonly int NumStates;
    public readonly uint[] AcceptingWords;
    public readonly uint[] StartingWords;
    public readonly uint[][] TransitionLists;

    public CompressedNFA(int numStates, uint[] acceptingWords, uint[] startingWords, uint[][] transitionLists)
    {
        NumStates = numStates;
        AcceptingWords = acceptingWords;
        StartingWords = startingWords;
        TransitionLists = transitionLists;
    }
}

// A constraint that enforces a compiled NFA over a sequence of cells.
// The NFA is serialized by the ISS NFASerializer (base64url bitstream) and deserialized here.
// Each symbol in the NFA corresponds to one candidate value: symbolIndex k → value k+1.
[Constraint(DisplayName = "NFA Constraint", ConsoleName = null)]
public sealed class NFAConstraint : Constraint
{
    private readonly int[] cellIndices;
    private readonly CompressedNFA nfa;
    private readonly bool[] cellMembership;
    private readonly int numWords;
    // Pre-allocated scratch bitsets to avoid per-call heap allocation.
    private readonly uint[][] stateWords;
    private readonly string constraintName;

    public NFAConstraint(Solver solver, int[] cellIndices, string serializedNFA, string name = null)
        : base(solver, serializedNFA)
    {
        this.cellIndices = cellIndices;
        this.constraintName = name ?? "NFA Constraint";
        this.nfa = NFADeserializer.Deserialize(serializedNFA);
        this.numWords = (nfa.NumStates + 31) / 32;

        cellMembership = new bool[NUM_CELLS];
        foreach (int ci in cellIndices)
        {
            if (ci >= 0 && ci < NUM_CELLS)
                cellMembership[ci] = true;
        }

        stateWords = new uint[cellIndices.Length + 1][];
        for (int s = 0; s <= cellIndices.Length; s++)
            stateWords[s] = new uint[numWords];
    }

    public override string SpecificName => constraintName;
    public override bool NeedsEnforceConstraint => true;

    public override bool EnforceConstraint(Solver solver, int i, int j, int val)
    {
        int cellIndex = i * WIDTH + j;
        if (!cellMembership[cellIndex]) return true;
        return RunFilter(solver) != LogicResult.Invalid;
    }

    public override LogicResult StepLogic(Solver solver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        return RunFilter(solver);
    }

    private LogicResult RunFilter(Solver solver)
    {
        IReadOnlyList<uint> board = solver.FlatBoard;
        int numCells = cellIndices.Length;

        // Clear scratch state bitsets
        for (int s = 0; s <= numCells; s++)
            Array.Clear(stateWords[s]);

        // Forward pass: propagate reachable states through each cell
        nfa.StartingWords.AsSpan().CopyTo(stateWords[0]);

        for (int i = 0; i < numCells; i++)
        {
            uint cellMask = board[cellIndices[i]] & ~valueSetMask;
            uint[] current = stateWords[i];
            uint[] next = stateWords[i + 1];

            for (int w = 0; w < numWords; w++)
            {
                uint word = current[w];
                while (word != 0)
                {
                    int bit = BitOperations.TrailingZeroCount(word);
                    word &= word - 1;
                    int stateId = w * 32 + bit;
                    foreach (uint entry in nfa.TransitionLists[stateId])
                    {
                        if ((cellMask & entry) != 0)
                        {
                            int target = (int)(entry >> 16);
                            next[target >> 5] |= 1u << (target & 31);
                        }
                    }
                }
            }

            bool anyReached = false;
            for (int w = 0; w < numWords; w++)
            {
                if (next[w] != 0) { anyReached = true; break; }
            }
            if (!anyReached) return LogicResult.Invalid;
        }

        // Filter final states to only accepting states
        bool hasAccept = false;
        for (int w = 0; w < numWords; w++)
        {
            stateWords[numCells][w] &= nfa.AcceptingWords[w];
            if (stateWords[numCells][w] != 0) hasAccept = true;
        }
        if (!hasAccept) return LogicResult.Invalid;

        // Backward pass: prune unsupported candidates
        bool changed = false;
        for (int i = numCells - 1; i >= 0; i--)
        {
            uint[] current = stateWords[i];
            uint[] next = stateWords[i + 1];
            int cellIndex = cellIndices[i];
            uint cellMask = board[cellIndex] & ~valueSetMask;
            uint supportedValues = 0;

            for (int w = 0; w < numWords; w++)
            {
                uint word = current[w];
                uint keptWord = 0;
                while (word != 0)
                {
                    int bit = BitOperations.TrailingZeroCount(word);
                    word &= word - 1;
                    int stateId = w * 32 + bit;
                    uint stateSup = 0;
                    foreach (uint entry in nfa.TransitionLists[stateId])
                    {
                        uint hit = cellMask & entry;
                        if (hit != 0)
                        {
                            int target = (int)(entry >> 16);
                            if ((next[target >> 5] & (1u << (target & 31))) != 0)
                                stateSup |= hit;
                        }
                    }
                    if (stateSup != 0)
                    {
                        keptWord |= 1u << bit;
                        supportedValues |= stateSup;
                    }
                }
                current[w] = keptWord;
            }

            if (supportedValues == 0) return LogicResult.Invalid;

            if (supportedValues != cellMask)
            {
                uint toRemove = cellMask & ~supportedValues;
                while (toRemove != 0)
                {
                    int val = MinValue(toRemove);
                    toRemove &= ~ValueMask(val);
                    if (!solver.ClearValue(cellIndex, val))
                        return LogicResult.Invalid;
                }
                changed = true;
            }
        }

        return changed ? LogicResult.Changed : LogicResult.None;
    }

    // Deserializes an ISS NFASerializer base64 bitstream directly into a CompressedNFA.
    internal static class NFADeserializer
    {
        private const int FORMAT_PLAIN = 0;
        private const int FORMAT_PACKED = 1;

        public static CompressedNFA Deserialize(string serialized)
        {
            if (string.IsNullOrEmpty(serialized))
            {
                // Empty NFA — rejects everything: 0 states, no accepting/starting states.
                return new CompressedNFA(0, [], [], []);
            }

            // Decode url-safe base64 to bytes
            string b64 = serialized.Replace('-', '+').Replace('_', '/');
            int pad = (4 - b64.Length % 4) % 4;
            if (pad > 0) b64 += new string('=', pad);
            byte[] bytes = Convert.FromBase64String(b64);

            var reader = new BitStreamReader(bytes);

            // Header
            int format = reader.ReadBits(2);
            int stateBits = reader.ReadBits(4) + 1;
            int symbolCount = reader.ReadBits(4) + 1;
            int startCount = reader.ReadBits(stateBits);
            int acceptCount = reader.ReadBits(stateBits);
            int startIsAccept = reader.ReadBits(startCount);
            int transitionCountBits = (format == FORMAT_PLAIN) ? reader.ReadBits(4) : 0;
            int symbolBits = RequiredBits(symbolCount - 1);

            // Collect raw (stateId, symbolIndex, target) transitions
            var rawTransitions = new List<(int state, int symbol, int target)>();
            int maxState = startCount + acceptCount - 1;

            if (format == FORMAT_PLAIN)
            {
                if (transitionCountBits > 0)
                {
                    int stateId = 0;
                    while (reader.RemainingBits >= transitionCountBits)
                    {
                        int count = reader.ReadBits(transitionCountBits);
                        for (int t = 0; t < count; t++)
                        {
                            if (reader.RemainingBits < symbolBits + stateBits)
                                throw new InvalidDataException("Truncated NFA plain body");
                            int sym = reader.ReadBits(symbolBits);
                            int tgt = reader.ReadBits(stateBits);
                            rawTransitions.Add((stateId, sym, tgt));
                            maxState = Math.Max(maxState, Math.Max(stateId, tgt));
                        }
                        stateId++;
                    }
                    maxState = Math.Max(maxState, stateId - 1);
                }
            }
            else // PACKED
            {
                int stateId = 0;
                while (reader.RemainingBits >= symbolCount)
                {
                    int activeMask = reader.ReadBits(symbolCount);
                    for (int s = 0; s < symbolCount; s++)
                    {
                        if ((activeMask & (1 << s)) != 0)
                        {
                            if (reader.RemainingBits < stateBits)
                                throw new InvalidDataException("Truncated NFA packed body");
                            int tgt = reader.ReadBits(stateBits);
                            rawTransitions.Add((stateId, s, tgt));
                            maxState = Math.Max(maxState, Math.Max(stateId, tgt));
                        }
                    }
                    stateId++;
                    maxState = Math.Max(maxState, stateId - 1);
                }
            }

            int numStates = maxState + 1;
            int numWords = (numStates + 31) / 32;

            // Build accepting and starting bitsets
            uint[] acceptingWords = new uint[numWords];
            uint[] startingWords = new uint[numWords];

            for (int i = 0; i < startCount; i++)
            {
                startingWords[i >> 5] |= 1u << (i & 31);
                if ((startIsAccept & (1 << i)) != 0)
                    acceptingWords[i >> 5] |= 1u << (i & 31);
            }
            for (int i = startCount; i < startCount + acceptCount; i++)
            {
                acceptingWords[i >> 5] |= 1u << (i & 31);
            }

            // Group transitions by state, merging symbol masks by target state
            var perState = new Dictionary<int, uint>[numStates];
            for (int s = 0; s < numStates; s++)
                perState[s] = [];

            foreach (var (state, sym, target) in rawTransitions)
            {
                var dict = perState[state];
                dict.TryGetValue(target, out uint existing);
                dict[target] = existing | (1u << sym);
            }

            // Build final flat transition arrays
            uint[][] transitionLists = new uint[numStates][];
            for (int s = 0; s < numStates; s++)
            {
                var dict = perState[s];
                transitionLists[s] = new uint[dict.Count];
                int idx = 0;
                foreach (var (target, mask) in dict)
                {
                    // Entry: [target: upper 16 bits, valueMask: lower 16 bits]
                    transitionLists[s][idx++] = ((uint)target << 16) | (mask & 0xFFFF);
                }
            }

            return new CompressedNFA(numStates, acceptingWords, startingWords, transitionLists);
        }

        private static int RequiredBits(int maxValue)
        {
            if (maxValue <= 0) return 1;
            return 32 - BitOperations.LeadingZeroCount((uint)maxValue);
        }

        // MSB-first bit reader over a byte array (matches ISS BitReader).
        private sealed class BitStreamReader
        {
            private readonly byte[] _bytes;
            private int _bitOffset;

            public BitStreamReader(byte[] bytes)
            {
                _bytes = bytes;
                _bitOffset = 0;
            }

            public int RemainingBits => _bytes.Length * 8 - _bitOffset;

            public int ReadBits(int count)
            {
                if (count == 0) return 0;
                if (_bitOffset + count > _bytes.Length * 8)
                    throw new InvalidDataException("Unexpected end of NFA bit stream");
                int value = 0;
                for (int i = 0; i < count; i++)
                {
                    int byteIndex = _bitOffset >> 3;
                    int bitIndex = 7 - (_bitOffset & 7); // MSB first
                    value = (value << 1) | ((_bytes[byteIndex] >> bitIndex) & 1);
                    _bitOffset++;
                }
                return value;
            }
        }
    }
}
