import type {
  SolverRequest,
  SolverResponse,
} from "../protocol";
import type {
  WorkerTransport,
  WorkerTransportEvent,
} from "../WorkerTransport";

export class FakeWorkerTransport implements WorkerTransport {
  readonly sent: SolverRequest[] = [];
  restartCount = 0;
  generation = 0;

  private readonly listeners = new Set<
    (event: WorkerTransportEvent) => void
  >();

  send(request: SolverRequest): void {
    this.sent.push(request);
  }

  subscribe(listener: (event: WorkerTransportEvent) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  emit(response: SolverResponse, generation = this.generation): void {
    this.publish({ kind: "response", generation, response });
  }

  fail(
    error: Error,
    requestId?: string,
    generation = this.generation,
  ): void {
    this.publish({ kind: "error", generation, requestId, error });
  }

  restart(): void {
    this.restartCount += 1;
    this.generation += 1;
  }

  dispose(): void {
    this.listeners.clear();
  }

  private publish(event: WorkerTransportEvent): void {
    for (const listener of this.listeners) {
      listener(event);
    }
  }
}
