namespace SudokuSolver.Constraints;

[Constraint(DisplayName = "Entropic Line", ConsoleName = "entrol")]
public class EntropicLineConstraint : Constraint
{
    public readonly List<(int, int)> cells;
    public readonly int difference;
    private readonly HashSet<(int, int)> cellsSet;

    private readonly uint[] groupMasks = { 0b000000111, 0b000111000, 0b111000000 };

    private static int[] lowGroup = {1, 2, 3};

    private static int[] midGroup = {4, 5, 6};
    
    private static int[] highGroup = {7, 8, 9};
    
    private static int[][] groups = new int[][] { lowGroup, midGroup, highGroup };

    private int[] groupIndices = {-1, -1, -1};

    public EntropicLineConstraint(Solver sudokuSolver, string options) : base(sudokuSolver)
    {
        var cellGroups = ParseCells(options);
        if (cellGroups.Count != 1)
        {
            throw new ArgumentException($"Entropic Line constraint expects 1 cell group, got {cellGroups.Count}.");
        }

        cells = cellGroups[0];
        cellsSet = new(cells);
    }

    public override string SpecificName => $"Entropic Line {CellName(cells[0])} - {CellName(cells[^1])}";

    private int getCellGroup(uint cellMask) {
        int curGroup = -1;

        if (IsValueSet(cellMask)) {
            int curValue = GetValue(cellMask);
            for (int group = 0; group < groups.Count(); group++) {
                if (groups[group].Contains(curValue)) {
                    curGroup = group;
                    break;
                }
            }
        }
        else {
            for (int group = 0; group < groupMasks.Count(); group++) {
                if (((cellMask & groupMasks[group]) != 0) && ((cellMask & ~groupMasks[group]) == 0)) {
                    curGroup = group;
                    break;
                }
            }
        }

        return curGroup;
    }

    private void findGroupIndices(uint[, ] board) {
        for (int i = 0; i < cells.Count; i++) {
            var currMask = board[cells[i].Item1, cells[i].Item2];
            for (int j = 0; j < groupMasks.Count(); j++) {
                if ((IsValueSet(currMask) && groups[j].Contains(GetValue(currMask)) ) || (((currMask & groupMasks[j]) != 0) && ((currMask & ~groupMasks[j]) == 0))) {
                    groupIndices[j] = i % 3;
                }
            }
        }

        int numNotSet = 0;

        int lastNotSetIndex = -1;

        for (int i = 0; i < groupIndices.Count(); i++) {
            if (groupIndices[i] == -1) {
                numNotSet++;
                lastNotSetIndex = i;
            }
        }

        if (numNotSet == 1) {
            for (int i = 0; i < 3; i++) {
                if (!groupIndices.Contains(i)) {
                    groupIndices[lastNotSetIndex] = i;
                }
            }
        }

    }

    public override LogicResult InitCandidates(Solver sudokuSolver)
    {
        // Without a value in any spot entropic lines cannot rule out any value

        return LogicResult.None;
    }

    public override bool EnforceConstraint(Solver sudokuSolver, int i, int j, int val)
    {
        if (cells.Count <= 1)
        {
            return true;
        }

        if (cellsSet.Contains((i, j)))
        {
            var board = sudokuSolver.Board;

            for (int ti = 0; ti < cells.Count - 2; ti++) {
                var curCell = cells[ti];
                var nextCell = cells[ti + 1];
                var nextNextCell = cells[ti + 2];
                uint curMask = board[curCell.Item1, curCell.Item2];
                uint nextMask = board[nextCell.Item1, nextCell.Item2];
                uint nextNextMask = board[nextNextCell.Item1, nextNextCell.Item2];
                bool curValueSet = IsValueSet(curMask);
                bool nextValueSet = IsValueSet(nextMask);
                bool nextNextValueSet = IsValueSet(nextNextMask);

                int curGroup = getCellGroup(curMask);

                if (curGroup != -1) {

                    if (!nextValueSet) {
                        foreach (var clearVal in groups[curGroup]) {
                            if (!sudokuSolver.ClearValue(nextCell.Item1, nextCell.Item2, clearVal))
                            {
                                return false;
                            }
                        }
                    }
                    else if (groups[curGroup].Contains(GetValue(nextMask))) {
                        return false;
                    }

                    if (!nextNextValueSet) {
                        foreach (var clearVal in groups[curGroup]) {
                            if (!sudokuSolver.ClearValue(nextNextCell.Item1, nextNextCell.Item2, clearVal))
                            {
                                return false;
                            }
                        }
                    }
                    else if (groups[curGroup].Contains(GetValue(nextNextMask))) {
                        return false;
                    }

                }

                int nextGroup = getCellGroup(nextMask);

                if (nextGroup != -1) {

                    if (!nextNextValueSet) {
                        foreach (var clearVal in groups[nextGroup]) {
                            if (!sudokuSolver.ClearValue(nextNextCell.Item1, nextNextCell.Item2, clearVal))
                            {
                                return false;
                            }
                        }
                    }
                    else if (groups[nextGroup].Contains(GetValue(nextNextMask))) {
                        return false;
                    }
                }

            }
        }

        return true;
    }

