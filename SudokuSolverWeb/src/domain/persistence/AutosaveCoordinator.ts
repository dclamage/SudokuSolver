import type { PuzzlePackageV1 } from "../puzzle/types";
import {
  type DocumentRepository,
  DocumentRepositoryError,
  type PersistedDocumentSnapshot,
  snapshotDocument,
} from "./DocumentRepository";

type Listener = () => void;

export interface AutosaveScheduler {
  set(callback: () => void, delayMs: number): unknown;
  clear(handle: unknown): void;
}

export type AutosaveSnapshot =
  | { readonly status: "idle"; readonly documentId: null; readonly revision: null; readonly error: null }
  | {
      readonly status: "saving" | "saved";
      readonly documentId: string;
      readonly revision: number;
      readonly error: null;
    }
  | {
      readonly status: "failed";
      readonly documentId: string;
      readonly revision: number;
      readonly error: DocumentRepositoryError;
    };

export type AutosaveResult =
  | { readonly status: "saved"; readonly revision: number }
  | { readonly status: "failed"; readonly revision: number }
  | { readonly status: "superseded"; readonly revision: number }
  | { readonly status: "canceled"; readonly revision: number };

interface ScheduledBatch {
  readonly token: number;
  readonly snapshot: PersistedDocumentSnapshot;
  readonly promise: Promise<AutosaveResult>;
  readonly resolve: (result: AutosaveResult) => void;
}

const defaultScheduler: AutosaveScheduler = {
  set: (callback, delayMs) => globalThis.setTimeout(callback, delayMs),
  clear: (handle) => globalThis.clearTimeout(handle as ReturnType<typeof setTimeout>),
};

export interface AutosaveCoordinatorOptions {
  readonly debounceMs?: number;
  readonly scheduler?: AutosaveScheduler;
  readonly now?: () => number;
}

/** Coordinates revision-owned journals and conditional document saves. */
export class AutosaveCoordinator {
  private readonly listeners = new Set<Listener>();
  private readonly debounceMs: number;
  private readonly scheduler: AutosaveScheduler;
  private readonly now: () => number;
  private sequence = 0;
  private latestToken = 0;
  private pending: ScheduledBatch | null = null;
  private timer: unknown = null;
  private failedSnapshot: PersistedDocumentSnapshot | null = null;
  private disposed = false;
  private state: AutosaveSnapshot = Object.freeze({
    status: "idle",
    documentId: null,
    revision: null,
    error: null,
  });

  public constructor(
    private readonly repository: DocumentRepository,
    options: AutosaveCoordinatorOptions = {},
  ) {
    this.debounceMs = options.debounceMs ?? 0;
    this.scheduler = options.scheduler ?? defaultScheduler;
    this.now = options.now ?? Date.now;
  }

  public readonly getSnapshot = (): AutosaveSnapshot => this.state;

  public readonly subscribe = (listener: Listener): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public schedule(document: PuzzlePackageV1): Promise<AutosaveResult> {
    const persisted = snapshotDocument(
      document,
      new Date(this.now()).toISOString(),
    );
    if (this.disposed) {
      return Promise.resolve({ status: "canceled", revision: persisted.revision });
    }

    if (this.pending !== null) {
      this.cancelTimer();
      this.pending.resolve({
        status: "superseded",
        revision: this.pending.snapshot.revision,
      });
      this.pending = null;
    }

    const batch = this.createBatch(persisted);
    this.pending = batch;
    this.latestToken = batch.token;
    this.failedSnapshot = null;
    this.publish({
      status: "saving",
      documentId: persisted.id,
      revision: persisted.revision,
      error: null,
    });

    if (this.debounceMs > 0) {
      this.timer = this.scheduler.set(() => this.startPending(), this.debounceMs);
    } else {
      this.startPending();
    }
    return batch.promise;
  }

  public flush(): Promise<AutosaveResult> | null {
    const pending = this.pending;
    if (pending === null) {
      return null;
    }
    this.cancelTimer();
    this.startPending();
    return pending.promise;
  }

  public retry(): Promise<AutosaveResult> {
    const snapshot = this.failedSnapshot;
    if (
      this.disposed ||
      snapshot === null ||
      this.state.status !== "failed" ||
      this.state.documentId !== snapshot.id ||
      this.state.revision !== snapshot.revision
    ) {
      return Promise.resolve({
        status: "canceled",
        revision: this.state.revision ?? 0,
      });
    }
    const batch = this.createBatch(structuredClone(snapshot));
    this.latestToken = batch.token;
    this.failedSnapshot = null;
    this.publish({
      status: "saving",
      documentId: snapshot.id,
      revision: snapshot.revision,
      error: null,
    });
    this.startBatch(batch);
    return batch.promise;
  }

  public dispose(): void {
    if (this.disposed) {
      return;
    }
    this.disposed = true;
    this.cancelTimer();
    if (this.pending !== null) {
      this.pending.resolve({
        status: "canceled",
        revision: this.pending.snapshot.revision,
      });
      this.pending = null;
    }
    this.listeners.clear();
  }

  private createBatch(snapshot: PersistedDocumentSnapshot): ScheduledBatch {
    let resolve!: (result: AutosaveResult) => void;
    const promise = new Promise<AutosaveResult>((settle) => {
      resolve = settle;
    });
    return { token: ++this.sequence, snapshot, promise, resolve };
  }

  private startPending(): void {
    const pending = this.pending;
    if (pending === null || this.disposed) {
      return;
    }
    this.pending = null;
    this.timer = null;
    this.startBatch(pending);
  }

  private startBatch(batch: ScheduledBatch): void {
    void this.persist(batch).then(batch.resolve);
  }

  private async persist(batch: ScheduledBatch): Promise<AutosaveResult> {
    const snapshot = structuredClone(batch.snapshot);
    try {
      await this.repository.saveJournal(snapshot);
      const outcome = await this.repository.save(snapshot);
      if (outcome.storedRevision >= snapshot.revision) {
        await this.repository.discardJournal(snapshot.id, {
          revision: snapshot.revision,
          data: snapshot.data,
        });
      }
      if (!this.disposed && batch.token === this.latestToken) {
        this.publish({
          status: "saved",
          documentId: snapshot.id,
          revision: snapshot.revision,
          error: null,
        });
      }
      return { status: "saved", revision: snapshot.revision };
    } catch (cause) {
      const error =
        cause instanceof DocumentRepositoryError
          ? cause
          : new DocumentRepositoryError(
              "transaction",
              `Unable to save document ${snapshot.id}`,
              { cause, details: { documentId: snapshot.id, revision: snapshot.revision } },
            );
      if (!this.disposed && batch.token === this.latestToken) {
        this.failedSnapshot = structuredClone(snapshot);
        this.publish({
          status: "failed",
          documentId: snapshot.id,
          revision: snapshot.revision,
          error,
        });
      }
      return { status: "failed", revision: snapshot.revision };
    }
  }

  private cancelTimer(): void {
    if (this.timer !== null) {
      this.scheduler.clear(this.timer);
      this.timer = null;
    }
  }

  private publish(state: AutosaveSnapshot): void {
    this.state = Object.freeze(state);
    for (const listener of [...this.listeners]) {
      listener();
    }
  }
}
