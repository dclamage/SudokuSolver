import type { SolverClient, SolverJob } from "../solver/SolverClient";
import type { SolverRequest, SolverResponse } from "../solver/protocol";

interface FakePendingJob {
  readonly resolve: (response: SolverResponse) => void;
  readonly reject: (error: Error) => void;
  readonly listeners: Set<(response: SolverResponse) => void>;
}

export class FakeSolverClient implements SolverClient {
  readonly requests: SolverRequest[] = [];
  readonly canceledRequestIds: string[] = [];

  private readonly pending = new Map<string, FakePendingJob>();
  private disposed = false;

  start<TResponse extends SolverResponse>(
    request: SolverRequest,
  ): SolverJob<TResponse> {
    if (this.disposed) {
      throw new Error("fake solver client disposed");
    }
    if (this.pending.has(request.requestId)) {
      throw new Error(`solver request ${request.requestId} is already active`);
    }

    this.requests.push(request);
    let resolveResult!: (response: TResponse) => void;
    let rejectResult!: (error: Error) => void;
    const result = new Promise<TResponse>((resolve, reject) => {
      resolveResult = resolve;
      rejectResult = reject;
    });
    const pendingJob: FakePendingJob = {
      resolve: (response) => resolveResult(response as TResponse),
      reject: rejectResult,
      listeners: new Set(),
    };
    this.pending.set(request.requestId, pendingJob);

    return {
      result,
      cancel: () => {
        if (this.pending.get(request.requestId) !== pendingJob) {
          return;
        }
        this.canceledRequestIds.push(request.requestId);
        this.pending.delete(request.requestId);
        pendingJob.reject(new Error("solver job canceled"));
      },
      subscribe: (listener) => {
        const typedListener = listener as (response: SolverResponse) => void;
        pendingJob.listeners.add(typedListener);
        return () => pendingJob.listeners.delete(typedListener);
      },
    };
  }

  progress(requestId: string, response: SolverResponse): void {
    const job = this.getPending(requestId);
    for (const listener of job.listeners) {
      listener(response);
    }
  }

  resolve(requestId: string, response: SolverResponse): void {
    const job = this.getPending(requestId);
    this.pending.delete(requestId);
    job.resolve(response);
  }

  reject(requestId: string, error: Error): void {
    const job = this.getPending(requestId);
    this.pending.delete(requestId);
    job.reject(error);
  }

  dispose(): void {
    if (this.disposed) {
      return;
    }
    this.disposed = true;
    for (const [requestId, job] of this.pending) {
      this.pending.delete(requestId);
      job.reject(new Error("fake solver client disposed"));
    }
  }

  private getPending(requestId: string): FakePendingJob {
    const job = this.pending.get(requestId);
    if (job === undefined) {
      throw new Error(`no pending solver request ${requestId}`);
    }
    return job;
  }
}
