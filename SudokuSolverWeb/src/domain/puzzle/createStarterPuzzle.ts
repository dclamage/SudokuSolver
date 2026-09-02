import fixture from "../../../../test-fixtures/native/classic-with-auxiliary.json";

import type { PuzzlePackageV1 } from "./types";
import { validatePuzzlePackage } from "./validatePuzzlePackage";

export function createStarterPuzzle(
  createId: () => string = () => crypto.randomUUID(),
): PuzzlePackageV1 {
  const puzzle = structuredClone(validatePuzzlePackage(fixture));
  puzzle.id = createId();
  puzzle.metadata.title = "Untitled puzzle";
  return puzzle;
}
