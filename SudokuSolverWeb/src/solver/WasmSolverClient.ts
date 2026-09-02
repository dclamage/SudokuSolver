import type { SolverClient, SolverJob } from "./SolverClient";
import type { SolverRequest, SolverResponse } from "./protocol";
import {
  ModuleWorkerTransport,
  type WorkerTransport,
  type WorkerTransportEvent,
} from "./WorkerTransport";

interface PendingJob {
  readonly request: SolverRequest;
  readonly generation: number;
  readonly resolve: (response: SolverResponse) => void;
  readonly reject: (error: Error) => void;
  readonly listeners: Set<(response: SolverResponse) => void>;
}

export class SolverResponseError extends Error {
  constructor(
    readonly code: string,
    message: string,
  ) {
    super(message);
    this.name = "SolverResponseError";
  }
}

export class WasmSolverClient implements SolverClient {
  private readonly pending = new Map<string, PendingJob>();
  private readonly completed = new Set<string>();
  private readonly unsubscribeTransport: () => void;
  private disposed = false;
  private unavailableError: Error | undefined;

  constructor(private readonly transport: WorkerTransport) {
    this.unsubscribeTransport = transport.subscribe((event) =>
      this.handleTransportEvent(event),
    );
  }

  start<TResponse extends SolverResponse>(
    request: SolverRequest,
  ): SolverJob<TResponse> {
    if (this.disposed) {
      throw new Error("solver client disposed");
    }

    if (this.unavailableError !== undefined) {
      throw new Error(
        `solver worker unavailable: ${this.unavailableError.message}`,
        { cause: this.unavailableError },
      );
    }

    if (this.pending.has(request.requestId)) {
      throw new Error(`solver request ${request.requestId} is already active`);
    }

    let resolveResult!: (response: TResponse) => void;
    let rejectResult!: (error: Error) => void;
    const result = new Promise<TResponse>((resolve, reject) => {
      resolveResult = resolve;
      rejectResult = reject;
    });
    const listeners = new Set<(response: SolverResponse) => void>();
    const generation = this.transport.generation;

    const pendingJob: PendingJob = {
      request,
      generation,
      resolve: (response) => resolveResult(response as TResponse),
      reject: rejectResult,
      listeners,
    };
    this.pending.set(request.requestId, pendingJob);
    this.transport.send(request);

    return {
      result,
      cancel: () => {
        if (this.pending.get(request.requestId) === pendingJob) {
          this.restartGeneration(generation);
        }
      },
      subscribe: (listener) => {
        const typedListener = listener as (response: SolverResponse) => void;
        listeners.add(typedListener);
        return () => listeners.delete(typedListener);
      },
    };
  }

  dispose(): void {
    if (this.disposed) {
      return;
    }

    this.disposed = true;
    for (const job of this.pending.values()) {
      this.rejectJob(job, new Error("solver client disposed"));
    }
    this.unsubscribeTransport();
    this.transport.dispose();
  }

  private handleTransportEvent(event: WorkerTransportEvent): void {
    if (event.kind === "error") {
      if (
        event.requestId === undefined &&
        event.generation === this.transport.generation
      ) {
        this.unavailableError ??= event.error;
      }
      for (const job of this.pending.values()) {
        if (
          job.generation === event.generation &&
          (event.requestId === undefined ||
            event.requestId === job.request.requestId)
        ) {
          this.rejectJob(job, event.error);
        }
      }
      return;
    }

    if (event.kind === "done") {
      const completionKey = getCompletionKey(event.generation, event.requestId);
      if (this.completed.delete(completionKey)) {
        return;
      }

      const job = this.pending.get(event.requestId);
      if (job !== undefined && job.generation === event.generation) {
        this.rejectJob(
          job,
          new Error("solver worker completed without a terminal response"),
        );
      }
      return;
    }

    if (event.kind !== "response") {
      return;
    }

    const job = this.pending.get(event.response.requestId);
    if (job === undefined || job.generation !== event.generation) {
      return;
    }

    if (!isCorrelated(job.request, event.response)) {
      this.rejectJob(job, new Error("stale solver response"));
      return;
    }

    if (event.response.kind === "progress") {
      for (const listener of job.listeners) {
        listener(event.response);
      }
      return;
    }

    if (event.response.kind === "result") {
      this.completed.add(
        getCompletionKey(event.generation, event.response.requestId),
      );
      this.pending.delete(job.request.requestId);
      job.resolve(event.response);
      return;
    }

    const error =
      event.response.kind === "error"
        ? new SolverResponseError(
            event.response.error.code,
            event.response.error.message,
          )
        : new Error("solver job canceled");
    this.completed.add(
      getCompletionKey(event.generation, event.response.requestId),
    );
    this.rejectJob(job, error);
  }

  private restartGeneration(generation: number): void {
    if (generation !== this.transport.generation) {
      return;
    }

    this.transport.restart();
    for (const completionKey of this.completed) {
      if (completionKey.startsWith(`${generation}:`)) {
        this.completed.delete(completionKey);
      }
    }
    for (const job of this.pending.values()) {
      if (job.generation === generation) {
        this.rejectJob(job, new Error("solver worker restarted"));
      }
    }
  }

  private rejectJob(job: PendingJob, error: Error): void {
    this.pending.delete(job.request.requestId);
    job.reject(error);
  }
}

function getCompletionKey(generation: number, requestId: string): string {
  return `${generation}:${requestId}`;
}

function isCorrelated(
  request: SolverRequest,
  response: SolverResponse,
): boolean {
  return (
    response.protocolVersion === request.protocolVersion &&
    response.requestId === request.requestId &&
    response.operation === request.operation &&
    response.documentRevision === request.documentRevision &&
    response.semanticRevision === request.semanticRevision &&
    response.semanticHash === request.semanticHash &&
    response.contextId === request.contextId
  );
}

export function createWasmSolverClient(): SolverClient {
  return new WasmSolverClient(new ModuleWorkerTransport());
}
