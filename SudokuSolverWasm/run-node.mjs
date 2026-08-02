#!/usr/bin/env node
// Headless driver for the WASM solver, using the same runtime the browser loads.
//
// Node and Chrome share V8's WebAssembly engine, so this is a faithful stand-in for measuring the
// WASM tax, and unlike the browser page it is scriptable and diffable against the native harness.
//
//   node SudokuSolverWasm/run-node.mjs <bundleDir> solve <fpuzzles-url>
//   node SudokuSolverWasm/run-node.mjs <bundleDir> bench [--iterations N] [--filter TEXT]
//                                                        [--multithread] [--save FILE]
//
// <bundleDir> is the published/built wwwroot, e.g.
//   SudokuSolverWasm/bin/Release/net10.0-browser/publish/wwwroot

import { pathToFileURL } from 'node:url';
import path from 'node:path';
import fs from 'node:fs';

const [bundleDir, mode, ...rest] = process.argv.slice(2);
if (!bundleDir || !mode) {
    console.error('usage: run-node.mjs <bundleDir> <solve|bench> [...]');
    process.exit(2);
}

const originalCwd = process.cwd();
const frameworkEntry = path.resolve(bundleDir, '_framework/dotnet.js');
if (!fs.existsSync(frameworkEntry)) {
    console.error(`No runtime at ${frameworkEntry} — build or publish the project first.`);
    process.exit(2);
}

// The runtime resolves its assets relative to the process working directory.
process.chdir(path.resolve(bundleDir));

const { dotnet } = await import(pathToFileURL(frameworkEntry).href);

const responses = [];
const { setModuleImports, getAssemblyExports, getConfig } = await dotnet
    .withDiagnosticTracing(false)
    .create();

setModuleImports('solver', {
    sendResponse: (json) => responses.push(JSON.parse(json)),
});

const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);
const solver = exports.SudokuSolverWasm.SolverInterop;
const bench = exports.SudokuSolverWasm.BenchInterop;

// Deliberately no dotnet.run(): Main returning would tear the runtime down (and exit Node).
// This module is a library, driven entirely through its exports.

const info = JSON.parse(solver.GetRuntimeInfo());
solver.Initialize(!info.threadsEnabled);
console.error(
    `runtime: .NET ${info.runtimeVersion} · ${info.osDescription} · ` +
    `threads ${info.threadsEnabled ? 'on' : 'off'} · ${info.processorCount} cpus`
);

if (mode === 'solve' || mode === 'count' || mode === 'check' || mode === 'solvepath' ||
    mode === 'step' || mode === 'truecandidates') {
    const data = rest[0];
    if (!data) {
        console.error('Pass an f-puzzles URL.');
        process.exit(2);
    }
    const started = performance.now();
    if (info.threadsEnabled) {
        await solver.HandleMessageAsync(JSON.stringify({ nonce: 1, command: mode, dataType: 'fpuzzles', data }));
    } else {
        solver.HandleMessage(JSON.stringify({ nonce: 1, command: mode, dataType: 'fpuzzles', data }));
    }
    console.error(`elapsed ${(performance.now() - started).toFixed(1)} ms`);
    console.log(JSON.stringify(responses, null, 2));
    process.exit(0);
}

if (mode === 'bench') {
    let iterations = 3;
    let filter = null;
    let multiThread = false;
    let savePath = null;
    for (let i = 0; i < rest.length; i++) {
        if (rest[i] === '--iterations') iterations = Number(rest[++i]);
        else if (rest[i] === '--filter') filter = rest[++i];
        else if (rest[i] === '--multithread') multiThread = true;
        else if (rest[i] === '--save') savePath = rest[++i];
    }

    if (multiThread && !info.threadsEnabled) {
        console.error('This bundle has threads disabled — publish with -p:WasmEnableThreads=true.');
        process.exit(2);
    }

    let cases = JSON.parse(fs.readFileSync('corpus.json', 'utf8'));
    if (filter) {
        const needle = filter.toLowerCase();
        cases = cases.filter(
            (c) => c.name.toLowerCase().includes(needle) || (c.category ?? '').toLowerCase().includes(needle)
        );
    }

    console.error(`iterations=${iterations}  multithread=${multiThread}  cases=${cases.length}`);
    console.error(
        `${'name'.padEnd(24)}${'op'.padEnd(7)}${'result'.padStart(13)}  ${'ok'.padEnd(4)}` +
        `${'min ms'.padStart(10)}${'med ms'.padStart(10)}${'alloc MB'.padStart(10)}`
    );
    console.error('-'.repeat(78));

    const results = [];
    let anyFail = false;
    for (const benchCase of cases) {
        let result;
        try {
            result = JSON.parse(bench.RunCase(JSON.stringify(benchCase), iterations, multiThread));
        } catch (err) {
            console.error(`${benchCase.name.padEnd(24)}ERROR  ${err}`);
            anyFail = true;
            continue;
        }
        results.push(result);
        if (!result.ok) anyFail = true;
        console.error(
            `${result.name.padEnd(24)}${result.op.padEnd(7)}${String(result.result).padStart(13)}  ` +
            `${(result.ok ? 'ok' : 'FAIL').padEnd(4)}${result.minMs.toFixed(2).padStart(10)}` +
            `${result.medianMs.toFixed(2).padStart(10)}${result.allocMB.toFixed(2).padStart(10)}`
        );
    }

    console.error('-'.repeat(78));
    console.error(`total min ms: ${results.reduce((s, r) => s + r.minMs, 0).toFixed(1)}`);

    if (savePath) {
        fs.writeFileSync(path.resolve(originalCwd, savePath), JSON.stringify(results, null, 2));
        console.error(`saved: ${savePath}`);
    }
    process.exit(anyFail ? 1 : 0);
}

console.error(`Unknown mode: ${mode}`);
process.exit(2);
