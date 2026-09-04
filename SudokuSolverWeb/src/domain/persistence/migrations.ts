import type { PuzzlePackageV1 } from "../puzzle/types";
import { validatePuzzlePackage } from "../puzzle/validatePuzzlePackage";

export const CURRENT_PUZZLE_SCHEMA_VERSION = 1;

export interface PersistedDocumentRecord {
  readonly id: string;
  readonly title: string;
  readonly schemaVersion: number;
  readonly revision: number;
  readonly updatedAt: string;
  readonly data: string;
}

export type PersistenceContentErrorCode =
  | "parse"
  | "validation"
  | "unsupported-version";

export class PersistenceContentError extends Error {
  public constructor(
    public readonly code: PersistenceContentErrorCode,
    message: string,
    options?: ErrorOptions & { readonly details?: Readonly<Record<string, unknown>> },
  ) {
    super(message, options);
    this.name = "PersistenceContentError";
    this.details = options?.details;
  }

  public readonly details?: Readonly<Record<string, unknown>>;
}

export type DecodedPersistedDocument =
  | {
      readonly status: "supported";
      readonly record: PersistedDocumentRecord;
      readonly document: PuzzlePackageV1;
    }
  | {
      readonly status: "unsupported";
      readonly record: PersistedDocumentRecord;
      readonly error: PersistenceContentError;
    };

type Migration = (value: unknown) => PuzzlePackageV1;

const migrations: Readonly<Record<number, Migration>> = Object.freeze({
  1: validatePuzzlePackage,
});

/**
 * Parses and migrates one persisted package through the authoritative
 * frontend validator. Future-version records are returned untouched so a
 * newer application can still recover their original serialized payload.
 */
export function decodePersistedDocument(
  record: PersistedDocumentRecord,
): DecodedPersistedDocument {
  if (record.schemaVersion > CURRENT_PUZZLE_SCHEMA_VERSION) {
    return {
      status: "unsupported",
      record,
      error: new PersistenceContentError(
        "unsupported-version",
        `Puzzle schema version ${record.schemaVersion} is newer than supported version ${CURRENT_PUZZLE_SCHEMA_VERSION}`,
        { details: { schemaVersion: record.schemaVersion } },
      ),
    };
  }

  const migrate = migrations[record.schemaVersion];
  if (migrate === undefined) {
    throw new PersistenceContentError(
      "unsupported-version",
      `Puzzle schema version ${record.schemaVersion} is not supported`,
      { details: { schemaVersion: record.schemaVersion } },
    );
  }

  let value: unknown;
  try {
    value = JSON.parse(record.data);
  } catch (cause) {
    throw new PersistenceContentError(
      "parse",
      `Document ${record.id} contains malformed JSON`,
      { cause, details: { documentId: record.id } },
    );
  }

  let document: PuzzlePackageV1;
  try {
    document = migrate(value);
  } catch (cause) {
    throw new PersistenceContentError(
      "validation",
      `Document ${record.id} failed schema validation`,
      { cause, details: { documentId: record.id } },
    );
  }

  if (
    document.id !== record.id ||
    document.schemaVersion !== record.schemaVersion ||
    document.revision !== record.revision
  ) {
    throw new PersistenceContentError(
      "validation",
      `Document ${record.id} does not match its persistence envelope`,
      {
        details: {
          documentId: record.id,
          packageId: document.id,
          revision: record.revision,
          packageRevision: document.revision,
        },
      },
    );
  }

  return { status: "supported", record, document };
}
