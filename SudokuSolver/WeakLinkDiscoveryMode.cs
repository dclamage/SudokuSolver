namespace SudokuSolver;

/// <summary>
/// When a brute-force operation pays for dynamic weak-link discovery.
/// </summary>
/// <remarks>
/// Discovery probes every candidate of every unset cell — setting it and propagating — to find weak
/// links that ordinary propagation misses. It is an unconditional up-front payment for a benefit
/// that ranges from 32x fewer search nodes to actively harmful, so no single setting wins
/// everywhere. Measurements: docs/weak-link-discovery-tradeoff.md.
/// </remarks>
public enum WeakLinkDiscoveryMode
{
    /// <summary>
    /// Always probe before searching. Essential on some variants (32x on <c>variant-orbit</c>) and
    /// pure overhead on others (22x on <c>variant-cloneways</c>).
    /// </summary>
    Always,

    /// <summary>
    /// Never probe. Much faster wherever brute force alone suffices, catastrophic on the puzzles
    /// that depend on the discovered links.
    /// </summary>
    Never,

    /// <summary>
    /// Search without probing first; if that search exceeds
    /// <see cref="Solver.WeakLinkDiscoveryNodeThreshold"/> nodes, abandon it, probe, and restart.
    /// Discovery is then only paid for by puzzles that have already proven expensive without it,
    /// which is the condition under which it pays off. The cost is a bounded wasted prefix.
    /// </summary>
    /// <remarks>
    /// Only the operations that can be restarted cleanly honour this — <see cref="Solver.FindSolution"/>,
    /// <see cref="Solver.CountSolutions"/> and <see cref="Solver.TrueCandidates"/>. The sampling
    /// estimators treat it as <see cref="Always"/>, and so does <see cref="Solver.CountSolutions"/>
    /// when a <c>solutionEvent</c> handler is attached, since a restart would report solutions twice.
    /// </remarks>
    Deferred,
}
