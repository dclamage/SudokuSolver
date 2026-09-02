import { describe, expect, it } from "vitest";
import { WasmSolverClient } from "./WasmSolverClient";
import type { SolverResponse } from "./protocol";
import {
  ModuleWorkerTransport,
  type WorkerLike,
  type WorkerOutboundMessage,
  type WorkerTransportEvent,
} from "./WorkerTransport";
import { FakeWorkerTransport } from "./testing/FakeWorkerTransport";
import {
  makeCountProgressResponse,
  makeCountRequest,
  makeCountResultResponse,
  makeValidateErrorResponse,
  makeValidateRequest,
  makeValidateResponse,
} from "./testing/protocolFixtures";

describe("WasmSolverClient", () => {
  it("rejects a response that does not match its submitted document revision", async () => {
    const transport = new FakeWorkerTransport();
    const client = new WasmSolverClient(transport);
    const job = client.start(
      makeValidateRequest({
        requestId: "r1",
        documentRevision: 4,
        semanticRevision: 2,
      }),
    );

    transport.emit(
      makeValidateResponse({
        requestId: "r1",
        documentRevision: 3,
        semanticRevision: 2,
      }),
    );

    await expect(job.result).rejects.toThrow("stale solver response");
  });

  it("cancels by restarting the single-threaded worker", async () => {
    const transport = new FakeWorkerTransport();
    const client = new WasmSolverClient(transport);
    const job = client.start(makeValidateRequest({ requestId: "r2" }));

    job.cancel();

    expect(transport.restartCount).toBe(1);
    await expect(job.result).rejects.toThrow("solver worker restarted");
  });

  it.each([
    ["protocol version", { protocolVersion: 2 }],
    ["operation", { operation: "solve" }],
    ["semantic revision", { semanticRevision: 8 }],
    ["semantic hash", { semanticHash: "sha256:different" }],
    ["context ID", { contextId: "other-context" }],
  ])("rejects a response with a mismatched %s", async (_name, mismatch) => {
    const transport = new FakeWorkerTransport();
    const client = new WasmSolverClient(transport);
    const job = client.start(makeValidateRequest({ requestId: "correlated" }));
    const response = {
      ...makeValidateResponse({ requestId: "correlated" }),
      ...mismatch,
    } as unknown as SolverResponse;

    transport.emit(response);

    await expect(job.result).rejects.toThrow("stale solver response");
  });

  it("preserves the machine-readable solver error code", async () => {
    const transport = new FakeWorkerTransport();
    const client = new WasmSolverClient(transport);
    const job = client.start(makeValidateRequest({ requestId: "invalid" }));

    transport.emit(makeValidateErrorResponse({ requestId: "invalid" }));

    await expect(job.result).rejects.toMatchObject({
      code: "contradiction",
      message: "The projected puzzle is contradictory.",
    });
  });

  it("does not restart the worker when a completed job is canceled", async () => {
    const transport = new FakeWorkerTransport();
    const client = new WasmSolverClient(transport);
    const job = client.start(makeValidateRequest({ requestId: "complete" }));
    transport.emit(makeValidateResponse({ requestId: "complete" }));
    await job.result;

    job.cancel();

    expect(transport.restartCount).toBe(0);
  });

  it("rejects a duplicate outstanding request ID", () => {
    const transport = new FakeWorkerTransport();
    const client = new WasmSolverClient(transport);
    client.start(makeValidateRequest({ requestId: "duplicate" }));

    expect(() =>
      client.start(makeValidateRequest({ requestId: "duplicate" })),
    ).toThrow("solver request duplicate is already active");
  });

  it("rejects outstanding and future jobs when disposed", async () => {
    const transport = new FakeWorkerTransport();
    const client = new WasmSolverClient(transport);
    const job = client.start(makeValidateRequest({ requestId: "dispose" }));
    const outcome = job.result.then(
      () => "resolved",
      (error: Error) => error.message,
    );

    client.dispose();

    await expect(
      Promise.race([
        outcome,
        new Promise<string>((resolve) =>
          setTimeout(() => resolve("still pending"), 10),
        ),
      ]),
    ).resolves.toBe("solver client disposed");
    expect(() =>
      client.start(makeValidateRequest({ requestId: "after-dispose" })),
    ).toThrow("solver client disposed");
  });

  it("rejects a job when its worker transport fails", async () => {
    const transport = new FakeWorkerTransport();
    const client = new WasmSolverClient(transport);
    const job = client.start(makeValidateRequest({ requestId: "failed" }));
    const outcome = job.result.then(
      () => "resolved",
      (error: Error) => error.message,
    );

    transport.fail(new Error("runtime boot failed"), "failed");

    await expect(
      Promise.race([
        outcome,
        new Promise<string>((resolve) =>
          setTimeout(() => resolve("still pending"), 10),
        ),
      ]),
    ).resolves.toBe("runtime boot failed");
  });

  it("fails a second request immediately after a generation-wide worker failure", async () => {
    const transport = new FakeWorkerTransport();
    const client = new WasmSolverClient(transport);
    const first = client.start(makeValidateRequest({ requestId: "first" }));
    transport.fail(new Error("runtime boot failed"));
    await expect(first.result).rejects.toThrow("runtime boot failed");

    expect(() =>
      client.start(makeValidateRequest({ requestId: "second" })),
    ).toThrow("solver worker unavailable: runtime boot failed");
  });

  it("remains unavailable after repeated generation-wide boot failures", () => {
    const transport = new FakeWorkerTransport();
    const client = new WasmSolverClient(transport);
    transport.fail(new Error("first boot failed"));
    transport.fail(new Error("second boot failed"));

    expect(() =>
      client.start(makeValidateRequest({ requestId: "after-retries" })),
    ).toThrow("solver worker unavailable: first boot failed");
  });

  it("publishes correlated progress and resolves only the terminal result", async () => {
    const transport = new FakeWorkerTransport();
    const client = new WasmSolverClient(transport);
    const job = client.start(makeCountRequest({ requestId: "count" }));
    const progress: SolverResponse[] = [];
    job.subscribe((response) => progress.push(response));
    const progressResponse = makeCountProgressResponse({ requestId: "count" });
    const resultResponse = makeCountResultResponse({ requestId: "count" });

    transport.emit(progressResponse);
    transport.emit(resultResponse);

    expect(progress).toEqual([progressResponse]);
    await expect(job.result).resolves.toBe(resultResponse);
  });

  it("rejects every outstanding job owned by a restarted generation", async () => {
    const transport = new FakeWorkerTransport();
    const client = new WasmSolverClient(transport);
    const first = client.start(makeValidateRequest({ requestId: "first" }));
    const second = client.start(makeValidateRequest({ requestId: "second" }));
    const firstRejection = expect(first.result).rejects.toThrow(
      "solver worker restarted",
    );
    const secondRejection = expect(second.result).rejects.toThrow(
      "solver worker restarted",
    );

    first.cancel();

    await Promise.all([firstRejection, secondRejection]);
    expect(transport.restartCount).toBe(1);
  });

  it("ignores an unknown request ID until its own response arrives", async () => {
    const transport = new FakeWorkerTransport();
    const client = new WasmSolverClient(transport);
    const job = client.start(makeValidateRequest({ requestId: "expected" }));
    transport.emit(makeValidateResponse({ requestId: "unknown" }));
    const expected = makeValidateResponse({ requestId: "expected" });

    transport.emit(expected);

    await expect(job.result).resolves.toBe(expected);
  });

  it("ignores a late response from the terminated worker generation", async () => {
    const transport = new FakeWorkerTransport();
    const client = new WasmSolverClient(transport);
    const oldJob = client.start(makeValidateRequest({ requestId: "reused" }));
    const oldRejection = expect(oldJob.result).rejects.toThrow(
      "solver worker restarted",
    );
    oldJob.cancel();
    await oldRejection;
    const newJob = client.start(makeValidateRequest({ requestId: "reused" }));
    transport.emit(makeValidateResponse({ requestId: "reused" }), 0);
    const current = makeValidateResponse({ requestId: "reused" });

    transport.emit(current);

    await expect(newJob.result).resolves.toBe(current);
  });

  it("rejects done without a terminal response and frees the request ID", async () => {
    const transport = new FakeWorkerTransport();
    const client = new WasmSolverClient(transport);
    const job = client.start(makeValidateRequest({ requestId: "done-only" }));
    const outcome = job.result.then(
      () => "resolved",
      (error: Error) => error.message,
    );

    transport.done("done-only");

    await expect(
      Promise.race([
        outcome,
        new Promise<string>((resolve) =>
          setTimeout(() => resolve("still pending"), 10),
        ),
      ]),
    ).resolves.toBe("solver worker completed without a terminal response");
    const replacement = client.start(
      makeValidateRequest({ requestId: "done-only" }),
    );
    const response = makeValidateResponse({ requestId: "done-only" });
    transport.emit(response);
    await expect(replacement.result).resolves.toBe(response);
  });

  it("ignores done after a normal terminal response", async () => {
    const transport = new FakeWorkerTransport();
    const client = new WasmSolverClient(transport);
    const job = client.start(makeValidateRequest({ requestId: "terminal" }));
    const response = makeValidateResponse({ requestId: "terminal" });
    transport.emit(response);
    await expect(job.result).resolves.toBe(response);

    transport.done("terminal");

    expect(transport.restartCount).toBe(0);
  });

  it("ignores late done from a terminated worker generation", async () => {
    const transport = new FakeWorkerTransport();
    const client = new WasmSolverClient(transport);
    const oldJob = client.start(makeValidateRequest({ requestId: "late-done" }));
    const oldRejection = expect(oldJob.result).rejects.toThrow(
      "solver worker restarted",
    );
    oldJob.cancel();
    await oldRejection;
    const currentJob = client.start(
      makeValidateRequest({ requestId: "late-done" }),
    );
    transport.done("late-done", 0);
    const current = makeValidateResponse({ requestId: "late-done" });
    transport.emit(current);

    await expect(currentJob.result).resolves.toBe(current);
  });
});

