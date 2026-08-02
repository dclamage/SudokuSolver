#!/usr/bin/env node
// Runs the benchmark page in headless Chrome and prints the results as JSON.
//
// Needed because multi-threaded .NET WASM refuses to start outside a real browser
// ("This build of dotnet is multi-threaded, it doesn't support shell environments like V8 or
// NodeJS"), so run-node.mjs can only measure single-threaded bundles. This drives Chrome over the
// DevTools protocol with no npm dependencies — Node's built-in WebSocket is enough.
//
//   node SudokuSolverWasm/drive-chrome.mjs <bundleDir> [--iterations N] [--filter TEXT]
//                                          [--exclude TEXT] [--multithread] [--save FILE] [--port N]

import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';

const CHROME = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';

const args = process.argv.slice(2);
const bundleDir = args[0];
if (!bundleDir || bundleDir.startsWith('--')) {
    console.error('usage: drive-chrome.mjs <bundleDir> [--iterations N] [--filter TEXT] [--multithread] [--save FILE]');
    process.exit(2);
}

let iterations = 3;
let filter = null;
let exclude = null;
let bitOps = false;
let multiThread = false;
let savePath = null;
let servePort = 8123;
let debugPort = 9222;
for (let i = 1; i < args.length; i++) {
    if (args[i] === '--iterations') iterations = Number(args[++i]);
    else if (args[i] === '--filter') filter = args[++i];
    else if (args[i] === '--exclude') exclude = args[++i];
    else if (args[i] === '--bitops') bitOps = true;
    else if (args[i] === '--multithread') multiThread = true;
    else if (args[i] === '--save') savePath = args[++i];
    else if (args[i] === '--port') servePort = Number(args[++i]);
    else if (args[i] === '--debug-port') debugPort = Number(args[++i]);
}

const root = path.resolve(bundleDir);
if (!fs.existsSync(path.join(root, '_framework/dotnet.js'))) {
    console.error(`No runtime at ${root}/_framework/dotnet.js — publish the project first.`);
    process.exit(2);
}

// --- static server with the cross-origin isolation headers SharedArrayBuffer needs -------------

const MIME = {
    '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8',
    '.mjs': 'text/javascript; charset=utf-8', '.json': 'application/json; charset=utf-8',
    '.css': 'text/css; charset=utf-8', '.wasm': 'application/wasm',
};
const server = http.createServer((req, res) => {
    const url = decodeURIComponent(req.url.split('?')[0]);
    const filePath = path.join(root, url === '/' ? '/index.html' : url);
    if (!filePath.startsWith(root)) return void res.writeHead(403).end();
    fs.readFile(filePath, (err, data) => {
        if (err) return void res.writeHead(404).end();
        res.writeHead(200, {
            'Content-Type': MIME[path.extname(filePath).toLowerCase()] ?? 'application/octet-stream',
            'Cross-Origin-Opener-Policy': 'same-origin',
            'Cross-Origin-Embedder-Policy': 'require-corp',
            'Cross-Origin-Resource-Policy': 'same-origin',
            'Cache-Control': 'no-store',
        }).end(data);
    });
});
await new Promise((r) => server.listen(servePort, r));

// --- chrome ------------------------------------------------------------------------------------

const profileDir = fs.mkdtempSync(path.join(os.tmpdir(), 'wasmbench-'));
const query = new URLSearchParams({ auto: '1', iterations: String(iterations) });
if (multiThread) query.set('multithread', '1');
if (filter) query.set('filter', filter);
if (exclude) query.set('exclude', exclude);
if (bitOps) query.set('bitops', '1');
const pageUrl = `http://localhost:${servePort}/bench.html?${query}`;

const chrome = spawn(CHROME, [
    '--headless=new',
    `--remote-debugging-port=${debugPort}`,
    `--user-data-dir=${profileDir}`,
    '--no-first-run',
    '--no-default-browser-check',
    '--disable-background-timer-throttling',
    '--disable-renderer-backgrounding',
    '--disable-backgrounding-occluded-windows',
    pageUrl,
], { stdio: ['ignore', 'ignore', 'pipe'] });

const cleanup = () => {
    try { chrome.kill(); } catch { }
    try { server.close(); } catch { }
    try { fs.rmSync(profileDir, { recursive: true, force: true }); } catch { }
};
process.on('exit', cleanup);
process.on('SIGINT', () => { cleanup(); process.exit(130); });

