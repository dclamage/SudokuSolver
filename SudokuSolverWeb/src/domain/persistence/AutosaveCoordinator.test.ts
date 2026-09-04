import { describe, expect, it } from "vitest";

import { createStarterPuzzle } from "../puzzle/createStarterPuzzle";
import type { PuzzlePackageV1 } from "../puzzle/types";
import {
  type DocumentRepository,
  DocumentRepositoryError,
  type JournalIdentity,
  type LoadedDocument,
  type PersistedDocumentSnapshot,
  type RecentDocument,
  type SaveDocumentOutcome,
} from "./DocumentRepository";
import {
  AutosaveCoordinator,
  type AutosaveScheduler,
} from "./AutosaveCoordinator";

function documentAtRevision(revision: number): PuzzlePackageV1 {
  const document = createStarterPuzzle(() => "autosave-document");
  document.revision = revision;
  document.semanticRevision = Math.min(document.semanticRevision, revision);
  document.metadata.title = `Revision ${revision}`;
  return document;
}

type SaveResolution =
  | { readonly kind: "resolve" }
  | { readonly kind: "reject"; readonly error: DocumentRepositoryError };

class DeferredRepository implements DocumentRepository {
  public current: PersistedDocumentSnapshot | null = null;
  public journal: PersistedDocumentSnapshot | null = null;
  public readonly savedInputs: PersistedDocumentSnapshot[] = [];
  public readonly discarded: JournalIdentity[] = [];

  private readonly pending = new Map<
    number,
    {
      readonly snapshot: PersistedDocumentSnapshot;
      readonly resolve: (outcome: SaveDocumentOutcome) => void;
      readonly reject: (error: DocumentRepositoryError) => void;
    }
  >();
  private readonly early = new Map<number, SaveResolution>();

  public async load(): Promise<LoadedDocument> {
    return { status: "missing" };
  }

  public async save(
    snapshot: PersistedDocumentSnapshot,
  ): Promise<SaveDocumentOutcome> {
    const clone = structuredClone(snapshot);
    this.savedInputs.push(clone);
    const promise = new Promise<SaveDocumentOutcome>((resolve, reject) => {
      this.pending.set(snapshot.revision, { snapshot: clone, resolve, reject });
    });
    const early = this.early.get(snapshot.revision);
    if (early !== undefined) {
      this.early.delete(snapshot.revision);
      this.settle(snapshot.revision, early);
    }
    return promise;
  }

  public async listRecent(): Promise<readonly RecentDocument[]> {
    return [];
  }

  public async saveJournal(snapshot: PersistedDocumentSnapshot): Promise<void> {
    this.journal = structuredClone(snapshot);
  }

  public async loadJournal(): Promise<LoadedDocument> {
    return { status: "missing" };
  }

  public async discardJournal(
    _documentId: string,
    identity: JournalIdentity,
  ): Promise<"discarded" | "ignored"> {
    this.discarded.push(structuredClone(identity));
    if (
      this.journal?.revision !== identity.revision ||
      this.journal.data !== identity.data
    ) {
      return "ignored";
    }
    this.journal = null;
    return "discarded";
  }

  public async getSelectedDocumentId(): Promise<string | null> {
    return null;
  }

  public async setSelectedDocumentId(): Promise<void> {}

  public resolveSave(revision: number): void {
    this.requestSettlement(revision, { kind: "resolve" });
  }

  public rejectSave(revision: number, error: DocumentRepositoryError): void {
    this.requestSettlement(revision, { kind: "reject", error });
  }

  private requestSettlement(revision: number, resolution: SaveResolution): void {
    if (!this.pending.has(revision)) {
      this.early.set(revision, resolution);
      return;
    }
    this.settle(revision, resolution);
  }

  private settle(revision: number, resolution: SaveResolution): void {
    const pending = this.pending.get(revision);
    if (pending === undefined) {
      throw new Error(`No save pending for revision ${revision}`);
    }
    this.pending.delete(revision);
    if (resolution.kind === "reject") {
      pending.reject(resolution.error);
      return;
    }
    if (
      this.current === null ||
      pending.snapshot.revision > this.current.revision
    ) {
      this.current = structuredClone(pending.snapshot);
      pending.resolve({ status: "saved", storedRevision: revision });
    } else {
      pending.resolve({
        status: "ignored",
        storedRevision: this.current.revision,
        reason: "not-newer",
      });
    }
  }
}

class ManualScheduler implements AutosaveScheduler {
  private callback: (() => void) | null = null;
  public lastDelay: number | null = null;

  public set(callback: () => void, delayMs: number): object {
    this.callback = callback;
    this.lastDelay = delayMs;
    return {};
  }

  public clear(): void {
    this.callback = null;
  }

  public run(): void {
    const callback = this.callback;
    this.callback = null;
    callback?.();
  }
}

