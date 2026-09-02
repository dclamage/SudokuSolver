import { describe, expect, it, vi } from "vitest";

import { createStarterPuzzle } from "./createStarterPuzzle";
import { PuzzleStore } from "./PuzzleStore";
import type { ExecutablePuzzleCommand } from "./PuzzleStore";
import type { CandidateContext, PuzzlePackageV1 } from "./types";

function contextIds(document: PuzzlePackageV1): string[] {
  return document.authoring.candidateContexts.map((context) => context.id);
}

describe("PuzzleStore", () => {
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
    ).toEqual({ r1c1: ["2", "7"] });
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
    ).toEqual({ "aux-1": ["3", "9"] });
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
    ).toEqual(["2", "7"]);
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
