import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "@playwright/test";
import { build } from "vite";
import {
  assertGeneratedRuntimeIsSelfContained,
  startProbeServer,
  stopProbeServer,
} from "./wasm-probe-orchestrator.mjs";
import { verifyEmittedSolverWorker } from "./verify-wasm-worker-build.mjs";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, "..");
const frameworkRoot = path.join(webRoot, "public", "solver", "_framework");
const probeConfig = path.join(webRoot, "vite.probe.config.ts");
const probeOutput = path.join(webRoot, ".wasm-probe-dist");

export async function runSelfContainedWasmProbe(options = {}) {
  const cleanup = createCleanupRegistry();
  const removeSignalHandlers = installSignalHandlers(cleanup);
  const probePage = options.probePage ?? probeRuntimePage;

  try {
    assertGeneratedRuntimeIsSelfContained(frameworkRoot);
    await runServerProbe("dev", probePage, cleanup, options);

    await build({
      configFile: probeConfig,
      logLevel: "info",
    });
    verifyEmittedSolverWorker(probeOutput);
    assertGeneratedRuntimeIsSelfContained(
      path.join(probeOutput, "solver", "_framework"),
    );

    await runServerProbe("preview", probePage, cleanup, options);
  } finally {
    removeSignalHandlers();
    await cleanup.runAll();
  }
}

export async function probeRuntimePage(baseUrl, options = {}) {
  const browser = await chromium.launch({ headless: true });
  const unregisterBrowser = options.cleanup?.add(() => browser.close());
  const page = await browser.newPage();
  const failures = [];
  const runtimeResponses = [];

  page.on("pageerror", (error) => {
    failures.push({ kind: "pageerror", error: error.message });
  });
  page.on("console", (message) => {
    if (message.type() === "error") {
      failures.push({ kind: "console", error: message.text() });
    }
  });
  page.on("requestfailed", (request) => {
    failures.push({
      kind: "requestfailed",
      url: request.url(),
      error: request.failure()?.errorText ?? "unknown request failure",
    });
  });
  page.on("response", (response) => {
    if (
      response.url().includes("/solver/") ||
      response.url().includes("solver.worker")
    ) {
      runtimeResponses.push({ url: response.url(), status: response.status() });
    }
    if (response.status() >= 400) {
      failures.push({
        kind: "response",
        url: response.url(),
        status: response.status(),
      });
    }
  });

  try {
    await page.goto(new URL("/wasm-probe.html", baseUrl).href);
    let waitError;
    try {
      await page.waitForFunction(
        () =>
          document
            .querySelector("#wasm-probe-status")
            ?.getAttribute("data-state") !== "pending",
        undefined,
        { timeout: options.timeoutMs ?? 30_000 },
      );
    } catch (error) {
      waitError = error instanceof Error ? error.message : String(error);
    }
    const state = await page
      .locator("#wasm-probe-status")
      .getAttribute("data-state");
    const probeText = await page.locator("#wasm-probe-status").textContent();
    let probe;
    try {
      probe = JSON.parse(probeText);
    } catch {
      probe = { outcome: "invalid-probe-output", text: probeText };
    }
    const solverWorker = runtimeResponses.find(({ url }) =>
      url.includes("solver.worker"),
    );
    const dotnetEntrypoint = runtimeResponses.find(({ url }) =>
      /\/solver\/_framework\/dotnet\.js(?:$|\?)/.test(url),
    );
    const hotReloadInitializerRequests = runtimeResponses.filter(({ url }) =>
      url.includes("Microsoft.DotNet.HotReload"),
    );
    const evidence = {
      state,
      probe,
      failures,
      runtime: {
        solverWorker,
        dotnetEntrypoint,
        frameworkAssetCount: runtimeResponses.filter(({ url }) =>
          url.includes("/solver/_framework/"),
        ).length,
        hotReloadInitializerRequests,
      },
      waitError,
    };
    console.log(JSON.stringify(evidence, undefined, 2));

    if (
      state !== "succeeded" ||
      probe.outcome !== "resolved" ||
      probe.kind !== "result" ||
      probe.correlation?.requestId !== "live-validate" ||
      probe.correlation?.operation !== "validate" ||
      solverWorker?.status !== 200 ||
      dotnetEntrypoint?.status !== 200 ||
      dotnetEntrypoint.url.includes("?import") ||
      hotReloadInitializerRequests.length !== 0 ||
      failures.length !== 0
    ) {
      throw new Error("WASM runtime probe failed");
    }
    return evidence;
  } finally {
    unregisterBrowser?.();
    await browser.close();
  }
}

async function runServerProbe(mode, probePage, cleanup, options) {
  const server = await startProbeServer(mode, options);
  const unregisterServer = cleanup.add(() =>
    stopProbeServer(server.child, {
      timeoutMs: options.shutdownTimeoutMs,
    }),
  );
  try {
    console.log(`Running ${mode} WASM worker/native probe at ${server.baseUrl}`);
    await probePage(server.baseUrl, { cleanup });
  } finally {
    unregisterServer();
    await stopProbeServer(server.child, {
      timeoutMs: options.shutdownTimeoutMs,
    });
  }
}

function createCleanupRegistry() {
  const cleanups = new Set();
  return {
    add(cleanup) {
      cleanups.add(cleanup);
      return () => cleanups.delete(cleanup);
    },
    async runAll() {
      const results = await Promise.allSettled(
        Array.from(cleanups).reverse().map((cleanup) => cleanup()),
      );
      cleanups.clear();
      const failure = results.find((result) => result.status === "rejected");
      if (failure?.status === "rejected") {
        throw failure.reason;
      }
    },
  };
}

function installSignalHandlers(cleanup) {
  const handlers = new Map();
  for (const [signal, exitCode] of [
    ["SIGINT", 130],
    ["SIGTERM", 143],
  ]) {
    const handler = () => {
      void cleanup.runAll().finally(() => process.exit(exitCode));
    };
    handlers.set(signal, handler);
    process.once(signal, handler);
  }
  return () => {
    for (const [signal, handler] of handlers) {
      process.off(signal, handler);
    }
  };
}

if (
  process.argv[1] !== undefined &&
  path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  try {
    await runSelfContainedWasmProbe();
  } catch (error) {
    console.error(error instanceof Error ? error.stack : String(error));
    process.exitCode = 1;
  }
}
