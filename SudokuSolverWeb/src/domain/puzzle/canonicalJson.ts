import type { JsonValue } from "./types";

function normalize(value: JsonValue): JsonValue {
  if (
    value === null ||
    typeof value === "boolean" ||
    typeof value === "string"
  ) {
    return value;
  }
  if (typeof value === "number") {
    if (!Number.isSafeInteger(value)) {
      throw new Error("canonical semantic JSON numbers must be finite safe integers");
    }
    return value;
  }
  if (Array.isArray(value)) {
    return value.map(normalize);
  }

  const record = value as Readonly<Record<string, JsonValue>>;
  const normalized: Record<string, JsonValue> = {};
  for (const key of Object.keys(record).sort()) {
    normalized[key] = normalize(record[key]);
  }
  return normalized;
}

export function canonicalJson(value: JsonValue): string {
  return JSON.stringify(normalize(value));
}