describe("ModuleWorkerTransport", () => {
  it("terminates and replaces the worker while preserving message generations", () => {
    const workers: TestWorker[] = [];
    const transport = new ModuleWorkerTransport(() => {
      const worker = new TestWorker();
      workers.push(worker);
      return worker;
    });
    const events: WorkerTransportEvent[] = [];
    transport.subscribe((event) => events.push(event));
    const request = makeValidateRequest({ requestId: "generation" });
    transport.send(request);

    transport.restart();
    workers[0]?.emit({
      kind: "response",
      response: makeValidateResponse({ requestId: "generation" }),
    });
    workers[1]?.emit({
      kind: "response",
      response: makeValidateResponse({ requestId: "generation" }),
    });

    expect(workers).toHaveLength(2);
    expect(workers[0]?.terminated).toBe(true);
    expect(workers[0]?.sent).toEqual([{ kind: "request", request }]);
    expect(
      events
        .filter((event) => event.kind === "response")
        .map((event) => event.generation),
    ).toEqual([0, 1]);
  });
});

class TestWorker implements WorkerLike {
  readonly sent: unknown[] = [];
  terminated = false;
  private readonly messageListeners = new Set<
    (event: MessageEvent<WorkerOutboundMessage>) => void
  >();
  private readonly errorListeners = new Set<(event: ErrorEvent) => void>();

  postMessage(message: unknown): void {
    this.sent.push(message);
  }

  terminate(): void {
    this.terminated = true;
  }

  addEventListener(
    type: "message" | "error",
    listener:
      | ((event: MessageEvent<WorkerOutboundMessage>) => void)
      | ((event: ErrorEvent) => void),
  ): void {
    if (type === "message") {
      this.messageListeners.add(
        listener as (event: MessageEvent<WorkerOutboundMessage>) => void,
      );
    } else {
      this.errorListeners.add(listener as (event: ErrorEvent) => void);
    }
  }

  emit(message: WorkerOutboundMessage): void {
    const event = { data: message } as MessageEvent<WorkerOutboundMessage>;
    for (const listener of this.messageListeners) {
      listener(event);
    }
  }
}
