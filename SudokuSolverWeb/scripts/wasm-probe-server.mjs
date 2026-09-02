import path from "node:path";
import { fileURLToPath } from "node:url";
import { createServer, preview } from "vite";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, "..");
const mode = process.argv[2];
let server;
let closing;

try {
  if (mode === "dev") {
    server = await createServer({
      configFile: path.join(webRoot, "vite.config.ts"),
      logLevel: "silent",
      server: { host: "127.0.0.1", port: 0, strictPort: true },
    });
    await server.listen();
  } else if (mode === "preview") {
    server = await preview({
      configFile: path.join(webRoot, "vite.probe.config.ts"),
      logLevel: "silent",
      preview: { host: "127.0.0.1", port: 0, strictPort: true },
    });
  } else {
    throw new Error(`Unsupported WASM probe server mode: ${mode}`);
  }

  const address = server.httpServer.address();
  if (address === null || typeof address === "string") {
    throw new Error(`${mode} server did not expose a TCP port`);
  }
  process.send?.({
    type: "ready",
    url: `http://127.0.0.1:${address.port}/`,
  });
} catch (error) {
  console.error(error instanceof Error ? error.stack : String(error));
  await shutdown(1);
}

process.on("message", (message) => {
  if (message?.type === "shutdown") {
    void shutdown(0);
  }
});
process.on("disconnect", () => void shutdown(0));
process.on("SIGINT", () => void shutdown(130));
process.on("SIGTERM", () => void shutdown(143));
process.on("uncaughtException", (error) => {
  console.error(error.stack ?? error.message);
  void shutdown(1);
});
process.on("unhandledRejection", (error) => {
  console.error(error instanceof Error ? error.stack : String(error));
  void shutdown(1);
});

async function shutdown(exitCode) {
  if (closing !== undefined) {
    return closing;
  }
  closing = (async () => {
    try {
      await server?.close();
    } finally {
      process.exit(exitCode);
    }
  })();
  return closing;
}
