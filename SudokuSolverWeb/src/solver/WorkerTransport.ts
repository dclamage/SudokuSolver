import type { SolverRequest, SolverResponse } from "./protocol";

export type WorkerRequestMessage = {
  kind: "request";
  request: SolverRequest;
};

export type WorkerOutboundMessage =
  | { kind: "ready" }
  | { kind: "response"; response: SolverResponse }
  | { kind: "done"; requestId: string; elapsedMs: number }
  | { kind: "error"; requestId?: string; message: string };

export type WorkerTransportEvent =
  | { kind: "ready"; generation: number }
  | { kind: "response"; generation: number; response: SolverResponse }
  | { kind: "done"; generation: number; requestId: string }
  | {
      kind: "error";
      generation: number;
      requestId?: string;
      error: Error;
    };

export interface WorkerTransport {
  readonly generation: number;
  send(request: SolverRequest): void;
  subscribe(listener: (event: WorkerTransportEvent) => void): () => void;
  restart(): void;
  dispose(): void;
}

export interface WorkerLike {
  postMessage(message: unknown): void;
  terminate(): void;
  addEventListener(
    type: "message" | "error",
    listener:
      | ((event: MessageEvent<WorkerOutboundMessage>) => void)
      | ((event: ErrorEvent) => void),
  ): void;
}

export class ModuleWorkerTransport implements WorkerTransport {
  generation = 0;

  private worker: WorkerLike;
  private disposed = false;
  private readonly listeners = new Set<
    (event: WorkerTransportEvent) => void
  >();

  constructor(
    private readonly createWorker: () => WorkerLike = createSolverWorker,
  ) {
    this.worker = this.attachWorker(this.generation);
  }

  send(request: SolverRequest): void {
    this.throwIfDisposed();
    const message: WorkerRequestMessage = { kind: "request", request };
    this.worker.postMessage(message);
  }

  subscribe(listener: (event: WorkerTransportEvent) => void): () => void {
    this.throwIfDisposed();
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  restart(): void {
    this.throwIfDisposed();
    this.worker.terminate();
    this.generation += 1;
    this.worker = this.attachWorker(this.generation);
  }

  dispose(): void {
    if (this.disposed) {
      return;
    }

    this.disposed = true;
    this.worker.terminate();
    this.listeners.clear();
  }

  private attachWorker(generation: number): WorkerLike {
    const worker = this.createWorker();
    worker.addEventListener(
      "message",
      (event: MessageEvent<WorkerOutboundMessage>) => {
        this.handleMessage(event.data, generation);
      },
    );
    worker.addEventListener(
      "error",
      (event: ErrorEvent) => {
        const error =
          event.error instanceof Error
            ? event.error
            : new Error(event.message || "solver worker failed");
        this.publish({ kind: "error", generation, error });
      },
    );
    return worker;
  }

  private handleMessage(
    message: WorkerOutboundMessage,
    generation: number,
  ): void {
    switch (message.kind) {
      case "ready":
        this.publish({ kind: "ready", generation });
        break;
      case "response":
        this.publish({
          kind: "response",
          generation,
          response: message.response,
        });
        break;
      case "done":
        this.publish({
          kind: "done",
          generation,
          requestId: message.requestId,
        });
        break;
      case "error":
        this.publish({
          kind: "error",
          generation,
          requestId: message.requestId,
          error: new Error(message.message),
        });
        break;
    }
  }

  private publish(event: WorkerTransportEvent): void {
    for (const listener of this.listeners) {
      listener(event);
    }
  }

  private throwIfDisposed(): void {
    if (this.disposed) {
      throw new Error("solver worker transport disposed");
    }
  }
}

function createSolverWorker(): WorkerLike {
  return new Worker(new URL("./worker/solver.worker.ts", import.meta.url), {
    type: "module",
    name: "sudoku-solver",
  }) as WorkerLike;
}
