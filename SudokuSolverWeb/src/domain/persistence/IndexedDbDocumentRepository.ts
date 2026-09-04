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
  CURRENT_PUZZLE_SCHEMA_VERSION,
  decodePersistedDocument,
  type PersistedDocumentRecord,
  PersistenceContentError,
} from "./migrations";

export const DOCUMENT_DATABASE_NAME = "sudoku-solver-web";
export const DOCUMENT_DATABASE_VERSION = 1;
export const DOCUMENT_DATABASE_STORES = [
  "documents",
  "journals",
  "sessions",
] as const;

export type PersistenceStoreName = (typeof DOCUMENT_DATABASE_STORES)[number];

export interface PersistenceTransaction {
  get<T>(storeName: PersistenceStoreName, key: string): Promise<T | undefined>;
  getAll<T>(storeName: PersistenceStoreName): Promise<T[]>;
  put(storeName: PersistenceStoreName, value: unknown): Promise<void>;
  delete(storeName: PersistenceStoreName, key: string): Promise<void>;
}

export interface PersistenceDatabase {
  transaction<T>(
    storeNames: readonly PersistenceStoreName[],
    mode: IDBTransactionMode,
    operation: (transaction: PersistenceTransaction) => Promise<T>,
  ): Promise<T>;
}

export interface PersistenceDatabaseFactory {
  open(
    name: string,
    version: number,
    stores: readonly PersistenceStoreName[],
  ): Promise<PersistenceDatabase>;
}

interface IndexedDbDocumentRepositoryOptions {
  readonly factory?: PersistenceDatabaseFactory;
  readonly indexedDb?: IDBFactory | null;
}

interface SessionRecord {
  readonly id: "current";
  readonly documentId: string;
}

class BrowserPersistenceTransaction implements PersistenceTransaction {
  public constructor(private readonly transaction: IDBTransaction) {}

  public async get<T>(
    storeName: PersistenceStoreName,
    key: string,
  ): Promise<T | undefined> {
    return requestResult<T | undefined>(
      this.transaction.objectStore(storeName).get(key),
    );
  }

  public async getAll<T>(storeName: PersistenceStoreName): Promise<T[]> {
    return requestResult<T[]>(this.transaction.objectStore(storeName).getAll());
  }

  public async put(storeName: PersistenceStoreName, value: unknown): Promise<void> {
    await requestResult(this.transaction.objectStore(storeName).put(value));
  }

  public async delete(storeName: PersistenceStoreName, key: string): Promise<void> {
    await requestResult(this.transaction.objectStore(storeName).delete(key));
  }
}

class BrowserPersistenceDatabase implements PersistenceDatabase {
  public constructor(private readonly database: IDBDatabase) {}

  public async transaction<T>(
    storeNames: readonly PersistenceStoreName[],
    mode: IDBTransactionMode,
    operation: (transaction: PersistenceTransaction) => Promise<T>,
  ): Promise<T> {
    const transaction = this.database.transaction([...storeNames], mode);
    const completion = transactionCompletion(transaction);
    try {
      const result = await operation(new BrowserPersistenceTransaction(transaction));
      await completion;
      return result;
    } catch (cause) {
      try {
        transaction.abort();
      } catch {
        // The transaction may already have completed or aborted.
      }
      await completion.catch(() => undefined);
      throw cause;
    }
  }
}

class BrowserPersistenceDatabaseFactory implements PersistenceDatabaseFactory {
  public constructor(private readonly indexedDb: IDBFactory) {}

  public open(
    name: string,
    version: number,
    stores: readonly PersistenceStoreName[],
  ): Promise<PersistenceDatabase> {
    return new Promise((resolve, reject) => {
      const request = this.indexedDb.open(name, version);
      let settled = false;
      request.onupgradeneeded = () => {
        for (const storeName of stores) {
          if (!request.result.objectStoreNames.contains(storeName)) {
            request.result.createObjectStore(storeName, { keyPath: "id" });
          }
        }
      };
      request.onblocked = () => {
        settled = true;
        reject(
          new DocumentRepositoryError(
            "blocked",
            "IndexedDB upgrade is blocked by another open application tab",
          ),
        );
      };
      request.onerror = () => {
        settled = true;
        reject(request.error ?? new Error("IndexedDB open failed"));
      };
      request.onsuccess = () => {
        if (settled) {
          request.result.close();
          return;
        }
        settled = true;
        resolve(new BrowserPersistenceDatabase(request.result));
      };
    });
  }
}

class UnavailableDatabaseFactory implements PersistenceDatabaseFactory {
  public async open(): Promise<PersistenceDatabase> {
    throw new DocumentRepositoryError(
      "unavailable",
      "IndexedDB is not available in this browser",
    );
  }
}

