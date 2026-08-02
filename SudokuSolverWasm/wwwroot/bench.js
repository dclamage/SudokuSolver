// Drives the corpus through the WASM build and emits results in the same shape the native
// harness's --save produces, so the two can be diffed directly.
//
// Unlike the testbed page, this boots the runtime on the *main thread* rather than in a worker:
// threads-enabled .NET WASM cannot start inside a worker, and this page has to run both bundles.
// It therefore uses only Task-returning exports, which is the calling convention MT requires.
// Blocking the page during a case is acceptable here — nothing else is happening.

import { dotnet } from './_framework/dotnet.js';

const logEl = document.getElementById('log');
const statusEl = document.getElementById('status');
const runtimeEl = document.getElementById('runtime');
const runButton = document.getElementById('run');
const exportButton = document.getElementById('export');

let solver = null;
let bench = null;
let runtimeInfo = null;
let results = [];

function log(text, kind = 'info') {
    const line = document.createElement('div');
    line.className = `line ${kind}`;
    line.textContent = text;
    logEl.appendChild(line);
    logEl.scrollTop = logEl.scrollHeight;
}

async function boot() {
    const { setModuleImports, getAssemblyExports, getConfig } = await dotnet
        .withDiagnosticTracing(false)
        .create();

    setModuleImports('solver', { sendResponse: () => { } });

    const config = getConfig();
    const exports = await getAssemblyExports(config.mainAssemblyName);
    solver = exports.SudokuSolverWasm.SolverInterop;
    bench = exports.SudokuSolverWasm.BenchInterop;

    runtimeInfo = JSON.parse(await solver.GetRuntimeInfoAsync());
    await solver.InitializeAsync(!runtimeInfo.threadsEnabled);

    runtimeEl.textContent =
        `.NET ${runtimeInfo.runtimeVersion} · ${runtimeInfo.osDescription} · ` +
        `threads ${runtimeInfo.threadsEnabled ? 'on' : 'off'} · ` +
        `${runtimeInfo.processorCount} logical cpu${runtimeInfo.processorCount === 1 ? '' : 's'}`;
    statusEl.textContent = 'idle';
    runButton.disabled = false;
}

async function run() {
    const iterations = Number(document.getElementById('iterations').value);
    const multiThread = document.getElementById('multithread').checked;
    const filter = document.getElementById('filter').value.trim().toLowerCase();
    const exclude = (new URLSearchParams(location.search).get('exclude') ?? '').trim().toLowerCase();

    if (multiThread && !runtimeInfo.threadsEnabled) {
        throw new Error('This build has threads disabled — publish with -p:WasmEnableThreads=true.');
    }

    runButton.disabled = true;
    exportButton.disabled = true;
    logEl.textContent = '';
    results = [];

    let cases = await (await fetch('corpus.json')).json();
    if (filter) {
        cases = cases.filter(
            (c) => c.name.toLowerCase().includes(filter) || (c.category ?? '').toLowerCase().includes(filter)
        );
    }
    if (exclude) {
        cases = cases.filter(
            (c) => !c.name.toLowerCase().includes(exclude) && !(c.category ?? '').toLowerCase().includes(exclude)
        );
    }

    log(`iterations=${iterations}  multithread=${multiThread}  cases=${cases.length}`, 'meta');
    log('name                    op         result  ok      min ms    med ms  alloc MB', 'meta');
    log('-'.repeat(78), 'meta');

    for (const benchCase of cases) {
        statusEl.textContent = `${benchCase.name}…`;
        statusEl.classList.add('busy');
        // Yield so the status paints before the runtime takes the thread.
        await new Promise((r) => setTimeout(r, 0));

        let result;
        try {
            result = JSON.parse(await bench.RunCaseAsync(JSON.stringify(benchCase), iterations, multiThread));
        } catch (err) {
            log(`${benchCase.name}: ${err}`, 'error');
            continue;
        }

        results.push(result);
        log(
            `${result.name.padEnd(24)}${result.op.padEnd(7)}${String(result.result).padStart(13)}  ` +
            `${(result.ok ? 'ok' : 'FAIL').padEnd(4)}${result.minMs.toFixed(2).padStart(10)}` +
            `${result.medianMs.toFixed(2).padStart(10)}${result.allocMB.toFixed(2).padStart(10)}`,
            result.ok ? 'info' : 'error'
        );
    }

    log('-'.repeat(78), 'meta');
    log(`total min ms: ${results.reduce((s, r) => s + r.minMs, 0).toFixed(1)}`, 'ok');

    statusEl.textContent = 'idle';
    statusEl.classList.remove('busy');
    runButton.disabled = false;
    exportButton.disabled = results.length === 0;

    // Signals for headless automation (drive-chrome.mjs).
    window.__benchResults = results;
    window.__benchDone = true;
}

function exportJson() {
    const blob = new Blob([JSON.stringify(results, null, 2)], { type: 'application/json' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = `wasm-${runtimeInfo.threadsEnabled ? 'mt' : '1t'}.json`;
    a.click();
    URL.revokeObjectURL(a.href);
}

// URL params let a headless driver configure and start a run without clicking:
//   bench.html?auto=1&iterations=5&multithread=1&filter=killer&exclude=blank6
function applyUrlParams() {
    const params = new URLSearchParams(location.search);
    if (params.has('iterations')) document.getElementById('iterations').value = params.get('iterations');
    if (params.has('filter')) document.getElementById('filter').value = params.get('filter');
    if (params.has('multithread')) document.getElementById('multithread').checked = params.get('multithread') !== '0';
    return params.get('auto') === '1';
}

const fail = (err) => {
    log(String(err && err.stack ? err.stack : err), 'error');
    window.__benchError = String(err);
    window.__benchDone = true;
};

runButton.disabled = true;
runButton.addEventListener('click', () => run().catch((err) => log(String(err), 'error')));
exportButton.addEventListener('click', exportJson);

async function runBitOps() {
    logEl.textContent = '';
    log('BitOperations vs software equivalents, same host:', 'meta');
    log(await bench.RunBitOpsAsync(), 'info');
    window.__benchResults = [];
    window.__benchDone = true;
}

const params = new URLSearchParams(location.search);
const autoRun = applyUrlParams();
boot()
    .then(() => (params.get('bitops') === '1' ? runBitOps() : autoRun ? run() : undefined))
    .catch(fail);
