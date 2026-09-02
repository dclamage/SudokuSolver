/// <reference lib="webworker" />

import type {
  DotnetModule,
  SudokuSolverAssemblyExports,
} from "../dotnet";
import type { SolverResponse } from "../protocol";
import type {
  WorkerOutboundMessage,
  WorkerRequestMessage,
} from "../WorkerTransport";

const queuedRequests: WorkerRequestMessage[] = [];
let solver: SudokuSolverAssemblyExports["SudokuSolverWasm"]["SolverInterop"]
  | undefined;

const workerScope = globalThis as unknown as {
  location: Location;
  postMessage(message: WorkerOutboundMessage): void;
  addEventListener(
    type: "message",
    listener: (event: MessageEvent<WorkerRequestMessage>) => void,
  ): void;
};

workerScope.addEventListener("message", (event) => {
  if (solver === undefined) {
    queuedRequests.push(event.data);
    return;
  }

  handleRequest(event.data);
});

void boot();

async function boot(): Promise<void> {
  try {
    const dotnetModuleUrl = new URL(
      "/solver/_framework/dotnet.js",
      workerScope.location.origin,
    ).href;
    const dotnetModule = (await import(
      /* @vite-ignore */ dotnetModuleUrl
    )) as DotnetModule;
    const runtime = await dotnetModule.dotnet
      .withDiagnosticTracing(false)
      .create();
    runtime.setModuleImports("solver", {
      sendResponse: (json: string) => {
        const response = JSON.parse(json) as SolverResponse;
        post({ kind: "response", response });
      },
    });
    const config = runtime.getConfig();
    const exports =
      await runtime.getAssemblyExports<SudokuSolverAssemblyExports>(
        config.mainAssemblyName,
      );
    solver = exports.SudokuSolverWasm.SolverInterop;
    solver.Initialize(true);
    post({ kind: "ready" });

    for (const request of queuedRequests.splice(0)) {
      handleRequest(request);
    }
  } catch (error) {
    post({ kind: "error", message: formatError("Boot failed", error) });
  }
}

function handleRequest(message: WorkerRequestMessage): void {
  const started = performance.now();
  try {
    const activeSolver = solver;
    if (activeSolver === undefined) {
      throw new Error("Solver runtime is not ready");
    }
    activeSolver.HandleMessage(JSON.stringify(message.request));
    post({
      kind: "done",
      requestId: message.request.requestId,
      elapsedMs: performance.now() - started,
    });
  } catch (error) {
    post({
      kind: "error",
      requestId: message.request.requestId,
      message: formatError("Solver request failed", error),
    });
  }
}

function post(message: WorkerOutboundMessage): void {
  workerScope.postMessage(message);
}

function formatError(prefix: string, error: unknown): string {
  const detail =
    error instanceof Error ? error.stack ?? error.message : String(error);
  return `${prefix}: ${detail}`;
}
