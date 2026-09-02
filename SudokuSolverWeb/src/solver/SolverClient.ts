import type { SolverRequest, SolverResponse } from "./protocol";

export interface SolverJob<
  TResponse extends SolverResponse = SolverResponse,
> {
  readonly result: Promise<TResponse>;
  cancel(): void;
  subscribe(listener: (response: TResponse) => void): () => void;
}

export interface SolverClient {
  start<TResponse extends SolverResponse>(
    request: SolverRequest,
  ): SolverJob<TResponse>;
  dispose(): void;
}