    public override LogicResult InitLinks(Solver solver, List<LogicalStepDesc> logicalStepDescription) => InitLinksByRunningLogic(solver, cells, logicalStepDescription);
    public override List<(int, int)> CellsMustContain(Solver sudokuSolver, int value) => CellsMustContainByRunningLogic(sudokuSolver, cells, value);

    public override LogicResult StepLogic(Solver sudokuSolver, StringBuilder logicalStepDescription, bool isBruteForcing)
    {
        if (cells.Count <= 1) {
            return LogicResult.None;
        }

        var board = sudokuSolver.Board;
        List<int> elims = null;
        bool hadChange = false;
        bool changed;

        do
        {
            changed = false;
            

            for (int ti = 0; ti < cells.Count - 2; ti++) {
                var curCell = cells[ti];
                var nextCell = cells[ti + 1];
                var nextNextCell = cells[ti + 2];
                uint curMask = board[curCell.Item1, curCell.Item2];
                uint nextMask = board[nextCell.Item1, nextCell.Item2];
                uint nextNextMask = board[nextNextCell.Item1, nextNextCell.Item2];
                bool curValueSet = IsValueSet(curMask);
                bool nextValueSet = IsValueSet(nextMask);
                bool nextNextValueSet = IsValueSet(nextNextMask);

                int curGroup = getCellGroup(curMask);
                int nextGroup = getCellGroup(nextMask);
                int nextNextGroup = getCellGroup(nextNextMask);

                if (curGroup != -1) {

                    if (!nextValueSet) {
                        LogicResult clearResult = sudokuSolver.ClearMask(nextCell.Item1, nextCell.Item2, groupMasks[curGroup]);
                        if (clearResult == LogicResult.Invalid)
                        {
                            logicalStepDescription?.Append($"{CellName(nextCell)} has no more valid candidates.");
                            return LogicResult.Invalid;
                        }
                        if (clearResult == LogicResult.Changed) {

                            if (logicalStepDescription != null) {
                            
                                elims ??= new();

                                foreach (var clearVal in groups[curGroup])
                                {
                                    elims.Add(CandidateIndex(nextCell, clearVal));
                                }

                                changed = true;
                                hadChange = true;
                            
                            }
                        
                        }
                    }
                    if (curGroup == nextGroup) {
                        logicalStepDescription?.Append($"{CellName(nextCell)} is in an invalid group.");
                        return LogicResult.Invalid;
                    }

                    if (!nextNextValueSet) {
                        LogicResult clearResult = sudokuSolver.ClearMask(nextNextCell.Item1, nextNextCell.Item2, groupMasks[curGroup]);
                        if (clearResult == LogicResult.Invalid)
                        {
                            logicalStepDescription?.Append($"{CellName(nextNextCell)} has no more valid candidates.");
                            return LogicResult.Invalid;
                        }
                        if (clearResult == LogicResult.Changed) {
                            if (logicalStepDescription != null) {
                            
                                elims ??= new();
                                                                
                                foreach (var clearVal in groups[curGroup])
                                {
                                    elims.Add(CandidateIndex(nextNextCell, clearVal));
                                }

                                changed = true;
                                hadChange = true;
                            
                            }
                        }
                    }
                    if (curGroup == nextNextGroup) {
                        logicalStepDescription?.Append($"{CellName(nextNextCell)} is in an invalid group.");
                        return LogicResult.Invalid;
                    }

                }

                if (nextGroup != -1) {

                    if (!nextNextValueSet) {
                        LogicResult clearResult = sudokuSolver.ClearMask(nextNextCell.Item1, nextNextCell.Item2, groupMasks[nextGroup]);
                        if (clearResult == LogicResult.Invalid)
                        {
                            logicalStepDescription?.Append($"{CellName(nextNextCell)} has no more valid candidates.");
                            return LogicResult.Invalid;
                        }
                        if (clearResult == LogicResult.Changed) {
                            if (logicalStepDescription != null) {
                            
                                elims ??= new();

                                foreach (var clearVal in groups[nextGroup])
                                {
                                    elims.Add(CandidateIndex(nextNextCell, clearVal));
                                }

                                changed = true;
                                hadChange = true;
                            
                            }
                        }
                    }
                    if (nextGroup == nextNextGroup) {
                        logicalStepDescription?.Append($"{CellName(nextNextCell)} is in an invalid group.");
                        return LogicResult.Invalid;
                    }

                }

            }

        } while (changed);


        if (logicalStepDescription != null && elims != null && elims.Count > 0)
        {
            logicalStepDescription.Append($"Re-evaluated => {sudokuSolver.DescribeElims(elims)}");
        }

        return hadChange ? LogicResult.Changed : LogicResult.None;
    }

}