/** IndexedDB-backed revision-safe local document repository. */
export class IndexedDbDocumentRepository implements DocumentRepository {
  private readonly factory: PersistenceDatabaseFactory;
  private databasePromise: Promise<PersistenceDatabase> | null = null;

  public constructor(options: IndexedDbDocumentRepositoryOptions = {}) {
    if (options.factory !== undefined) {
      this.factory = options.factory;
      return;
    }
    const indexedDb =
      options.indexedDb === undefined ? globalThis.indexedDB : options.indexedDb;
    this.factory =
      indexedDb === null || indexedDb === undefined
        ? new UnavailableDatabaseFactory()
        : new BrowserPersistenceDatabaseFactory(indexedDb);
  }

  public async load(documentId: string): Promise<LoadedDocument> {
    return this.loadFromStore("documents", documentId);
  }

  public async save(
    snapshot: PersistedDocumentSnapshot,
  ): Promise<SaveDocumentOutcome> {
    assertValidRecord(snapshot);
    return this.run(["documents"], "readwrite", async (transaction) => {
      const storedValue = await transaction.get<unknown>("documents", snapshot.id);
      if (storedValue !== undefined) {
        const stored = parseRecord(storedValue);
        if (stored.schemaVersion > CURRENT_PUZZLE_SCHEMA_VERSION) {
          return {
            status: "ignored",
            storedRevision: stored.revision,
            reason: "unsupported-existing",
          };
        }
        if (snapshot.revision <= stored.revision) {
          return {
            status: "ignored",
            storedRevision: stored.revision,
            reason: "not-newer",
          };
        }
      }
      await transaction.put("documents", structuredClone(snapshot));
      return { status: "saved", storedRevision: snapshot.revision };
    });
  }

  public async listRecent(): Promise<readonly RecentDocument[]> {
    const records = await this.run(["documents"], "readonly", (transaction) =>
      transaction.getAll<unknown>("documents"),
    );
    return records
      .map(parseRecord)
      .map((record): RecentDocument => ({
        id: record.id,
        title: record.title,
        schemaVersion: record.schemaVersion,
        revision: record.revision,
        updatedAt: record.updatedAt,
        support:
          record.schemaVersion === CURRENT_PUZZLE_SCHEMA_VERSION
            ? "supported"
            : "unsupported",
      }))
      .sort((left, right) => right.updatedAt.localeCompare(left.updatedAt));
  }

  public async saveJournal(snapshot: PersistedDocumentSnapshot): Promise<void> {
    assertValidRecord(snapshot);
    await this.run(["journals"], "readwrite", async (transaction) => {
      await transaction.put("journals", structuredClone(snapshot));
    });
  }

  public async loadJournal(documentId: string): Promise<LoadedDocument> {
    return this.loadFromStore("journals", documentId);
  }

  public async discardJournal(
    documentId: string,
    identity: JournalIdentity,
  ): Promise<"discarded" | "ignored"> {
    return this.run(["journals"], "readwrite", async (transaction) => {
      const value = await transaction.get<unknown>("journals", documentId);
      if (value === undefined) {
        return "ignored";
      }
      const record = parseRecord(value);
      if (record.revision !== identity.revision || record.data !== identity.data) {
        return "ignored";
      }
      await transaction.delete("journals", documentId);
      return "discarded";
    });
  }

  public async getSelectedDocumentId(): Promise<string | null> {
    return this.run(["sessions"], "readonly", async (transaction) => {
      const value = await transaction.get<unknown>("sessions", "current");
      if (value === undefined) {
        return null;
      }
      if (
        typeof value !== "object" ||
        value === null ||
        !("documentId" in value) ||
        typeof value.documentId !== "string"
      ) {
        throw new DocumentRepositoryError(
          "validation",
          "The current document session record is invalid",
        );
      }
      return value.documentId;
    });
  }

  public async setSelectedDocumentId(documentId: string): Promise<void> {
    await this.run(["sessions"], "readwrite", async (transaction) => {
      const session: SessionRecord = { id: "current", documentId };
      await transaction.put("sessions", session);
    });
  }

  private async loadFromStore(
    storeName: "documents" | "journals",
    documentId: string,
  ): Promise<LoadedDocument> {
    const value = await this.run([storeName], "readonly", (transaction) =>
      transaction.get<unknown>(storeName, documentId),
    );
    if (value === undefined) {
      return { status: "missing" };
    }
    let record: PersistedDocumentRecord;
    try {
      record = parseRecord(value);
      return decodePersistedDocument(record);
    } catch (cause) {
      if (cause instanceof PersistenceContentError) {
        const fallback = isRecordLike(value)
          ? coerceInvalidRecord(value, documentId)
          : invalidFallbackRecord(documentId);
        return { status: "invalid", record: fallback, error: cause };
      }
      throw cause;
    }
  }