const getJson = async (route) => {
    for (let attempt = 0; attempt < 100; attempt++) {
        try {
            const res = await fetch(`http://localhost:${debugPort}${route}`);
            if (res.ok) return await res.json();
        } catch { }
        await new Promise((r) => setTimeout(r, 200));
    }
    throw new Error(`Chrome DevTools endpoint ${route} never came up`);
};

await getJson('/json/version');

// Find the bench page target.
let pageTarget = null;
for (let attempt = 0; attempt < 100 && !pageTarget; attempt++) {
    const targets = await getJson('/json/list');
    pageTarget = targets.find((t) => t.type === 'page' && t.url.includes('bench.html'));
    if (!pageTarget) await new Promise((r) => setTimeout(r, 200));
}
if (!pageTarget) throw new Error('bench.html page target not found');

const ws = new WebSocket(pageTarget.webSocketDebuggerUrl);
await new Promise((resolve, reject) => {
    ws.addEventListener('open', resolve, { once: true });
    ws.addEventListener('error', reject, { once: true });
});

let nextId = 1;
const pendingCalls = new Map();
ws.addEventListener('message', (event) => {
    const msg = JSON.parse(event.data);
    if (msg.id && pendingCalls.has(msg.id)) {
        pendingCalls.get(msg.id)(msg);
        pendingCalls.delete(msg.id);
    } else if (msg.method === 'Runtime.consoleAPICalled') {
        const text = msg.params.args.map((a) => a.value ?? a.description ?? '').join(' ');
        if (text) console.error(`[page] ${text}`);
    } else if (msg.method === 'Runtime.exceptionThrown') {
        console.error(`[page error] ${msg.params.exceptionDetails.text} ${msg.params.exceptionDetails.exception?.description ?? ''}`);
    }
});

const send = (method, params = {}) =>
    new Promise((resolve) => {
        const id = nextId++;
        pendingCalls.set(id, resolve);
        ws.send(JSON.stringify({ id, method, params }));
    });

await send('Runtime.enable');

const evaluate = async (expression) => {
    const res = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
    return res.result?.result?.value;
};

// Wait for the page to finish its run. Threads-enabled AOT boots slowly, and the corpus itself
// takes minutes, so the ceiling here is generous.
const deadline = Date.now() + 45 * 60 * 1000;
let done = false;
let reportedLines = 0;
while (Date.now() < deadline) {
    done = await evaluate('!!window.__benchDone');

    // Mirror the page's log as it fills so a slow or stalled run is visible rather than silent.
    const pageLog = (await evaluate('Array.from(document.getElementById("log").children).map(e => e.textContent).join("\\n")')) ?? '';
    const lines = pageLog.split('\n').filter(Boolean);
    for (const line of lines.slice(reportedLines)) console.error(line);
    reportedLines = lines.length;

    if (done) break;
    await new Promise((r) => setTimeout(r, 1000));
}
if (!done) {
    console.error('Timed out waiting for the benchmark to finish.');
    process.exit(1);
}

const pageError = await evaluate('window.__benchError ?? null');
if (pageError) {
    console.error(`Benchmark failed: ${pageError}`);
    const pageLog = await evaluate('Array.from(document.getElementById("log").children).map(e => e.textContent).join("\\n")');
    if (pageLog) console.error(`--- page log ---\n${pageLog}`);
    process.exit(1);
}

const info = await evaluate('document.getElementById("runtime").textContent');
console.error(`runtime: ${info}`);

const results = await evaluate('JSON.stringify(window.__benchResults ?? [])');
const parsed = JSON.parse(results);

console.error(
    `${'name'.padEnd(24)}${'op'.padEnd(7)}${'result'.padStart(13)}  ${'ok'.padEnd(4)}` +
    `${'min ms'.padStart(10)}${'med ms'.padStart(10)}`
);
console.error('-'.repeat(68));
for (const r of parsed) {
    console.error(
        `${r.name.padEnd(24)}${r.op.padEnd(7)}${String(r.result).padStart(13)}  ` +
        `${(r.ok ? 'ok' : 'FAIL').padEnd(4)}${r.minMs.toFixed(2).padStart(10)}${r.medianMs.toFixed(2).padStart(10)}`
    );
}
console.error('-'.repeat(68));
console.error(`total min ms: ${parsed.reduce((s, r) => s + r.minMs, 0).toFixed(1)}`);

if (savePath) {
    fs.writeFileSync(path.resolve(savePath), JSON.stringify(parsed, null, 2));
    console.error(`saved: ${savePath}`);
}

console.log(JSON.stringify(parsed, null, 2));
cleanup();
process.exit(parsed.some((r) => !r.ok) ? 1 : 0);
