// Hosts the .NET WASM runtime off the UI thread and relays the solver's JSON protocol.
//
// Page -> worker:  {kind:'message', payload}  {kind:'cancel'}
//                  {kind:'bench', caseJson, iterations, multiThread}
// Worker -> page:  {kind:'ready', info}  {kind:'response', payload}  {kind:'done', elapsedMs}
//                  {kind:'benchResult', payload}  {kind:'error', message}
//
// `payload` is exactly the JSON the native websocket server sends and receives, so a client can
// be pointed at either transport.

import { dotnet } from './_framework/dotnet.js';

let solver = null;
let bench = null;
let runtimeInfo = null;
const queued = [];

const post = (msg) => self.postMessage(msg);

async function boot() {
    const { setModuleImports, getAssemblyExports, getConfig } = await dotnet
        .withDiagnosticTracing(false)
        .create();

    setModuleImports('solver', {
        sendResponse: (json) => post({ kind: 'response', payload: json }),
    });

    const config = getConfig();
    const exports = await getAssemblyExports(config.mainAssemblyName);
    solver = exports.SudokuSolverWasm.SolverInterop;
    bench = exports.SudokuSolverWasm.BenchInterop;

    // Deliberately no dotnet.run(): Main returning shuts the runtime down. This is a library.

    runtimeInfo = JSON.parse(solver.GetRuntimeInfo());
    // A threads-enabled build lets the solver use its multi-threaded brute force.
    solver.Initialize(!runtimeInfo.threadsEnabled);

    post({ kind: 'ready', info: runtimeInfo });
    for (const msg of queued.splice(0)) {
        await handle(msg);
    }
}

async function handle(msg) {
    if (msg.kind === 'cancel') {
        // Only reachable in threads-enabled builds; otherwise the worker is blocked inside
        // HandleMessage and the page terminates us instead.
        solver.Cancel();
        return;
    }

    if (msg.kind === 'bench') {
        try {
            post({
                kind: 'benchResult',
                payload: bench.RunCase(msg.caseJson, msg.iterations, msg.multiThread),
            });
        } catch (err) {
            post({ kind: 'error', message: `${msg.name}: ${err}` });
        }
        return;
    }

    if (msg.kind !== 'message') {
        return;
    }

    const started = performance.now();
    try {
        if (runtimeInfo.threadsEnabled) {
            await solver.HandleMessageAsync(msg.payload);
        } else {
            solver.HandleMessage(msg.payload);
        }
        post({ kind: 'done', elapsedMs: performance.now() - started });
    } catch (err) {
        post({ kind: 'error', message: String(err && err.stack ? err.stack : err) });
    }
}

self.onmessage = async (e) => {
    if (!solver) {
        queued.push(e.data);
        return;
    }
    await handle(e.data);
};

boot().catch((err) => post({ kind: 'error', message: `Boot failed: ${err}` }));