  private async run<T>(
    stores: readonly PersistenceStoreName[],
    mode: IDBTransactionMode,
    operation: (transaction: PersistenceTransaction) => Promise<T>,
  ): Promise<T> {
    try {
      const database = await this.getDatabase();
      return await database.transaction(stores, mode, operation);
    } catch (cause) {
      throw mapRepositoryError(cause, mode === "readwrite" ? "transaction" : "transaction");
    }
  }

  private getDatabase(): Promise<PersistenceDatabase> {
    this.databasePromise ??= this.factory
      .open(
        DOCUMENT_DATABASE_NAME,
        DOCUMENT_DATABASE_VERSION,
        DOCUMENT_DATABASE_STORES,
      )
      .catch((cause) => {
        this.databasePromise = null;
        throw mapRepositoryError(cause, "blocked");
      });
    return this.databasePromise;
  }
}

function requestResult<T>(request: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error("IndexedDB request failed"));
  });
}

function transactionCompletion(transaction: IDBTransaction): Promise<void> {
  return new Promise((resolve, reject) => {
    transaction.oncomplete = () => resolve();
    transaction.onabort = () =>
      reject(transaction.error ?? new DOMException("Transaction aborted", "AbortError"));
    transaction.onerror = () =>
      reject(transaction.error ?? new Error("IndexedDB transaction failed"));
  });
}

function mapRepositoryError(
  cause: unknown,
  fallback: "blocked" | "transaction",
): DocumentRepositoryError {
  if (cause instanceof DocumentRepositoryError) {
    return cause;
  }
  if (cause instanceof PersistenceContentError) {
    return new DocumentRepositoryError(cause.code, cause.message, {
      cause,
      details: cause.details,
    });
  }
  const name = cause instanceof DOMException ? cause.name : "";
  const code =
    name === "QuotaExceededError"
      ? "quota"
      : name === "AbortError"
        ? "aborted"
        : name === "VersionError"
          ? "blocked"
          : fallback;
  return new DocumentRepositoryError(code, storageErrorMessage(code), { cause });
}

function storageErrorMessage(code: DocumentRepositoryError["code"]): string {
  switch (code) {
    case "quota":
      return "Local storage quota was exceeded";
    case "aborted":
      return "The IndexedDB transaction was aborted";
    case "blocked":
      return "IndexedDB could not open because an upgrade is blocked";
    default:
      return "The local document transaction failed";
  }
}

function parseRecord(value: unknown): PersistedDocumentRecord {
  if (!isRecordLike(value)) {
    throw new PersistenceContentError("validation", "Persistence record must be an object");
  }
  const record = value as unknown as PersistedDocumentRecord;
  assertValidRecord(record);
  return structuredClone(record);
}

function assertValidRecord(record: PersistedDocumentRecord): void {
  if (
    typeof record.id !== "string" ||
    record.id.length === 0 ||
    typeof record.title !== "string" ||
    record.title.length === 0 ||
    !Number.isSafeInteger(record.schemaVersion) ||
    record.schemaVersion < 1 ||
    !Number.isSafeInteger(record.revision) ||
    record.revision < 0 ||
    typeof record.data !== "string" ||
    !isUtcIsoTimestamp(record.updatedAt)
  ) {
    throw new PersistenceContentError(
      "validation",
      `Persistence record ${String(record.id)} has invalid metadata`,
    );
  }
}

function isUtcIsoTimestamp(value: unknown): value is string {
  if (typeof value !== "string") {
    return false;
  }
  const milliseconds = Date.parse(value);
  return Number.isFinite(milliseconds) && new Date(milliseconds).toISOString() === value;
}

function isRecordLike(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function coerceInvalidRecord(
  value: Record<string, unknown>,
  documentId: string,
): PersistedDocumentRecord {
  return {
    id: typeof value.id === "string" ? value.id : documentId,
    title: typeof value.title === "string" ? value.title : "Unreadable document",
    schemaVersion:
      typeof value.schemaVersion === "number" ? value.schemaVersion : 1,
    revision: typeof value.revision === "number" ? value.revision : 0,
    updatedAt:
      typeof value.updatedAt === "string"
        ? value.updatedAt
        : "1970-01-01T00:00:00.000Z",
    data: typeof value.data === "string" ? value.data : "",
  };
}

function invalidFallbackRecord(documentId: string): PersistedDocumentRecord {
  return {
    id: documentId,
    title: "Unreadable document",
    schemaVersion: 1,
    revision: 0,
    updatedAt: "1970-01-01T00:00:00.000Z",
    data: "",
  };
}
