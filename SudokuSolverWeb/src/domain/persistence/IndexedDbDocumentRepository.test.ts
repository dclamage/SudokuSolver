import { describe, expect, it } from "vitest";

import { createStarterPuzzle } from "../puzzle/createStarterPuzzle";
import { snapshotDocument } from "./DocumentRepository";
import {
  DOCUMENT_DATABASE_NAME,
  DOCUMENT_DATABASE_STORES,
  DOCUMENT_DATABASE_VERSION,
  IndexedDbDocumentRepository,
  type PersistenceDatabase,
  type PersistenceDatabaseFactory,
  type PersistenceStoreName,
  type PersistenceTransaction,
} from "./IndexedDbDocumentRepository";
import type { PersistedDocumentRecord } from "./migrations";

class MemoryDatabase implements PersistenceDatabase {
  public readonly stores = new Map<PersistenceStoreName, Map<string, unknown>>(
    DOCUMENT_DATABASE_STORES.map((store) => [store, new Map()]),
  );
  public failure: Error | null = null;

  public async transaction<T>(
    storeNames: readonly PersistenceStoreName[],
    mode: IDBTransactionMode,
    operation: (transaction: PersistenceTransaction) => Promise<T>,
  ): Promise<T> {
    if (this.failure !== null) {
      throw this.failure;
    }
    const working = new Map(
      [...this.stores].map(([name, store]) => [name, new Map(store)]),
    );
    const source = mode === "readwrite" ? working : this.stores;
    const transaction: PersistenceTransaction = {
      get: async <TValue>(storeName: PersistenceStoreName, key: string) =>
        structuredClone(source.get(storeName)?.get(key) as TValue | undefined),
      getAll: async <TValue>(storeName: PersistenceStoreName) =>
        [...(source.get(storeName)?.values() ?? [])].map((value) =>
          structuredClone(value),
        ) as TValue[],
      put: async (storeName, value) => {
        const record = value as { id: string };
        source.get(storeName)?.set(record.id, structuredClone(value));
      },
      delete: async (storeName, key) => {
        source.get(storeName)?.delete(key);
      },
    };
    for (const storeName of storeNames) {
      if (!source.has(storeName)) {
        throw new Error(`missing store ${storeName}`);
      }
    }
    const result = await operation(transaction);
    if (mode === "readwrite") {
      for (const storeName of storeNames) {
        this.stores.set(storeName, working.get(storeName) ?? new Map());
      }
    }
    return result;
  }
}

class MemoryFactory implements PersistenceDatabaseFactory {
  public readonly database = new MemoryDatabase();
  public openArguments: {
    name: string;
    version: number;
    stores: readonly PersistenceStoreName[];
  } | null = null;
  public failure: Error | null = null;

  public async open(
    name: string,
    version: number,
    stores: readonly PersistenceStoreName[],
  ): Promise<PersistenceDatabase> {
    this.openArguments = { name, version, stores };
    if (this.failure !== null) {
      throw this.failure;
    }
    return this.database;
  }
}

function documentAt(id: string, revision: number, title = id) {
  const document = createStarterPuzzle(() => id);
  document.revision = revision;
  document.semanticRevision = Math.min(document.semanticRevision, revision);
  document.metadata.title = title;
  return document;
}

