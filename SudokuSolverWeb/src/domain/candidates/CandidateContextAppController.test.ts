import { describe, expect, it, vi } from "vitest";

import { createTestAppController } from "../../test/createTestAppController";

describe("AppController candidate contexts", () => {
  it("does not replay stale automatic configuration when an earlier hash resolves", async () => {
    const digestResolvers: Array<(value: ArrayBuffer) => void> = [];
    const digestSpy = vi
      .spyOn(crypto.subtle, "digest")
      .mockImplementation(
        () =>
          new Promise<ArrayBuffer>((resolve) => {
            digestResolvers.push(resolve);
          }),
      );
    const controller = createTestAppController();
    try {
      expect(digestResolvers).toHaveLength(2);
      controller.candidates.activate("true-candidates");
      controller.puzzle.execute({
        type: "configureTrueCandidates",
        contextId: "true-candidates",
        refresh: "onRequest",
        display: "possibility",
        solutionCountCap: 2,
      });

      digestResolvers[1](new Uint8Array(32).buffer);
      await Promise.resolve();
      await Promise.resolve();

      expect(
        controller.candidates
          .getSnapshot()
          .definitions.find(
            (definition) => definition.id === "true-candidates",
          ),
      ).toMatchObject({ refresh: "onRequest" });
      expect(
        controller.testDependencies.solver.requests.filter(
          (request) => request.operation === "count",
        ),
      ).toHaveLength(0);
    } finally {
      controller.dispose();
      digestResolvers[0]?.(new Uint8Array(32).buffer);
      digestSpy.mockRestore();
    }
  });
});
