import { readFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { fileURLToPath } from 'node:url';
import { dirname, extname, normalize, resolve, sep } from 'node:path';

import puppeteer from 'puppeteer-core';

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, '../..');
const defaultPuzzlePath = resolve(repoRoot, 'iss-arrow-data.txt');

const args = parseArgs(process.argv.slice(2));
const issText = await readFile(args.puzzle ?? defaultPuzzlePath, 'utf8');
const server = await startStaticServer(repoRoot);
const browser = await puppeteer.launch({
  channel: args.channel,
  executablePath: process.env.CHROME_PATH || undefined,
  headless: args.headless,
  args: ['--disable-background-timer-throttling', '--disable-renderer-backgrounding'],
});

try {
  const page = await browser.newPage();
  page.on('console', message => {
    if (message.text().startsWith('Worker initialized in ')) return;
    console.error(`[browser:${message.type()}] ${message.text()}`);
  });
  page.on('pageerror', error => {
    console.error(`[browser:pageerror] ${error.stack || error.message}`);
  });

  await page.goto(`http://127.0.0.1:${server.port}/benchmarks/js-chrome/iss-baseline.html`);
  await page.waitForFunction('typeof globalThis.runIssBenchmark === "function"');

  const result = await page.evaluate(
    async ({ text, options }) => globalThis.runIssBenchmark(text, options),
    {
      text: issText,
      options: {
        warmup: args.warmup,
        samples: args.samples,
        maxSolutions: args.maxSolutions,
        traceHash: args.traceHash,
      },
    });

  result.environment = await page.evaluate(() => ({
    userAgent: navigator.userAgent,
    hardwareConcurrency: navigator.hardwareConcurrency,
  }));

  console.log(JSON.stringify(result, null, 2));
} finally {
  await browser.close();
  await new Promise(resolveClose => server.instance.close(resolveClose));
}

async function startStaticServer(root) {
  const rootPath = normalize(root);
  const instance = createServer(async (request, response) => {
    try {
      const url = new URL(request.url, 'http://127.0.0.1');
      if (url.pathname === '/favicon.ico') {
        response.writeHead(204).end();
        return;
      }
      const pathname = url.pathname === '/' ? '/benchmarks/js-chrome/iss-baseline.html' : decodeURIComponent(url.pathname);
      const filePath = normalize(resolve(rootPath, `.${pathname}`));
      if (filePath !== rootPath && !filePath.startsWith(rootPath + sep)) {
        response.writeHead(403).end('Forbidden');
        return;
      }

      const content = await readFile(filePath);
      response.writeHead(200, { 'content-type': contentType(filePath) });
      response.end(content);
    } catch (error) {
      response.writeHead(404).end('Not found');
    }
  });

  await new Promise(resolveListen => instance.listen(0, '127.0.0.1', resolveListen));
  return { instance, port: instance.address().port };
}

function contentType(filePath) {
  switch (extname(filePath)) {
    case '.html': return 'text/html; charset=utf-8';
    case '.mjs': return 'text/javascript; charset=utf-8';
    case '.js': return 'text/javascript; charset=utf-8';
    case '.json': return 'application/json; charset=utf-8';
    default: return 'application/octet-stream';
  }
}

function parseArgs(argv) {
  const result = {
    puzzle: null,
    warmup: 5,
    samples: 20,
    maxSolutions: 0,
    traceHash: true,
    channel: 'chrome',
    headless: 'new',
  };

  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '--puzzle') result.puzzle = resolve(argv[++i]);
    else if (arg.startsWith('--puzzle=')) result.puzzle = resolve(arg.slice('--puzzle='.length));
    else if (arg === '--warmup') result.warmup = Number(argv[++i]);
    else if (arg.startsWith('--warmup=')) result.warmup = Number(arg.slice('--warmup='.length));
    else if (arg === '--samples') result.samples = Number(argv[++i]);
    else if (arg.startsWith('--samples=')) result.samples = Number(arg.slice('--samples='.length));
    else if (arg === '--max-solutions') result.maxSolutions = Number(argv[++i]);
    else if (arg.startsWith('--max-solutions=')) result.maxSolutions = Number(arg.slice('--max-solutions='.length));
    else if (arg === '--no-trace-hash') result.traceHash = false;
    else if (arg === '--channel') result.channel = argv[++i] || 'chrome';
    else if (arg.startsWith('--channel=')) result.channel = arg.slice('--channel='.length) || 'chrome';
    else if (arg === '--headed') result.headless = false;
    else if (arg === '--help' || arg === '-h') {
      console.log('Usage: node iss-baseline-runner.mjs [--puzzle path] [--warmup n] [--samples n] [--max-solutions n] [--no-trace-hash] [--channel chrome] [--headed]');
      process.exit(0);
    }
  }

  return result;
}