describe("IndexedDbDocumentRepository", () => {
  it("opens the exact database and creates all v1 stores", async () => {
    const factory = new MemoryFactory();
    const repository = new IndexedDbDocumentRepository({ factory });

    await repository.listRecent();

    expect(factory.openArguments).toEqual({
      name: DOCUMENT_DATABASE_NAME,
      version: DOCUMENT_DATABASE_VERSION,
      stores: DOCUMENT_DATABASE_STORES,
    });
  });

  it("conditionally saves only strictly newer revisions and keeps freshness metadata", async () => {
    const factory = new MemoryFactory();
    const repository = new IndexedDbDocumentRepository({ factory });
    const revision3 = snapshotDocument(
      documentAt("ordered", 3, "Newest"),
      "2026-09-04T12:03:00.000Z",
    );
    const revision2 = snapshotDocument(
      documentAt("ordered", 2, "Old"),
      "2026-09-04T12:04:00.000Z",
    );

    await expect(repository.save(revision3)).resolves.toEqual({
      status: "saved",
      storedRevision: 3,
    });
    await expect(repository.save(revision2)).resolves.toEqual({
      status: "ignored",
      storedRevision: 3,
      reason: "not-newer",
    });

    const loaded = await repository.load("ordered");
    expect(loaded.status).toBe("supported");
    if (loaded.status === "supported") {
      expect(loaded.document.metadata.title).toBe("Newest");
      expect(loaded.record.updatedAt).toBe("2026-09-04T12:03:00.000Z");
    }
  });

  it("orders recent metadata without returning serialized payloads", async () => {
    const factory = new MemoryFactory();
    const repository = new IndexedDbDocumentRepository({ factory });
    await repository.save(
      snapshotDocument(documentAt("older", 1, "Older"), "2026-09-04T11:00:00.000Z"),
    );
    await repository.save(
      snapshotDocument(documentAt("newer", 2, "Newer"), "2026-09-04T12:00:00.000Z"),
    );

    const recent = await repository.listRecent();

    expect(recent.map((item) => item.id)).toEqual(["newer", "older"]);
    expect(recent[0]).not.toHaveProperty("data");
  });

  it("discards a journal only when revision and serialized identity match", async () => {
    const factory = new MemoryFactory();
    const repository = new IndexedDbDocumentRepository({ factory });
    const journal = snapshotDocument(
      documentAt("journaled", 4),
      "2026-09-04T12:00:00.000Z",
    );
    await repository.saveJournal(journal);

    await expect(
      repository.discardJournal("journaled", { revision: 3, data: journal.data }),
    ).resolves.toBe("ignored");
    await expect(repository.loadJournal("journaled")).resolves.toMatchObject({
      status: "supported",
      record: { revision: 4 },
    });
    await expect(
      repository.discardJournal("journaled", {
        revision: 4,
        data: `${journal.data} `,
      }),
    ).resolves.toBe("ignored");
    await expect(
      repository.discardJournal("journaled", {
        revision: 4,
        data: journal.data,
      }),
    ).resolves.toBe("discarded");
    await expect(repository.loadJournal("journaled")).resolves.toEqual({
      status: "missing",
    });
  });

  it("preserves and reports a future-schema record without overwriting it", async () => {
    const factory = new MemoryFactory();
    const rawData = '{"schemaVersion":9,"id":"future","opaque":{"x":1}}';
    const future: PersistedDocumentRecord = {
      id: "future",
      title: "Future puzzle",
      schemaVersion: 9,
      revision: 2,
      updatedAt: "2026-09-04T12:00:00.000Z",
      data: rawData,
    };
    factory.database.stores.get("documents")?.set("future", future);
    const repository = new IndexedDbDocumentRepository({ factory });

    await expect(repository.load("future")).resolves.toMatchObject({
      status: "unsupported",
      record: { data: rawData },
    });
    await expect(
      repository.save(
        snapshotDocument(documentAt("future", 8), "2026-09-04T13:00:00.000Z"),
      ),
    ).resolves.toEqual({
      status: "ignored",
      storedRevision: 2,
      reason: "unsupported-existing",
    });
    expect(
      (factory.database.stores.get("documents")?.get("future") as PersistedDocumentRecord)
        .data,
    ).toBe(rawData);
  });

  it("returns invalid content as a typed load result", async () => {
    const factory = new MemoryFactory();
    const malformed: PersistedDocumentRecord = {
      id: "broken",
      title: "Broken",
      schemaVersion: 1,
      revision: 1,
      updatedAt: "2026-09-04T12:00:00.000Z",
      data: "{bad-json",
    };
    factory.database.stores.get("documents")?.set("broken", malformed);
    const repository = new IndexedDbDocumentRepository({ factory });

    await expect(repository.load("broken")).resolves.toMatchObject({
      status: "invalid",
      error: { code: "parse" },
    });
  });

  it.each([
    ["QuotaExceededError", "quota"],
    ["AbortError", "aborted"],
  ])("maps %s transaction failures to %s", async (name, code) => {
    const factory = new MemoryFactory();
    factory.database.failure = new DOMException("failed", name);
    const repository = new IndexedDbDocumentRepository({ factory });

    await expect(
      repository.save(
        snapshotDocument(documentAt("failed", 2), "2026-09-04T12:00:00.000Z"),
      ),
    ).rejects.toMatchObject({ code });
  });

  it("surfaces unavailable and blocked database opens with stable codes", async () => {
    await expect(
      new IndexedDbDocumentRepository({ indexedDb: null }).listRecent(),
    ).rejects.toMatchObject({ code: "unavailable" });

    const factory = new MemoryFactory();
    factory.failure = new DOMException("upgrade blocked", "VersionError");
    const repository = new IndexedDbDocumentRepository({ factory });
    await expect(repository.listRecent()).rejects.toMatchObject({ code: "blocked" });
  });
});
