using System.Diagnostics;
using SudokuSolverISS;
using SudokuSolverISS.Handlers;

// ---- Argument parsing ----
// -f <fpuzzles_base64_or_json>   f-puzzles compressed URL param or raw JSON
// -j <json_file>                  path to f-puzzles JSON file
// -i <iss_constraint_string>      ISS URL constraint format (.Arrow~...  .Given~...)
// --count                         count all solutions (default: find first)
string input = "";
string format = "fpuzzles";
bool countAll = false;
for (int i = 0; i < args.Length; i++)
{
    if ((args[i] == "-f" || args[i] == "-j") && i + 1 < args.Length) { input = args[++i]; format = "fpuzzles"; }
    else if (args[i] == "-i" && i + 1 < args.Length)                 { input = args[++i]; format = "iss"; }
    else if (args[i] == "--count")                                     countAll = true;
}
if (string.IsNullOrEmpty(input))
{
    Console.Error.WriteLine("Usage: SudokuSolverISS [-f <fpuzzles>  |  -j <json>  |  -i <iss_constraints>] [--count]");
    return 1;
}

// If it's a file path, read it.
if (File.Exists(input))
    input = File.ReadAllText(input);

// Auto-detect ISS format if content starts with '.'.
if (format == "fpuzzles" && input.TrimStart().StartsWith('.'))
    format = "iss";


// ---- Setup ----
var setupSw = Stopwatch.StartNew();

FPuzzlesLoader.Puzzle puzzle;
try
{
    puzzle = format == "iss"
        ? ISSFormatLoader.Load(input)
        : FPuzzlesLoader.Load(input);
}
catch (Exception ex) { Console.Error.WriteLine($"Parse error: {ex.Message}"); return 1; }

int[][] peers = BuildPeers();

var handlers   = new List<IHandler>(108 + puzzle.Arrows.Count + 54);
var singleton  = new int[G.NUM_CELLS][];
var auxLists   = new List<int>[G.NUM_CELLS];  // fired only on AddForFixedCell (mirrors ISS aux)
var ordLists   = new List<int>[G.NUM_CELLS];
var houseCells = new List<int[]>(G.SIZE * 3);
for (int i = 0; i < G.NUM_CELLS; i++) { auxLists[i] = []; ordLists[i] = []; }

var rows  = new int[G.SIZE][];
var cols  = new int[G.SIZE][];
var boxes = new int[G.SIZE][];

for (int cell = 0; cell < G.NUM_CELLS; cell++)
{
    singleton[cell] = [handlers.Count];
    handlers.Add(new SingletonHandler(cell, peers[cell]));
}
for (int r = 0; r < G.SIZE; r++)
{
    int idx = handlers.Count;
    int[] row = Enumerable.Range(0, G.SIZE).Select(c => G.CellIndex(r, c)).ToArray();
    rows[r] = row;
    handlers.Add(new AllDifferentHandler(row));
    foreach (int cell in row) ordLists[cell].Add(idx);
    houseCells.Add(row);
}
for (int c = 0; c < G.SIZE; c++)
{
    int idx = handlers.Count;
    int[] col = Enumerable.Range(0, G.SIZE).Select(r => G.CellIndex(r, c)).ToArray();
    cols[c] = col;
    handlers.Add(new AllDifferentHandler(col));
    foreach (int cell in col) ordLists[cell].Add(idx);
    houseCells.Add(col);
}
for (int b = 0; b < G.SIZE; b++)
{
    int idx = handlers.Count;
    int br = (b / 3) * 3, bc = (b % 3) * 3;
    int[] box = new int[G.SIZE];
    for (int k = 0; k < G.SIZE; k++)
        box[k] = G.CellIndex(br + k / 3, bc + k % 3);
    boxes[b] = box;
    handlers.Add(new AllDifferentHandler(box));
    foreach (int cell in box) ordLists[cell].Add(idx);
    houseCells.Add(box);
}
// Locked candidates (mirrors ISS _addGridHouseIntersections).
// Added as AUX handlers (like ISS's handlerSet.addAux): fired only when a cell
// becomes fixed, not on every candidate narrowing.
for (int r = 0; r < G.SIZE; r++)
{
    for (int s = 0; s < 3; s++)   // 3 box col-bands per row
    {
        int b = (r / 3) * 3 + s;
        var boxSet  = new HashSet<int>(boxes[b]);
        int[] rowPart = rows[r].Where(c => !boxSet.Contains(c)).ToArray();
        int[] boxPart = boxes[b].Where(c => G.Row(c) != r).ToArray();
        int idx = handlers.Count;
        handlers.Add(new LockedCandidatesHandler(rowPart, boxPart));
        foreach (int c in rowPart) auxLists[c].Add(idx);
        foreach (int c in boxPart) auxLists[c].Add(idx);
    }
}
for (int c = 0; c < G.SIZE; c++)
{
    for (int t = 0; t < 3; t++)   // 3 box row-bands per col
    {
        int b = t * 3 + (c / 3);
        var boxSet  = new HashSet<int>(boxes[b]);
        int[] colPart = cols[c].Where(cell => !boxSet.Contains(cell)).ToArray();
        int[] boxPart = boxes[b].Where(cell => G.Col(cell) != c).ToArray();
        int idx = handlers.Count;
        handlers.Add(new LockedCandidatesHandler(colPart, boxPart));
        foreach (int cell in colPart) auxLists[cell].Add(idx);
        foreach (int cell in boxPart) auxLists[cell].Add(idx);
    }
}
foreach (var (circle, arrow) in puzzle.Arrows)
{
    int idx = handlers.Count;
    handlers.Add(new SumHandler(circle, arrow));
    ordLists[circle].Add(idx);
    foreach (int c in arrow) ordLists[c].Add(idx);
}

