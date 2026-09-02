import { EventEmitter } from "node:events";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { PassThrough } from "node:stream";
import { afterEach, describe, expect, it } from "vitest";
import {
  assertGeneratedRuntimeIsSelfContained,
  startProbeServer,
  withProbeServer,
} from "./wasm-probe-orchestrator.mjs";

const temporaryDirectories = [];

afterEach(() => {
  for (const directory of temporaryDirectories.splice(0)) {
    rmSync(directory, { recursive: true, force: true });
  }
});

describe("generated WASM runtime assertion", () => {
  it("rejects a generated runtime with an external content dependency", () => {
    const frameworkRoot = createFrameworkRoot(
      '"name":"_content/Microsoft.DotNet.HotReload.WebAssembly.Browser/init.js"',
    );

    expect(() => assertGeneratedRuntimeIsSelfContained(frameworkRoot)).toThrow(
      "generated dotnet.js references an external _content dependency",
    );
  });

  it("accepts a generated runtime containing only framework assets", () => {
    const frameworkRoot = createFrameworkRoot(
      '"name":"System.Private.CoreLib.wasm"',
    );

    expect(() => assertGeneratedRuntimeIsSelfContained(frameworkRoot)).not.toThrow();
  });
});

describe("WASM probe server orchestration", () => {
  it("does not pass inherited parent execution flags to the server child", async () => {
    const child = new FakeChild();
    const server = await startProbeServer("dev", {
      forkProcess: (_entrypoint, _arguments, forkOptions) => {
        const effectiveExecArguments =
          forkOptions.execArgv ?? ["--input-type=module"];
        if (effectiveExecArguments.includes("--input-type=module")) {
          queueMicrotask(() => child.exit(1));
        } else {
          child.readyUrl = "http://127.0.0.1:54320/";
        }
        return child;
      },
      startupTimeoutMs: 100,
    });

    expect(server.baseUrl).toBe("http://127.0.0.1:54320/");
    child.exit(0);
  });

  it("reports a child that exits before announcing readiness", async () => {
    const child = new FakeChild();
    const started = startProbeServer("dev", {
      forkProcess: () => child,
      startupTimeoutMs: 100,
    });
    queueMicrotask(() => child.exit(17));

    await expect(started).rejects.toThrow(
      "dev server exited before readiness with code 17",
    );
    expect(child.running).toBe(false);
  });

  it("stops a ready child when the probe operation fails", async () => {
    const child = new FakeChild();
    child.readyUrl = "http://127.0.0.1:54321/";

    await expect(
      withProbeServer(
        "preview",
        async () => {
          throw new Error("forced probe failure");
        },
        {
          forkProcess: () => child,
          startupTimeoutMs: 100,
          shutdownTimeoutMs: 100,
        },
      ),
    ).rejects.toThrow("forced probe failure");
    expect(child.running).toBe(false);
  });

  it("terminates a child that never announces readiness", async () => {
    const child = new FakeChild();

    await expect(
      startProbeServer("dev", {
        forkProcess: () => child,
        startupTimeoutMs: 5,
        shutdownTimeoutMs: 100,
      }),
    ).rejects.toThrow("dev server did not become ready within 5ms");
    expect(child.running).toBe(false);
  });
});

class FakeChild extends EventEmitter {
  stdout = new PassThrough();
  stderr = new PassThrough();
  connected = true;
  exitCode = null;
  signalCode = null;
  running = true;
  readyUrl;

  constructor() {
    super();
    queueMicrotask(() => {
      if (this.running && this.readyUrl !== undefined) {
        this.emit("message", { type: "ready", url: this.readyUrl });
      }
    });
  }

  send(message) {
    if (message?.type === "shutdown") {
      queueMicrotask(() => this.exit(0));
    }
  }

  kill(signal = "SIGTERM") {
    this.signalCode = signal;
    queueMicrotask(() => this.exit(null, signal));
    return true;
  }

  exit(code, signal = null) {
    if (!this.running) {
      return;
    }
    this.running = false;
    this.connected = false;
    this.exitCode = code;
    this.signalCode = signal;
    this.emit("exit", code, signal);
  }
}

function createFrameworkRoot(dotnetSource) {
  const directory = mkdtempSync(path.join(os.tmpdir(), "wasm-runtime-test-"));
  temporaryDirectories.push(directory);
  writeFileSync(path.join(directory, "dotnet.js"), dotnetSource);
  return directory;
}
