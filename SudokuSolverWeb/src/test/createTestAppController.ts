import { AppController } from "../app/AppController";
import { createStarterPuzzle } from "../domain/puzzle/createStarterPuzzle";
import { PuzzleStore } from "../domain/puzzle/PuzzleStore";
import type { PuzzlePackageV1 } from "../domain/puzzle/types";
import { FakeSolverClient } from "./FakeSolverClient";

export class InMemoryPuzzlePersistence {
  public current: PuzzlePackageV1;
  public readonly revisions: number[] = [];

  public constructor(initial: PuzzlePackageV1) {
    this.current = structuredClone(initial);
  }

  public readonly save = (document: PuzzlePackageV1): void => {
    this.current = structuredClone(document);
    this.revisions.push(document.revision);
  };
}

export interface TestClock {
  readonly now: () => number;
  advance(milliseconds: number): void;
}

export interface TestAppDependencies {
  readonly solver: FakeSolverClient;
  readonly persistence: InMemoryPuzzlePersistence;
  readonly clock: TestClock;
  readonly requestIds: readonly string[];
}

export type TestAppController = AppController & {
  readonly testDependencies: TestAppDependencies;
};

export function createTestAppController(): TestAppController {
  const starter = createStarterPuzzle(() => "test-puzzle");
  const puzzle = new PuzzleStore(starter);
  const solver = new FakeSolverClient();
  const persistence = new InMemoryPuzzlePersistence(starter);
  const requestIds: string[] = [];
  let now = Date.UTC(2026, 8, 2, 12, 0, 0);
  const clock: TestClock = {
    now: () => now,
    advance: (milliseconds) => {
      now += milliseconds;
    },
  };
  let requestNumber = 0;
  const controller = new AppController({
    puzzle,
    solver,
    now: clock.now,
    persist: persistence.save,
    createRequestId: () => {
      requestNumber += 1;
      const requestId = `test-request-${requestNumber}`;
      requestIds.push(requestId);
      return requestId;
    },
  });
  return Object.assign(controller, {
    testDependencies: {
      solver,
      persistence,
      clock,
      requestIds,
    },
  });
}
