import type {
  CellId,
  PuzzlePackageV1,
  ValueId,
} from "../../domain/puzzle/types";

export type PlaytestInputMode =
  | "digit"
  | "corner"
  | "centre"
  | "color"
  | "erase";

export interface PlaytestSessionSnapshot {
  readonly values: Readonly<Record<CellId, ValueId>>;
  readonly manualCandidates: {
    readonly corner: Readonly<Record<CellId, readonly ValueId[]>>;
    readonly centre: Readonly<Record<CellId, readonly ValueId[]>>;
  };
  readonly colors: Readonly<Record<CellId, readonly string[]>>;
  readonly inputMode: PlaytestInputMode;
  readonly historyLength: number;
  readonly canUndo: boolean;
  readonly canRedo: boolean;
  readonly timerStartedAt: number | null;
  readonly elapsedMilliseconds: number;
  readonly timerRunning: boolean;
  readonly checkMessage: string | null;
}

interface PlaytestContent {
  values: Record<CellId, ValueId>;
  corner: Record<CellId, readonly ValueId[]>;
  centre: Record<CellId, readonly ValueId[]>;
  colors: Record<CellId, readonly string[]>;
}

type PlaytestListener = () => void;

const uniquenessGroupRoles: ReadonlySet<string> = new Set([
  "row",
  "column",
  "region",
  "all-different",
]);

function cloneArrayRecord<T>(
  record: Readonly<Record<CellId, readonly T[]>>,
): Record<CellId, readonly T[]> {
  return Object.fromEntries(
    Object.entries(record).map(([cellId, values]) => [cellId, [...values]]),
  );
}

function cloneContent(content: PlaytestContent): PlaytestContent {
  return {
    values: { ...content.values },
    corner: cloneArrayRecord(content.corner),
    centre: cloneArrayRecord(content.centre),
    colors: cloneArrayRecord(content.colors),
  };
}

function freezeArrayRecord<T>(record: Record<CellId, readonly T[]>) {
  for (const values of Object.values(record)) {
    Object.freeze(values);
  }
  return Object.freeze(record);
}

function recordsEqual<T>(
  left: Readonly<Record<CellId, readonly T[]>>,
  right: Readonly<Record<CellId, readonly T[]>>,
) {
  const leftEntries = Object.entries(left);
  const rightKeys = Object.keys(right);
  return (
    leftEntries.length === rightKeys.length &&
    leftEntries.every(([cellId, values]) => {
      const other = right[cellId];
      return (
        other !== undefined &&
        values.length === other.length &&
        values.every((value, index) => value === other[index])
      );
    })
  );
}

function contentsEqual(left: PlaytestContent, right: PlaytestContent) {
  const leftValues = Object.entries(left.values);
  return (
    leftValues.length === Object.keys(right.values).length &&
    leftValues.every(([cellId, value]) => right.values[cellId] === value) &&
    recordsEqual(left.corner, right.corner) &&
    recordsEqual(left.centre, right.centre) &&
    recordsEqual(left.colors, right.colors)
  );
}

function orderedToggle(values: readonly string[], value: string): string[] {
  const selected = new Set(values);
  if (selected.has(value)) {
    selected.delete(value);
  } else {
    selected.add(value);
  }
  return [...selected].sort((left, right) =>
    left.localeCompare(right, undefined, { numeric: true }),
  );
}

export class PlaytestSession {
  private content: PlaytestContent = {
    values: {},
    corner: {},
    centre: {},
    colors: {},
  };
  private readonly undoStack: PlaytestContent[] = [];
  private readonly redoStack: PlaytestContent[] = [];
  private readonly listeners = new Set<PlaytestListener>();
  private inputMode: PlaytestInputMode = "digit";
  private checkMessage: string | null = null;
  private timerStartedAt: number | null = null;
  private elapsedMilliseconds = 0;
  private snapshot: PlaytestSessionSnapshot;

  public constructor(
    private readonly now: () => number = () => Date.now(),
  ) {
    this.snapshot = this.createSnapshot();
  }

  public readonly getSnapshot = (): PlaytestSessionSnapshot => this.snapshot;

  public readonly subscribe = (listener: PlaytestListener): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public setInputMode(mode: PlaytestInputMode): void {
    if (this.inputMode === mode) {
      return;
    }
    this.inputMode = mode;
    this.publish();
  }

  public resumeTimer(): void {
    if (this.timerStartedAt !== null) {
      return;
    }
    this.timerStartedAt = this.now();
    this.publish();
  }

