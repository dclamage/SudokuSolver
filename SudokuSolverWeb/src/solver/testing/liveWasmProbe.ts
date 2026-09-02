import { createWasmSolverClient } from "../WasmSolverClient";
import { makeValidateRequest } from "./protocolFixtures";

const status = document.querySelector<HTMLPreElement>("#wasm-probe-status");
if (status === null) {
  throw new Error("WASM probe status element is missing");
}

void runProbe(status);

async function runProbe(output: HTMLPreElement): Promise<void> {
  const client = createWasmSolverClient();
  const request = makeValidateRequest({ requestId: "live-validate" });

  try {
    const response = await client.start(request).result;
    output.dataset.state = "succeeded";
    output.textContent = JSON.stringify({
      outcome: "resolved",
      correlation: {
        requestId: response.requestId,
        operation: response.operation,
        documentRevision: response.documentRevision,
        semanticRevision: response.semanticRevision,
        semanticHash: response.semanticHash,
        contextId: response.contextId,
      },
      kind: response.kind,
      capability:
        response.kind === "result" && response.capability !== undefined
          ? {
              projectionId: response.capability.projectionId,
              contradiction: response.capability.contradiction,
              entityCount: Object.keys(response.capability.entities).length,
            }
          : undefined,
    });
  } catch (error) {
    output.dataset.state = "failed";
    output.textContent = JSON.stringify({
      outcome: "rejected",
      error: error instanceof Error ? error.message : String(error),
    });
  } finally {
    client.dispose();
  }
}
