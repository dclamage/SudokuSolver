import type { PuzzlePackageV1 } from "../puzzle/types";
import type {
  DecodedPersistedDocument,
  PersistedDocumentRecord,
  PersistenceContentError,
} from "./migrations";

export type PersistedDocumentSnapshot = PersistedDocumentRecord;

export interface JournalIdentity {
  readonly revision: number;
  readonly data: string;
}

export type DocumentRepositoryErrorCode =
  | "unavailable"
  | "blocked"
  | "quota"
  | "aborted"
  | "transaction"
  | "parse"
  | "validation"
  | "unsupported-version";

export class DocumentRepositoryError extends Error {
  public constructor(
    public readonly code: DocumentRepositoryErrorCode,
    message: string,
    options?: ErrorOptions & { readonly details?: Readonly<Record<string, unknown>> },
  ) {
    super(message, options);
    this.name = "DocumentRepositoryError";
    this.details = options?.details;
  }

  public readonly details?: Readonly<Record<string, unknown>>;
}

export type LoadedDocument =
  | { readonly status: "missing" }
  | DecodedPersistedDocument
  | {
      readonly status: "invalid";
      readonly record: PersistedDocumentRecord;
      readonly error: PersistenceContentError;
    };

export interface RecentDocument {
  readonly id: string;
  readonly title: string;
  readonly schemaVersion: number;
  readonly revision: number;
  readonly updatedAt: string;
  readonly support: "supported" | "unsupported";
}

export type SaveDocumentOutcome =
  | { readonly status: "saved"; readonly storedRevision: number }
  | {
      readonly status: "ignored";
      readonly storedRevision: number;
      readonly reason: "not-newer" | "unsupported-existing";
    };

/** Local document storage boundary used by startup and autosave. */
export interface DocumentRepository {
  load(documentId: string): Promise<LoadedDocument>;
  save(snapshot: PersistedDocumentSnapshot): Promise<SaveDocumentOutcome>;
  listRecent(): Promise<readonly RecentDocument[]>;
  saveJournal(snapshot: PersistedDocumentSnapshot): Promise<void>;
  loadJournal(documentId: string): Promise<LoadedDocument>;
  discardJournal(
    documentId: string,
    identity: JournalIdentity,
  ): Promise<"discarded" | "ignored">;
  getSelectedDocumentId(): Promise<string | null>;
  setSelectedDocumentId(documentId: string): Promise<void>;
}

export function snapshotDocument(
  document: PuzzlePackageV1,
  updatedAt: string,
): PersistedDocumentSnapshot {
  const clone = structuredClone(document);
  return {
    id: clone.id,
    title: clone.metadata.title,
    schemaVersion: clone.schemaVersion,
    revision: clone.revision,
    updatedAt,
    data: JSON.stringify(clone),
  };
}
