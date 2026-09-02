import { fork } from "node:child_process";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, "..");
const serverEntrypoint = path.join(scriptDirectory, "wasm-probe-server.mjs");

export function assertGeneratedRuntimeIsSelfContained(frameworkRoot) {
  const entrypoint = path.join(frameworkRoot, "dotnet.js");
  const source = readFileSync(entrypoint, "utf8");
  if (
    source.includes("Microsoft.DotNet.HotReload") ||
    source.includes("_content/")
  ) {
    throw new Error(
      "generated dotnet.js references an external _content dependency",
    );
  }
}

export async function startProbeServer(mode, options = {}) {
  if (mode !== "dev" && mode !== "preview") {
    throw new Error(`Unsupported WASM probe server mode: ${mode}`);
  }

  const forkProcess = options.forkProcess ?? fork;
  const startupTimeoutMs = options.startupTimeoutMs ?? 30_000;
  const shutdownTimeoutMs = options.shutdownTimeoutMs ?? 5_000;
  const child = forkProcess(serverEntrypoint, [mode], {
    cwd: webRoot,
    execArgv: [],
    stdio: ["ignore", "pipe", "pipe", "ipc"],
  });
  const output = [];
  child.stdout?.on("data", (chunk) => output.push(String(chunk)));
  child.stderr?.on("data", (chunk) => output.push(String(chunk)));

  return await new Promise((resolve, reject) => {
    let settled = false;
    const timer = setTimeout(() => {
      void fail(
        new Error(
          `${mode} server did not become ready within ${startupTimeoutMs}ms`,
        ),
        true,
      );
    }, startupTimeoutMs);

    const cleanup = () => {
      clearTimeout(timer);
      child.off("message", onMessage);
      child.off("error", onError);
      child.off("exit", onExit);
    };
    const fail = async (error, stopChild) => {
      if (settled) {
        return;
      }
      settled = true;
      cleanup();
      if (stopChild) {
        await stopProbeServer(child, { timeoutMs: shutdownTimeoutMs });
      }
      reject(withChildOutput(error, output));
    };
    const onMessage = (message) => {
      if (
        message?.type !== "ready" ||
        typeof message.url !== "string"
      ) {
        return;
      }
      if (settled) {
        return;
      }
      settled = true;
      cleanup();
      resolve({ child, baseUrl: new URL(message.url).href });
    };
    const onError = (error) => {
      void fail(new Error(`${mode} server failed to start: ${error.message}`), true);
    };
    const onExit = (code, signal) => {
      const detail =
        code === null ? ` after signal ${signal ?? "unknown"}` : ` with code ${code}`;
      void fail(
        new Error(`${mode} server exited before readiness${detail}`),
        false,
      );
    };

    child.on("message", onMessage);
    child.on("error", onError);
    child.on("exit", onExit);
  });
}

export async function stopProbeServer(child, options = {}) {
  const timeoutMs = options.timeoutMs ?? 5_000;
  if (!isRunning(child)) {
    return;
  }

  try {
    if (child.connected && typeof child.send === "function") {
      child.send({ type: "shutdown" });
    } else {
      child.kill("SIGTERM");
    }
  } catch {
    child.kill("SIGTERM");
  }

  if (await waitForExit(child, timeoutMs)) {
    return;
  }
  child.kill("SIGTERM");
  if (await waitForExit(child, timeoutMs)) {
    return;
  }
  child.kill("SIGKILL");
  if (!(await waitForExit(child, timeoutMs))) {
    throw new Error("WASM probe server did not stop");
  }
}

export async function withProbeServer(mode, operation, options = {}) {
  const server = await startProbeServer(mode, options);
  try {
    return await operation(server.baseUrl);
  } finally {
    await stopProbeServer(server.child, {
      timeoutMs: options.shutdownTimeoutMs,
    });
  }
}

function isRunning(child) {
  return child.exitCode === null && child.signalCode === null;
}

function waitForExit(child, timeoutMs) {
  if (!isRunning(child)) {
    return Promise.resolve(true);
  }

  return new Promise((resolve) => {
    const timer = setTimeout(() => finish(false), timeoutMs);
    const onExit = () => finish(true);
    const finish = (exited) => {
      clearTimeout(timer);
      child.off("exit", onExit);
      resolve(exited);
    };
    child.once("exit", onExit);
  });
}

function withChildOutput(error, output) {
  const detail = output.join("").trim();
  return detail.length === 0
    ? error
    : new Error(`${error.message}\n${detail}`, { cause: error });
}
