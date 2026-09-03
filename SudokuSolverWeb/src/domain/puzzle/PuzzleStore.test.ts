import { describe, expect, it, vi } from "vitest";

import { createStarterPuzzle } from "./createStarterPuzzle";
import { applyPuzzleCommand } from "./applyPuzzleCommand";
import { computeSemanticHash } from "./computeSemanticHash";
import { PuzzleStore } from "./PuzzleStore";
import type { ExecutablePuzzleCommand } from "./PuzzleStore";
import type { CandidateContext, PuzzlePackageV1 } from "./types";
import { validatePuzzlePackage } from "./validatePuzzlePackage";

function contextIds(document: PuzzlePackageV1): string[] {
  return document.authoring.candidateContexts.map((context) => context.id);
}

describe("PuzzleStore", () => {
  it("stores corner, centre, and color independently without changing semantics", async () => {
    const store = new PuzzleStore(createStarterPuzzle(() => "manual-state"));
    const startingHash = await computeSemanticHash(store.getSnapshot().document);

    store.execute({
      type: "setManualMarks",
      contextId: "setter-notes",
      cellId: "r1c1",
      kind: "corner",
      valueIds: ["7", "2", "7"],
    } as unknown as ExecutablePuzzleCommand);
    store.execute({
      type: "setManualMarks",
      contextId: "setter-notes",
      cellId: "r1c1",
      kind: "centre",
      valueIds: ["2"],
    } as unknown as ExecutablePuzzleCommand);
    store.execute({
      type: "setManualColor",
      contextId: "setter-notes",
      cellId: "r1c1",
      color: "cyan",
    } as unknown as ExecutablePuzzleCommand);

    expect(
      store.getSnapshot().document.authoring.manualMarks["setter-notes"].r1c1,
    ).toEqual({ corner: ["2", "7"], centre: ["2"], color: "cyan" });
    expect(store.getSnapshot().document.semanticRevision).toBe(1);
    await expect(
      computeSemanticHash(store.getSnapshot().document),
    ).resolves.toBe(startingHash);

    store.undo();
    expect(
      store.getSnapshot().document.authoring.manualMarks["setter-notes"].r1c1,
    ).toEqual({ corner: ["2", "7"], centre: ["2"], color: null });
    store.redo();
    expect(
      store.getSnapshot().document.authoring.manualMarks["setter-notes"].r1c1,
    ).toEqual({ corner: ["2", "7"], centre: ["2"], color: "cyan" });
  });

  it("duplicates and restores every manual cell state field", () => {
    const store = new PuzzleStore(createStarterPuzzle(() => "manual-clone"));
    for (const command of [
      {
        type: "setManualMarks",
        contextId: "setter-notes",
        cellId: "aux-1",
        kind: "corner",
        valueIds: ["9"],
      },
      {
        type: "setManualMarks",
        contextId: "setter-notes",
        cellId: "aux-1",
        kind: "centre",
        valueIds: ["3"],
      },
      {
        type: "setManualColor",
        contextId: "setter-notes",
        cellId: "aux-1",
        color: "rose",
      },
    ]) {
      store.execute(command as unknown as ExecutablePuzzleCommand);
    }

    store.execute({
      type: "duplicateCandidateContext",
      sourceContextId: "setter-notes",
      contextId: "notes-copy",
      name: "Notes copy",
    });
    expect(
      store.getSnapshot().document.authoring.manualMarks["notes-copy"][
        "aux-1"
      ],
    ).toEqual({ corner: ["9"], centre: ["3"], color: "rose" });

    store.execute({ type: "removeCandidateContext", contextId: "notes-copy" });
    store.undo();
    expect(
      store.getSnapshot().document.authoring.manualMarks["notes-copy"][
        "aux-1"
      ],
    ).toEqual({ corner: ["9"], centre: ["3"], color: "rose" });
  });

  it("validates manual mark kind and candidate-input capability before mutation", () => {
    const document = createStarterPuzzle(() => "manual-validation");
    document.cells.r1c1.input.acceptsCandidates = false;
    const store = new PuzzleStore(document);
    const before = store.getSnapshot();

    expect(() =>
      store.execute({
        type: "setManualMarks",
        contextId: "setter-notes",
        cellId: "r1c1",
        kind: "edge",
        valueIds: ["2"],
      } as unknown as ExecutablePuzzleCommand),
    ).toThrow("manual mark kind edge is invalid");
    expect(() =>
      store.execute({
        type: "setManualMarks",
        contextId: "setter-notes",
        cellId: "r1c1",
        kind: "corner",
        valueIds: ["2"],
      }),
    ).toThrow("cell r1c1 does not accept candidate marks");
    expect(store.getSnapshot()).toBe(before);
  });

  it("normalizes all restored manual cell fields", () => {
    const document = createStarterPuzzle(() => "manual-restore");
    const result = applyPuzzleCommand(document, {
      type: "restoreCandidateContext",
      context: { id: "restored", name: "Restored", kind: "manual" },
      index: 1,
      manualMarks: {
        "aux-1": {
          corner: ["9", "3", "9"],
          centre: ["7", "3", "7"],
          color: "yellow",
        },
      },
    });

    expect(result.document.authoring.manualMarks.restored["aux-1"]).toEqual({
      corner: ["3", "9"],
      centre: ["3", "7"],
      color: "yellow",
    });
  });
  it("commits a gesture as one reversible revision", () => {
    const store = new PuzzleStore(createStarterPuzzle(() => "history-test"));

    store.execute({ type: "setGiven", cellId: "r1c1", valueId: "5" });

    expect(store.getSnapshot().document.givens.r1c1).toBe("5");
    expect(store.getSnapshot().document.revision).toBe(2);
    expect(store.getSnapshot().document.semanticRevision).toBe(2);

    store.undo();

    expect(store.getSnapshot().document.givens.r1c1).toBeUndefined();
    expect(store.getSnapshot().document.revision).toBe(3);
    expect(store.getSnapshot().document.semanticRevision).toBe(3);
  });

  it("moves an auxiliary cell without changing solver membership", () => {
    const store = new PuzzleStore(createStarterPuzzle(() => "move-test"));

    store.execute({ type: "moveCell", cellId: "aux-1", x: 11.25, y: 4.5 });

    expect(store.getSnapshot().document.cells["aux-1"].shape).toMatchObject({
      x: 11.25,
      y: 4.5,
    });
    expect(store.getSnapshot().document.revision).toBe(2);
    expect(store.getSnapshot().document.semanticRevision).toBe(1);
    expect(
      store.getSnapshot().document.solverProjections[0].cellIdsByRow.flat(),
    ).not.toContain("aux-1");
  });

  it("restores a removed context at its original index with all manual marks", () => {
    const store = new PuzzleStore(createStarterPuzzle(() => "remove-test"));
    store.execute({
      type: "setManualMarks",
      contextId: "setter-notes",
      cellId: "r1c1",
      valueIds: ["2", "7"],
    });

    store.execute({
      type: "removeCandidateContext",
      contextId: "setter-notes",
    });

    expect(contextIds(store.getSnapshot().document)).toEqual([
      "true-candidates",
      "logical-solver",
    ]);
    expect(
      store.getSnapshot().document.authoring.manualMarks["setter-notes"],
    ).toBeUndefined();

    store.undo();

    expect(contextIds(store.getSnapshot().document)).toEqual([
      "setter-notes",
      "true-candidates",
      "logical-solver",
    ]);
    expect(
      store.getSnapshot().document.authoring.manualMarks["setter-notes"],
    ).toEqual({
      r1c1: { corner: ["2", "7"], centre: [], color: null },
    });
  });

  it("undoes a context reorder back to the exact prior order", () => {
    const store = new PuzzleStore(createStarterPuzzle(() => "reorder-test"));

    store.execute({
      type: "moveCandidateContext",
      contextId: "logical-solver",
      toIndex: 0,
    });
    expect(contextIds(store.getSnapshot().document)).toEqual([
      "logical-solver",
      "setter-notes",
      "true-candidates",
    ]);

    store.undo();

    expect(contextIds(store.getSnapshot().document)).toEqual([
      "setter-notes",
      "true-candidates",
      "logical-solver",
    ]);
  });

  it("duplicates context configuration and marks under the new stable ID", () => {
    const store = new PuzzleStore(createStarterPuzzle(() => "duplicate-test"));
    store.execute({
      type: "setManualMarks",
      contextId: "true-candidates",
      cellId: "aux-1",
      valueIds: ["3", "9"],
    });

    store.execute({
      type: "duplicateCandidateContext",
      sourceContextId: "true-candidates",
      contextId: "true-candidates-copy",
      name: "True candidates copy",
      index: 1,
    });

    expect(store.getSnapshot().document.authoring.candidateContexts[1]).toEqual({
      id: "true-candidates-copy",
      name: "True candidates copy",
      kind: "trueCandidates",
      refresh: "automatic",
      display: "possibility",
      solutionCountCap: 1000,
    });
    expect(
      store.getSnapshot().document.authoring.manualMarks[
        "true-candidates-copy"
      ],
    ).toEqual({
      "aux-1": { corner: ["3", "9"], centre: [], color: null },
    });
  });

  it("configures only True Candidates contexts and treats authoring as nonsemantic", () => {
    const store = new PuzzleStore(createStarterPuzzle(() => "config-test"));

    store.execute({
      type: "configureTrueCandidates",
      contextId: "true-candidates",
      refresh: "onRequest",
      display: "solutionFrequency",
      solutionCountCap: 250,
    });

    expect(store.getSnapshot().document.authoring.candidateContexts[1]).toEqual({
      id: "true-candidates",
      name: "True candidates",
      kind: "trueCandidates",
      refresh: "onRequest",
      display: "solutionFrequency",
      solutionCountCap: 250,
    });
    expect(store.getSnapshot().document.semanticRevision).toBe(1);
    expect(() =>
      store.execute({
        type: "configureTrueCandidates",
        contextId: "setter-notes",
        refresh: "automatic",
        display: "possibility",
        solutionCountCap: 10,
      }),
    ).toThrow("candidate context setter-notes is not a True Candidates context");
  });

  it.each([
    {
      name: "refresh",
      command: {
        type: "configureTrueCandidates",
        contextId: "true-candidates",
        refresh: "eventually",
        display: "possibility",
        solutionCountCap: 10,
      },
      error: "refresh must be automatic or onRequest",
    },
    {
      name: "display",
      command: {
        type: "configureTrueCandidates",
        contextId: "true-candidates",
        refresh: "automatic",
        display: "heatmap",
        solutionCountCap: 10,
      },
      error: "display is invalid",
    },
    {
      name: "solution count cap",
      command: {
        type: "configureTrueCandidates",
        contextId: "true-candidates",
        refresh: "automatic",
        display: "possibility",
        solutionCountCap: -1,
      },
      error: "solutionCountCap must be a non-negative safe integer",
    },
  ])("rejects an invalid runtime $name configuration", ({ command, error }) => {
    const store = new PuzzleStore(createStarterPuzzle(() => "invalid-config"));
    const before = store.getSnapshot();

    expect(() =>
      store.execute(command as unknown as ExecutablePuzzleCommand),
    ).toThrow(error);
    expect(store.getSnapshot()).toBe(before);
  });

  it("renames an unknown context without dropping opaque configuration", () => {
    const document = createStarterPuzzle(() => "future-context-test");
    document.authoring.candidateContexts = [
      ...document.authoring.candidateContexts,
      {
        id: "future-candidates",
        name: "Future candidates",
        kind: "futureCandidates",
        futurePolicy: { retain: true, modes: ["one", "two"] },
      } as unknown as CandidateContext,
    ];
    document.authoring.manualMarks["future-candidates"] = {};
    const store = new PuzzleStore(document);

    store.execute({
      type: "renameCandidateContext",
      contextId: "future-candidates",
      name: "Renamed future candidates",
    });

    expect(
      store.getSnapshot().document.authoring.candidateContexts.at(-1),
    ).toEqual({
      id: "future-candidates",
      name: "Renamed future candidates",
      kind: "futureCandidates",
      futurePolicy: { retain: true, modes: ["one", "two"] },
    });
  });

  it("round-trips, renames, and duplicates an opaque unknown context", () => {
    const opaqueContext = {
      id: "future-candidates",
      name: "Future candidates",
      kind: "futureCandidates",
      futurePolicy: {
        retain: true,
        modes: ["one", "two"],
        nested: { threshold: 0.5 },
      },
    } as unknown as CandidateContext;
    const store = new PuzzleStore(
      createStarterPuzzle(() => "future-round-trip-test"),
    );

    store.execute({ type: "addCandidateContext", context: opaqueContext });
    store.execute({
      type: "duplicateCandidateContext",
      sourceContextId: opaqueContext.id,
      contextId: "future-candidates-copy",
      name: "Future candidates copy",
    });
    store.execute({
      type: "renameCandidateContext",
      contextId: opaqueContext.id,
      name: "Renamed future candidates",
    });

    expect(
      store.getSnapshot().document.authoring.candidateContexts.slice(-2),
    ).toEqual([
      {
        ...opaqueContext,
        name: "Renamed future candidates",
      },
      {
        ...opaqueContext,
        id: "future-candidates-copy",
        name: "Future candidates copy",
      },
    ]);
    const reloaded = validatePuzzlePackage(
      JSON.parse(JSON.stringify(store.getSnapshot().document)),
    );
    expect(reloaded.authoring.candidateContexts.slice(-2)).toEqual(
      store.getSnapshot().document.authoring.candidateContexts.slice(-2),
    );
  });

  it.each([
    {
      name: "missing True Candidates refresh",
      context: {
        id: "invalid-true",
        name: "Invalid true",
        kind: "trueCandidates",
        display: "possibility",
        solutionCountCap: 10,
      },
      error: "refresh must be automatic or onRequest",
    },
    {
      name: "invalid True Candidates display",
      context: {
        id: "invalid-true",
        name: "Invalid true",
        kind: "trueCandidates",
        refresh: "automatic",
        display: "heatmap",
        solutionCountCap: 10,
      },
      error: "display is invalid",
    },
    {
      name: "invalid True Candidates count cap",
      context: {
        id: "invalid-true",
        name: "Invalid true",
        kind: "trueCandidates",
        refresh: "automatic",
        display: "possibility",
        solutionCountCap: -1,
      },
      error: "solutionCountCap must be a non-negative safe integer",
    },
    {
      name: "invalid Logical Solver follow flag",
      context: {
        id: "invalid-logical",
        name: "Invalid logical",
        kind: "logicalSolver",
        followPuzzleRevision: false,
        enabledTechniqueIds: [],
      },
      error: "followPuzzleRevision must be true",
    },
    {
      name: "non-array Logical Solver techniques",
      context: {
        id: "invalid-logical",
        name: "Invalid logical",
        kind: "logicalSolver",
        followPuzzleRevision: true,
        enabledTechniqueIds: "single",
      },
      error: "enabledTechniqueIds must be an array of non-empty strings",
    },
    {
      name: "empty Logical Solver technique",
      context: {
        id: "invalid-logical",
        name: "Invalid logical",
        kind: "logicalSolver",
        followPuzzleRevision: true,
        enabledTechniqueIds: [""],
      },
      error: "enabledTechniqueIds must be an array of non-empty strings",
    },
    {
      name: "empty unknown kind",
      context: {
        id: "invalid-unknown",
        name: "Invalid unknown",
        kind: "",
        futurePolicy: { retain: true },
      },
      error: "kind must be a non-empty string",
    },
    {
      name: "non-JSON unknown configuration",
      context: {
        id: "invalid-unknown",
        name: "Invalid unknown",
        kind: "futureCandidates",
        futurePolicy: undefined,
      },
      error: "candidate context invalid-unknown must be valid JSON",
    },
  ])("rejects $name at the add boundary", ({ context, error }) => {
    const store = new PuzzleStore(createStarterPuzzle(() => "invalid-context"));
    const before = store.getSnapshot();
    const listener = vi.fn();
    store.subscribe(listener);

    expect(() =>
      store.execute({
        type: "addCandidateContext",
        context: context as unknown as CandidateContext,
      }),
    ).toThrow(error);
    expect(store.getSnapshot()).toBe(before);
    expect(listener).not.toHaveBeenCalled();
  });

  it("rejects a malformed known context at the restore boundary", () => {
    const document = createStarterPuzzle(() => "invalid-restore");

    expect(() =>
      applyPuzzleCommand(document, {
        type: "restoreCandidateContext",
        context: {
          id: "invalid-logical",
          name: "Invalid logical",
          kind: "logicalSolver",
          followPuzzleRevision: true,
          enabledTechniqueIds: [""],
        } as unknown as CandidateContext,
        index: 1,
        manualMarks: {},
      }),
    ).toThrow("enabledTechniqueIds must be an array of non-empty strings");
  });

  it.each(["renameCandidateContext", "duplicateCandidateContext"] as const)(
    "rejects malformed known source definitions before %s cloning",
    (type) => {
      const document = createStarterPuzzle(() => "invalid-clone");
      document.authoring.candidateContexts =
        document.authoring.candidateContexts.map((context) =>
          context.id === "logical-solver"
            ? ({
                ...context,
                enabledTechniqueIds: "single",
              } as unknown as CandidateContext)
            : context,
        );
      const store = new PuzzleStore(document);

      expect(() =>
        type === "renameCandidateContext"
          ? store.execute({
              type,
              contextId: "logical-solver",
              name: "Renamed logical",
            })
          : store.execute({
              type,
              sourceContextId: "logical-solver",
              contextId: "logical-solver-copy",
              name: "Logical copy",
            }),
      ).toThrow("enabledTechniqueIds must be an array of non-empty strings");
    },
  );

  it("reports the exact last change and notifies active listeners once per commit", () => {
    const store = new PuzzleStore(createStarterPuzzle(() => "listener-test"));
    const listener = vi.fn();
    const unsubscribe = store.subscribe(listener);

    expect(store.getSnapshot().lastChange).toBeNull();

    store.execute({ type: "moveCell", cellId: "aux-1", x: 12, y: 5 });

    expect(listener).toHaveBeenCalledTimes(1);
    expect(store.getSnapshot().lastChange).toEqual({
      semantic: false,
      documentRevision: 2,
      semanticRevision: 1,
    });

    unsubscribe();
    store.execute({ type: "setGiven", cellId: "aux-1", valueId: "4" });

    expect(listener).toHaveBeenCalledTimes(1);
    expect(store.getSnapshot().lastChange).toEqual({
      semantic: true,
      documentRevision: 3,
      semanticRevision: 2,
    });
  });

  it("keeps previous snapshots unchanged when later commands commit", () => {
    const store = new PuzzleStore(createStarterPuzzle(() => "snapshot-test"));
    const before = store.getSnapshot();

    store.execute({ type: "setGiven", cellId: "r1c1", valueId: "6" });

    expect(before.document.givens.r1c1).toBeUndefined();
    expect(before.document.revision).toBe(1);
    expect(before.lastChange).toBeNull();
    expect(store.getSnapshot()).not.toBe(before);
  });

  it("redoes an undone command and clears redo after a new command", () => {
    const store = new PuzzleStore(createStarterPuzzle(() => "redo-test"));
    store.execute({ type: "setGiven", cellId: "r1c1", valueId: "1" });
    store.undo();

    store.redo();

    expect(store.getSnapshot().document.givens.r1c1).toBe("1");
    expect(store.getSnapshot().document.revision).toBe(4);
    expect(store.getSnapshot().document.semanticRevision).toBe(4);

    store.undo();
    store.execute({ type: "setGiven", cellId: "r1c2", valueId: "2" });
    const snapshot = store.getSnapshot();

    store.redo();

    expect(store.getSnapshot()).toBe(snapshot);
    expect(store.getSnapshot().document.givens.r1c1).toBeUndefined();
  });

  it("preserves redo and the current snapshot when a semantic command is a no-op", () => {
    const store = new PuzzleStore(createStarterPuzzle(() => "semantic-no-op"));
    store.execute({ type: "setGiven", cellId: "r1c1", valueId: "5" });
    store.execute({ type: "setGiven", cellId: "r1c2", valueId: "6" });
    store.undo();
    const beforeNoOp = store.getSnapshot();
    const listener = vi.fn();
    store.subscribe(listener);

    store.execute({ type: "setGiven", cellId: "r1c1", valueId: "5" });

    expect(store.getSnapshot()).toBe(beforeNoOp);
    expect(store.getSnapshot().document.revision).toBe(4);
    expect(store.getSnapshot().document.semanticRevision).toBe(4);
    expect(listener).not.toHaveBeenCalled();

    store.redo();

    expect(store.getSnapshot().document.givens.r1c2).toBe("6");
    expect(store.getSnapshot().document.revision).toBe(5);
    expect(store.getSnapshot().document.semanticRevision).toBe(5);
    expect(listener).toHaveBeenCalledTimes(1);
  });

  it.each<{ name: string; command: ExecutablePuzzleCommand }>([
    {
      name: "existing cell coordinates",
      command: { type: "moveCell", cellId: "aux-1", x: 10, y: 4 },
    },
    {
      name: "empty manual marks",
      command: {
        type: "setManualMarks",
        contextId: "setter-notes",
        cellId: "r1c1",
        valueIds: [],
      },
    },
    {
      name: "existing context name",
      command: {
        type: "renameCandidateContext",
        contextId: "setter-notes",
        name: "Setter notes",
      },
    },
    {
      name: "existing True Candidates configuration",
      command: {
        type: "configureTrueCandidates",
        contextId: "true-candidates",
        refresh: "automatic",
        display: "possibility",
        solutionCountCap: 1000,
      },
    },
    {
      name: "existing context index",
      command: {
        type: "moveCandidateContext",
        contextId: "setter-notes",
        toIndex: 0,
      },
    },
  ])(
    "does not commit or notify for nonsemantic no-op: $name",
    ({ command }) => {
      const store = new PuzzleStore(createStarterPuzzle(() => "no-op-test"));
      const listener = vi.fn();
      store.subscribe(listener);
      const before = store.getSnapshot();

      store.execute(command);

      expect(store.getSnapshot()).toBe(before);
      expect(store.getSnapshot().document.revision).toBe(1);
      expect(store.getSnapshot().document.semanticRevision).toBe(1);
      expect(listener).not.toHaveBeenCalled();
    },
  );

  it("normalizes manual marks by domain order before testing equality", () => {
    const store = new PuzzleStore(createStarterPuzzle(() => "mark-no-op"));
    store.execute({
      type: "setManualMarks",
      contextId: "setter-notes",
      cellId: "r1c1",
      valueIds: ["7", "2", "7"],
    });
    expect(
      store.getSnapshot().document.authoring.manualMarks["setter-notes"].r1c1,
    ).toEqual({ corner: ["2", "7"], centre: [], color: null });
    const beforeNoOp = store.getSnapshot();
    const listener = vi.fn();
    store.subscribe(listener);

    store.execute({
      type: "setManualMarks",
      contextId: "setter-notes",
      cellId: "r1c1",
      valueIds: ["2", "7"],
    });

    expect(store.getSnapshot()).toBe(beforeNoOp);
    expect(listener).not.toHaveBeenCalled();

    store.undo();

    expect(
      store.getSnapshot().document.authoring.manualMarks["setter-notes"].r1c1,
    ).toBeUndefined();
    expect(store.getSnapshot().document.revision).toBe(3);
    expect(listener).toHaveBeenCalledTimes(1);
  });

  it("rejects invalid commands without committing or notifying", () => {
    const store = new PuzzleStore(createStarterPuzzle(() => "invalid-test"));
    const listener = vi.fn();
    store.subscribe(listener);
    const before = store.getSnapshot();

    expect(() =>
      store.execute({
        type: "setGiven",
        cellId: "r1c1",
        valueId: "not-in-domain",
      }),
    ).toThrow("value not-in-domain is not in cell r1c1 domain digits-1-9");

    expect(store.getSnapshot()).toBe(before);
    expect(listener).not.toHaveBeenCalled();
  });

  it("rejects moving polygon and path cells with a clear rectangular-shape error", () => {
    for (const kind of ["polygon", "path"] as const) {
      const document = createStarterPuzzle(() => `${kind}-test`);
      document.cells["aux-1"].shape =
        kind === "polygon"
          ? { kind, points: [{ x: 10, y: 4 }] }
          : { kind, d: "M 10 4" };
      const store = new PuzzleStore(document);

      expect(() =>
        store.execute({
          type: "moveCell",
          cellId: "aux-1",
          x: 12,
          y: 5,
        }),
      ).toThrow(`moveCell supports rectangular cells only; aux-1 uses ${kind}`);
    }
  });

  it("rejects removing the last context", () => {
    const document = createStarterPuzzle(() => "last-context-test");
    const onlyContext: CandidateContext = {
      id: "only-context",
      name: "Only context",
      kind: "manual",
    };
    document.authoring = {
      candidateContexts: [onlyContext],
      manualMarks: { "only-context": {} },
    };
    const store = new PuzzleStore(document);

    expect(() =>
      store.execute({
        type: "removeCandidateContext",
        contextId: "only-context",
      }),
    ).toThrow("cannot remove the last candidate context");
  });
});
