import { applyPuzzleCommand } from "./applyPuzzleCommand";
import type { PuzzleCommand } from "./commands";
import type { PuzzlePackageV1 } from "./types";

export type ExecutablePuzzleCommand = Exclude<
  PuzzleCommand,
  { type: "restoreCandidateContext" }
>;

export interface PuzzleStoreChange {
  readonly semantic: boolean;
  readonly documentRevision: number;
  readonly semanticRevision: number;
}

export interface PuzzleStoreSnapshot {
  readonly document: PuzzlePackageV1;
  readonly lastChange: PuzzleStoreChange | null;
}

type PuzzleStoreListener = () => void;

function deepFreeze<T>(value: T): T {
  if (
    typeof value !== "object" ||
    value === null ||
    Object.isFrozen(value)
  ) {
    return value;
  }
  for (const child of Object.values(value)) {
    deepFreeze(child);
  }
  return Object.freeze(value);
}

function createSnapshot(
  document: PuzzlePackageV1,
  semantic: boolean | null,
): PuzzleStoreSnapshot {
  const frozenDocument = deepFreeze(document);
  return Object.freeze({
    document: frozenDocument,
    lastChange:
      semantic === null
        ? null
        : Object.freeze({
            semantic,
            documentRevision: frozenDocument.revision,
            semanticRevision: frozenDocument.semanticRevision,
          }),
  });
}

export class PuzzleStore {
  private snapshot: PuzzleStoreSnapshot;
  private readonly listeners = new Set<PuzzleStoreListener>();
  private readonly undoStack: PuzzleCommand[] = [];
  private readonly redoStack: PuzzleCommand[] = [];

  public constructor(document: PuzzlePackageV1) {
    this.snapshot = createSnapshot(structuredClone(document), null);
  }

  public readonly getSnapshot = (): PuzzleStoreSnapshot => this.snapshot;

  public readonly subscribe = (listener: PuzzleStoreListener): (() => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  public execute(command: ExecutablePuzzleCommand): void {
    const result = applyPuzzleCommand(this.snapshot.document, command);
    if (!result.changed) {
      return;
    }
    this.undoStack.push(result.inverse);
    this.redoStack.length = 0;
    this.commit(result.document, result.semanticChange);
  }

  public undo(): void {
    const command = this.undoStack.at(-1);
    if (command === undefined) {
      return;
    }
    const result = applyPuzzleCommand(this.snapshot.document, command);
    if (!result.changed) {
      return;
    }
    this.undoStack.pop();
    this.redoStack.push(result.inverse);
    this.commit(result.document, result.semanticChange);
  }

  public redo(): void {
    const command = this.redoStack.at(-1);
    if (command === undefined) {
      return;
    }
    const result = applyPuzzleCommand(this.snapshot.document, command);
    if (!result.changed) {
      return;
    }
    this.redoStack.pop();
    this.undoStack.push(result.inverse);
    this.commit(result.document, result.semanticChange);
  }

  private commit(document: PuzzlePackageV1, semantic: boolean): void {
    this.snapshot = createSnapshot(document, semantic);
    for (const listener of [...this.listeners]) {
      listener();
    }
  }
}