describe("AutosaveCoordinator", () => {
  it("never lets an older save overwrite a newer revision", async () => {
    const repository = new DeferredRepository();
    const coordinator = new AutosaveCoordinator(repository);
    const first = coordinator.schedule(documentAtRevision(2));
    const second = coordinator.schedule(documentAtRevision(3));
    repository.resolveSave(3);
    repository.resolveSave(2);
    await Promise.all([first, second]);
    expect(repository.current?.revision).toBe(3);
  });

  it("keeps a journal until save succeeds", async () => {
    const repository = new DeferredRepository();
    const coordinator = new AutosaveCoordinator(repository);
    const pending = coordinator.schedule(documentAtRevision(2));
    expect(repository.journal?.revision).toBe(2);
    repository.resolveSave(2);
    await pending;
    expect(repository.journal).toBeNull();
  });

  it("does not let an old completion clear a newer journal", async () => {
    const repository = new DeferredRepository();
    const coordinator = new AutosaveCoordinator(repository);
    const first = coordinator.schedule(documentAtRevision(2));
    const second = coordinator.schedule(documentAtRevision(3));

    repository.resolveSave(2);
    await first;
    expect(repository.journal?.revision).toBe(3);

    repository.resolveSave(3);
    await second;
    expect(repository.journal).toBeNull();
  });

  it("retains a failed journal and permits a retry", async () => {
    const repository = new DeferredRepository();
    const coordinator = new AutosaveCoordinator(repository);
    const failed = coordinator.schedule(documentAtRevision(4));
    repository.rejectSave(
      4,
      new DocumentRepositoryError("quota", "Storage quota exceeded"),
    );

    await expect(failed).resolves.toEqual({ status: "failed", revision: 4 });
    expect(repository.journal?.revision).toBe(4);
    expect(coordinator.getSnapshot()).toMatchObject({
      status: "failed",
      documentId: "autosave-document",
      revision: 4,
      error: expect.objectContaining({ code: "quota" }),
    });

    const retry = coordinator.retry();
    repository.resolveSave(4);
    await retry;
    expect(repository.journal).toBeNull();
    expect(coordinator.getSnapshot()).toMatchObject({
      status: "saved",
      revision: 4,
    });
  });

  it("clones snapshots before crossing the repository boundary", async () => {
    const repository = new DeferredRepository();
    const coordinator = new AutosaveCoordinator(repository);
    const document = documentAtRevision(2);
    const pending = coordinator.schedule(document);

    document.metadata.title = "Mutated after schedule";
    expect(JSON.parse(repository.journal?.data ?? "null").metadata.title).toBe(
      "Revision 2",
    );

    repository.resolveSave(2);
    await pending;
    expect(JSON.parse(repository.current?.data ?? "null").metadata.title).toBe(
      "Revision 2",
    );
  });

  it("coalesces normal debounce schedules at 300 ms", async () => {
    const repository = new DeferredRepository();
    const scheduler = new ManualScheduler();
    const coordinator = new AutosaveCoordinator(repository, {
      debounceMs: 300,
      scheduler,
    });

    const first = coordinator.schedule(documentAtRevision(2));
    const second = coordinator.schedule(documentAtRevision(3));
    expect(scheduler.lastDelay).toBe(300);
    expect(repository.journal).toBeNull();

    scheduler.run();
    repository.resolveSave(3);
    await expect(Promise.all([first, second])).resolves.toEqual([
      { status: "superseded", revision: 2 },
      { status: "saved", revision: 3 },
    ]);
    expect(repository.savedInputs.map((item) => item.revision)).toEqual([3]);
  });

  it("does not let a late older completion regress status", async () => {
    const repository = new DeferredRepository();
    const coordinator = new AutosaveCoordinator(repository);
    const first = coordinator.schedule(documentAtRevision(2));
    const second = coordinator.schedule(documentAtRevision(3));

    repository.resolveSave(3);
    await second;
    expect(coordinator.getSnapshot()).toMatchObject({ status: "saved", revision: 3 });

    repository.resolveSave(2);
    await first;
    expect(coordinator.getSnapshot()).toMatchObject({ status: "saved", revision: 3 });
  });

  it("dispose cancels pending timers without discarding recovery data", async () => {
    const repository = new DeferredRepository();
    const scheduler = new ManualScheduler();
    const coordinator = new AutosaveCoordinator(repository, {
      debounceMs: 300,
      scheduler,
    });
    const pending = coordinator.schedule(documentAtRevision(2));

    coordinator.dispose();
    scheduler.run();

    await expect(pending).resolves.toEqual({ status: "canceled", revision: 2 });
    expect(repository.savedInputs).toEqual([]);
    expect(repository.discarded).toEqual([]);
  });
});