  public pauseTimer(): void {
    if (this.timerStartedAt === null) {
      return;
    }
    this.elapsedMilliseconds = this.getElapsedMilliseconds();
    this.timerStartedAt = null;
    this.publish();
  }

  public enterValue(cellId: CellId, valueId: ValueId): void {
    this.commit((next) => {
      if (next.values[cellId] === valueId) {
        delete next.values[cellId];
      } else {
        next.values[cellId] = valueId;
      }
      delete next.corner[cellId];
      delete next.centre[cellId];
    });
  }

  public toggleCandidate(
    kind: "corner" | "centre",
    cellId: CellId,
    valueId: ValueId,
  ): void {
    this.commit((next) => {
      delete next.values[cellId];
      const candidates = orderedToggle(next[kind][cellId] ?? [], valueId);
      if (candidates.length === 0) {
        delete next[kind][cellId];
      } else {
        next[kind][cellId] = candidates;
      }
    });
  }

  public setManualMarks(
    kind: "corner" | "centre",
    cellId: CellId,
    valueIds: readonly ValueId[],
  ): void {
    this.commit((next) => {
      delete next.values[cellId];
      if (valueIds.length === 0) {
        delete next[kind][cellId];
      } else {
        next[kind][cellId] = [...valueIds];
      }
    });
  }

  public applyColor(cellId: CellId, color: string): void {
    this.commit((next) => {
      if (next.colors[cellId]?.[0] === color) {
        delete next.colors[cellId];
      } else {
        next.colors[cellId] = [color];
      }
    });
  }

  public erase(cellId: CellId): void {
    this.commit((next) => {
      delete next.values[cellId];
      delete next.corner[cellId];
      delete next.centre[cellId];
      delete next.colors[cellId];
    });
  }

  public undo(): void {
    const previous = this.undoStack.pop();
    if (previous === undefined) {
      return;
    }
    this.redoStack.push(cloneContent(this.content));
    this.content = previous;
    this.checkMessage = null;
    this.publish();
  }

  public redo(): void {
    const next = this.redoStack.pop();
    if (next === undefined) {
      return;
    }
    this.undoStack.push(cloneContent(this.content));
    this.content = next;
    this.checkMessage = null;
    this.publish();
  }

  public check(puzzle: PuzzlePackageV1): void {
    const values = { ...puzzle.givens, ...this.content.values };
    const hasConflict = Object.values(puzzle.groups)
      .filter((group) =>
        group.roles.some((role) => uniquenessGroupRoles.has(role)),
      )
      .some((group) => {
        const seen = new Set<ValueId>();
        for (const cellId of group.cellIds) {
          const value = values[cellId];
          if (value !== undefined) {
            if (seen.has(value)) {
              return true;
            }
            seen.add(value);
          }
        }
        return false;
      });
    this.checkMessage = hasConflict
      ? "Basic check: duplicate value in a uniqueness group. Other constraints are not checked."
      : "Basic check: no duplicate values in uniqueness groups. Other constraints are not checked.";
    this.publish();
  }

  public getElapsedMilliseconds(): number {
    return Math.max(
      0,
      this.elapsedMilliseconds +
        (this.timerStartedAt === null ? 0 : this.now() - this.timerStartedAt),
    );
  }

  private commit(update: (content: PlaytestContent) => void): void {
    const next = cloneContent(this.content);
    update(next);
    if (contentsEqual(this.content, next)) {
      return;
    }
    this.undoStack.push(cloneContent(this.content));
    this.redoStack.length = 0;
    this.content = next;
    this.checkMessage = null;
    this.publish();
  }

  private publish(): void {
    this.snapshot = this.createSnapshot();
    for (const listener of [...this.listeners]) {
      listener();
    }
  }

  private createSnapshot(): PlaytestSessionSnapshot {
    const values = Object.freeze({ ...this.content.values });
    const corner = freezeArrayRecord(cloneArrayRecord(this.content.corner));
    const centre = freezeArrayRecord(cloneArrayRecord(this.content.centre));
    const colors = freezeArrayRecord(cloneArrayRecord(this.content.colors));
    return Object.freeze({
      values,
      manualCandidates: Object.freeze({ corner, centre }),
      colors,
      inputMode: this.inputMode,
      historyLength: this.undoStack.length,
      canUndo: this.undoStack.length > 0,
      canRedo: this.redoStack.length > 0,
      timerStartedAt: this.timerStartedAt,
      elapsedMilliseconds: this.getElapsedMilliseconds(),
      timerRunning: this.timerStartedAt !== null,
      checkMessage: this.checkMessage,
    });
  }
}
