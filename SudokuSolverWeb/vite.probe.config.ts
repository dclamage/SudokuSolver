import path from "node:path";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";

const webRoot = path.dirname(fileURLToPath(import.meta.url));

export default defineConfig({
  root: webRoot,
  worker: {
    format: "es",
  },
  build: {
    outDir: ".wasm-probe-dist",
    emptyOutDir: true,
    rollupOptions: {
      input: path.join(webRoot, "wasm-probe.html"),
    },
  },
  preview: {
    headers: {
      "Cross-Origin-Opener-Policy": "same-origin",
      "Cross-Origin-Embedder-Policy": "require-corp",
      "Cross-Origin-Resource-Policy": "same-origin",
    },
  },
});
