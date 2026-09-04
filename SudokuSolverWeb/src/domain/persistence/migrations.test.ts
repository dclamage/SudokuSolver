import { describe, expect, it } from "vitest";

import { createStarterPuzzle } from "../puzzle/createStarterPuzzle";
import {
  decodePersistedDocument,
  PersistenceContentError,
  type PersistedDocumentRecord,
} from "./migrations";

function recordWith(data: string): PersistedDocumentRecord {
  return {
    id: "migration-test",
    title: "Migration test",
    schemaVersion: 1,
    revision: 1,
    updatedAt: "2026-09-04T12:00:00.000Z",
    data,
  };
}

describe("decodePersistedDocument", () => {
  it("validates and clones a current v1 package", () => {
    const document = createStarterPuzzle(() => "migration-test");

    const result = decodePersistedDocument(recordWith(JSON.stringify(document)));

    expect(result.status).toBe("supported");
    if (result.status === "supported") {
      expect(result.document).toEqual(document);
      expect(result.document).not.toBe(document);
    }
  });

  it("distinguishes malformed JSON from validation failure", () => {
    expect(() => decodePersistedDocument(recordWith("{not-json"))).toThrow(
      expect.objectContaining({ code: "parse" }),
    );

    const invalid = createStarterPuzzle(() => "migration-test");
    invalid.metadata.title = "";
    expect(() =>
      decodePersistedDocument(recordWith(JSON.stringify(invalid))),
    ).toThrow(expect.objectContaining({ code: "validation" }));
  });

  it("preserves unsupported future data exactly without validating it", () => {
    const rawData = '{"schemaVersion":2,"id":"migration-test","newField":true}';
    const record = { ...recordWith(rawData), schemaVersion: 2 };

    const result = decodePersistedDocument(record);

    expect(result).toEqual({
      status: "unsupported",
      record,
      error: expect.objectContaining({
        code: "unsupported-version",
        details: { schemaVersion: 2 },
      }),
    });
    expect(result.record.data).toBe(rawData);
  });

  it("rejects records whose envelope does not match the validated package", () => {
    const document = createStarterPuzzle(() => "different-id");

    expect(() =>
      decodePersistedDocument(recordWith(JSON.stringify(document))),
    ).toThrow(PersistenceContentError);
    expect(() =>
      decodePersistedDocument(recordWith(JSON.stringify(document))),
    ).toThrow(expect.objectContaining({ code: "validation" }));
  });
});
