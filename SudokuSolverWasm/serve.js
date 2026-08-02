#!/usr/bin/env node
// Minimal static server for the WASM testbed. No dependencies.
//
// It sets COOP/COEP so the page is cross-origin isolated, which SharedArrayBuffer — and therefore
// any threads-enabled .NET WASM build — requires. Single-threaded builds work fine with the
// headers too, so one server covers both.
//
//   node SudokuSolverWasm/serve.js <root> [port]

const http = require('http');
const fs = require('fs');
const path = require('path');

const root = path.resolve(process.argv[2] ?? '.');
const port = Number(process.argv[3] ?? 8080);

const MIME = {
    '.html': 'text/html; charset=utf-8',
    '.js': 'text/javascript; charset=utf-8',
    '.mjs': 'text/javascript; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.wasm': 'application/wasm',
    '.dat': 'application/octet-stream',
    '.dll': 'application/octet-stream',
    '.pdb': 'application/octet-stream',
    '.blat': 'application/octet-stream',
    '.svg': 'image/svg+xml',
    '.woff2': 'font/woff2',
};

http.createServer((req, res) => {
    const url = decodeURIComponent(req.url.split('?')[0]);
    let filePath = path.join(root, url === '/' ? '/index.html' : url);

    // Keep requests inside the served root.
    if (!filePath.startsWith(root)) {
        res.writeHead(403).end('Forbidden');
        return;
    }

    fs.readFile(filePath, (err, data) => {
        if (err) {
            res.writeHead(404, { 'Content-Type': 'text/plain' }).end(`Not found: ${url}`);
            return;
        }
        res.writeHead(200, {
            'Content-Type': MIME[path.extname(filePath).toLowerCase()] ?? 'application/octet-stream',
            'Cross-Origin-Opener-Policy': 'same-origin',
            'Cross-Origin-Embedder-Policy': 'require-corp',
            'Cross-Origin-Resource-Policy': 'same-origin',
            'Cache-Control': 'no-store',
        }).end(data);
    });
}).listen(port, () => {
    console.log(`Serving ${root}`);
    console.log(`  http://localhost:${port}/         testbed`);
    console.log(`  http://localhost:${port}/bench.html  benchmark`);
});
