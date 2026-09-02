import { spawnSync } from "node:child_process";
import {
  cpSync,
  existsSync,
  mkdirSync,
  rmSync,
} from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, "..");
const repositoryRoot = path.resolve(webRoot, "..");
const buildRoot = resolveGeneratedPath(
  webRoot,
  [".wasm-build"],
  "WASM build directory",
);
const publicSolverRoot = resolveGeneratedPath(
  webRoot,
  ["public", "solver"],
  "public solver directory",
);
const sourceFramework = path.join(buildRoot, "wwwroot", "_framework");
const targetFramework = path.join(publicSolverRoot, "_framework");
const project = path.join(
  repositoryRoot,
  "SudokuSolverWasm",
  "SudokuSolverWasm.csproj",
);

rmSync(buildRoot, { recursive: true, force: true });
mkdirSync(buildRoot, { recursive: true });

const publish = spawnSync(
  "dotnet",
  [
    "publish",
    project,
    "-c",
    "Debug",
    "-p:WasmEnableHotReload=false",
    "-o",
    buildRoot,
  ],
  { cwd: repositoryRoot, stdio: "inherit", shell: false },
);
if (publish.error !== undefined) {
  throw publish.error;
}
if (publish.status !== 0) {
  throw new Error(`dotnet publish failed with exit code ${publish.status}`);
}

const sourceEntrypoint = path.join(sourceFramework, "dotnet.js");
if (!existsSync(sourceEntrypoint)) {
  throw new Error(`Published WASM runtime is missing ${sourceEntrypoint}`);
}

mkdirSync(publicSolverRoot, { recursive: true });
rmSync(targetFramework, { recursive: true, force: true });
cpSync(sourceFramework, targetFramework, { recursive: true });

const targetEntrypoint = path.join(targetFramework, "dotnet.js");
if (!existsSync(targetEntrypoint)) {
  throw new Error(`Copied WASM runtime is missing ${targetEntrypoint}`);
}

console.log(`Published WASM runtime to ${targetFramework}`);

function resolveGeneratedPath(root, segments, label) {
  const resolvedRoot = path.resolve(root);
  const resolvedCandidate = path.resolve(root, ...segments);
  const expectedRelativePath = path.join(...segments);
  const actualRelativePath = path.relative(resolvedRoot, resolvedCandidate);
  if (actualRelativePath !== expectedRelativePath) {
    throw new Error(
      `${label} resolved outside its exact generated path: ${resolvedCandidate}`,
    );
  }

  const parsed = path.parse(resolvedCandidate);
  if (resolvedCandidate === parsed.root) {
    throw new Error(`${label} must not be a filesystem root`);
  }
  return resolvedCandidate;
}
