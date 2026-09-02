import { readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, "..");
const outputRoot = path.join(webRoot, ".wasm-probe-dist");
const javascriptFiles = findJavaScriptFiles(outputRoot);
const workerBundles = javascriptFiles.filter((file) => {
  const source = readFileSync(file, "utf8");
  return (
    source.includes("/solver/_framework/dotnet.js") &&
    source.includes("Initialize") &&
    source.includes("HandleMessage")
  );
});

if (workerBundles.length !== 1) {
  throw new Error(
    `Expected one emitted solver worker bundle, found ${workerBundles.length}`,
  );
}

const workerSource = readFileSync(workerBundles[0], "utf8");
if (workerSource.includes("dotnet.js?import")) {
  throw new Error("Emitted solver worker appends Vite's ?import query");
}

console.log(`Verified emitted solver worker ${path.relative(webRoot, workerBundles[0])}`);

function findJavaScriptFiles(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const candidate = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      return findJavaScriptFiles(candidate);
    }
    return entry.isFile() && candidate.endsWith(".js") ? [candidate] : [];
  });
}
