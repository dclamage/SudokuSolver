#!/usr/bin/env node
// Diffs a native benchmark run against a WASM one, case by case.
//
//   node SudokuSolverWasm/compare.js <native.json> <wasm.json>
//
// The native harness writes PascalCase keys and the WASM page writes camelCase, so keys are
// normalized before comparing. Only compare runs made with the same iteration count and the same
// threading mode — 1T against 1T, MT against MT.

const fs = require('fs');

const load = (path) =>
    JSON.parse(fs.readFileSync(path, 'utf8')).map((r) => {
        const out = {};
        for (const [k, v] of Object.entries(r)) out[k[0].toLowerCase() + k.slice(1)] = v;
        return out;
    });

const [nativePath, wasmPath] = process.argv.slice(2);
if (!nativePath || !wasmPath) {
    console.error('usage: node compare.js <native.json> <wasm.json>');
    process.exit(2);
}

const native = load(nativePath);
const wasm = new Map(load(wasmPath).map((r) => [r.name, r]));

console.log(`${'case'.padEnd(24)}${'native ms'.padStart(11)}${'wasm ms'.padStart(11)}${'ratio'.padStart(9)}   result`);
console.log('-'.repeat(72));

const ratios = [];
let mismatches = 0;
// Node counts are the stronger agreement check: two runs can produce the same answer while
// searching different trees, and only this notices. Collected separately from `result` because a
// divergence here is a lead, not a proven defect — see the note printed at the end.
const nodeDivergences = [];
let comparedNodes = 0;

for (const n of native) {
    const w = wasm.get(n.name);
    if (!w) {
        console.log(`${n.name.padEnd(24)}${n.minMs.toFixed(2).padStart(11)}${'—'.padStart(11)}${'—'.padStart(9)}   (not run in wasm)`);
        continue;
    }

    const ratio = n.minMs > 0 ? w.minMs / n.minMs : NaN;
    ratios.push(ratio);

    const agree = String(n.result) === String(w.result);
    if (!agree) mismatches++;

    // `estimate` samples random paths, so its counts differ from themselves and prove nothing.
    if (n.op !== 'estimate' && typeof n.nodes === 'number' && typeof w.nodes === 'number') {
        comparedNodes++;
        if (n.nodes !== w.nodes) nodeDivergences.push(`${n.name} native=${n.nodes} wasm=${w.nodes}`);
    }

    console.log(
        `${n.name.padEnd(24)}${n.minMs.toFixed(2).padStart(11)}${w.minMs.toFixed(2).padStart(11)}` +
        `${(ratio.toFixed(2) + '×').padStart(9)}   ${agree ? 'match' : `MISMATCH native=${n.result} wasm=${w.result}`}`
    );
}

console.log('-'.repeat(72));
if (ratios.length) {
    const sorted = [...ratios].sort((a, b) => a - b);
    const geomean = Math.exp(ratios.reduce((s, r) => s + Math.log(r), 0) / ratios.length);
    console.log(`wasm / native  geomean ${geomean.toFixed(2)}×   median ${sorted[sorted.length >> 1].toFixed(2)}×   ` +
        `best ${sorted[0].toFixed(2)}×   worst ${sorted[sorted.length - 1].toFixed(2)}×`);
}
if (comparedNodes) {
    const same = comparedNodes - nodeDivergences.length;
    console.log(`nodes: ${same}/${comparedNodes} case(s) explored an identical search tree`);
    if (nodeDivergences.length) {
        console.log(`  diverged: ${nodeDivergences.slice(0, 6).join(', ')}${nodeDivergences.length > 6 ? ', ...' : ''}`);
        console.log('  NOTE: same answer, different tree. Expected if either run was multi-threaded');
        console.log('        (conflict scores are shared and updated concurrently, so branch order');
        console.log('        varies); otherwise the two builds are not running the same search.');
    }
} else {
    console.log('nodes: not comparable (one or both runs predate node counting)');
}

if (mismatches) {
    console.error(`\n${mismatches} case(s) produced different results between native and wasm — investigate before trusting timings.`);
    process.exit(1);
}