int[][] aux      = auxLists.Select(l => l.ToArray()).ToArray();
int[][] ordinary = ordLists.Select(l => l.ToArray()).ToArray();
var acc = new HandlerAccumulator(handlers.ToArray(), singleton, aux, ordinary);
var cs  = new ConflictScores();

// Seed structural cell priorities (mirrors ISS _initCellPriorities).
// Houses: priority = cells.length = 9.
// Sum/arrow handlers: priority = max(numValues*2 - cells.length, numValues).
{
    // Each cell is in exactly 3 houses of 9 cells each.
    for (int i = 0; i < G.NUM_CELLS; i++) cs.Scores[i] += G.SIZE * 3;  // row + col + box
    // Arrow/sum constraints: priority = max(9*2 - n, 9) where n = circle + arrow cells.
    foreach (var (circle, arrow) in puzzle.Arrows)
    {
        int n      = 1 + arrow.Length;
        int prio   = Math.Max(G.SIZE * 2 - n, G.SIZE);
        cs.Scores[circle] += prio;
        foreach (int ac in arrow) cs.Scores[ac] += prio;
    }
}

var searchCells = new List<int>(G.NUM_CELLS);
for (int i = 0; i < G.NUM_CELLS; i++)
    if (!G.IsSingleton(puzzle.Grid[i]))
        searchCells.Add(i);

setupSw.Stop();

// ---- Solve ----
var solver  = new ISSSolver(puzzle.Grid, searchCells.ToArray(), cs, acc, houseCells.ToArray());
var solveSw = Stopwatch.StartNew();

if (countAll)
{
    solver.CountSolutions();
}
else
{
    uint[]? solution = solver.FindSolution();
    if (solution != null)
    {
        Console.WriteLine("Solution found:");
        for (int r = 0; r < G.SIZE; r++)
        {
            for (int c = 0; c < G.SIZE; c++)
            {
                uint v = solution[G.CellIndex(r, c)];
                Console.Write(G.IsSingleton(v) ? G.SingletonValue(v).ToString() : "?");
                if (c < G.SIZE - 1) Console.Write(' ');
            }
            Console.WriteLine();
        }
    }
    else
    {
        Console.WriteLine("No solution found.");
    }
}

solveSw.Stop();

// ---- Stats (matches ISS JS counter names) ----
Console.WriteLine($"Solutions:             {solver.Solutions:N0}");
Console.WriteLine($"Guesses:               {solver.Guesses:N0}");
Console.WriteLine($"Values tried:          {solver.ValuesTried:N0}");
Console.WriteLine($"Constraints processed: {solver.ConstraintsProcessed:N0}");
Console.WriteLine($"Nodes searched:        {solver.NodesSearched:N0}");
Console.WriteLine($"Backtracks:            {solver.Backtracks:N0}");
Console.WriteLine($"Search space explored: {solver.SearchSpaceExplored:F2}%");
Console.WriteLine($"Search cells:          {searchCells.Count}  |  Arrows: {puzzle.Arrows.Count}");
Console.WriteLine($"Puzzle setup time:     {setupSw.Elapsed.TotalMilliseconds:F3} ms");
Console.WriteLine($"Runtime:               {solveSw.Elapsed.TotalMilliseconds:F3} ms");

return 0;

// ---- Helpers ----

static int[][] BuildPeers()
{
    var result = new int[G.NUM_CELLS][];
    for (int cell = 0; cell < G.NUM_CELLS; cell++)
    {
        var set = new HashSet<int>(20);
        int r = G.Row(cell), c = G.Col(cell);
        int b = G.Box(cell);
        int br = (b / 3) * 3, bc = (b % 3) * 3;
        for (int j = 0; j < G.SIZE; j++)
        {
            set.Add(G.CellIndex(r, j));
            set.Add(G.CellIndex(j, c));
        }
        for (int dr = 0; dr < 3; dr++)
            for (int dc = 0; dc < 3; dc++)
                set.Add(G.CellIndex(br + dr, bc + dc));
        set.Remove(cell);
        result[cell] = [.. set];
    }
    return result;
}
