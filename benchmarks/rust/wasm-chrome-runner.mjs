import { createRequire } from 'node:module';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, '../..');
const requireFromJsChrome = createRequire(new URL('../js-chrome/package.json', import.meta.url));
const puppeteerModuleUrl = pathToFileURL(requireFromJsChrome.resolve('puppeteer-core')).href;
const { default: puppeteer } = await import(puppeteerModuleUrl);

const options = parseArgs(process.argv.slice(2));
const puzzlePath = path.resolve(scriptDir, options.puzzle ?? '../../iss-arrow-data.txt');
const issText = await readFile(puzzlePath, 'utf8');

const server = createStaticServer(repoRoot);
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));

const { port } = server.address();
const browser = await puppeteer.launch({
  channel: options.channel,
  executablePath: process.env.CHROME_PATH || undefined,
  headless: options.headed ? false : 'new',
  args: ['--disable-background-timer-throttling'],
});

try {
  const page = await browser.newPage();
  page.on('console', msg => console.error(`[browser:${msg.type()}] ${msg.text()}`));
  page.on('pageerror', err => console.error(`[browser:error] ${err.stack || err.message}`));

  await page.goto(`http://127.0.0.1:${port}/benchmarks/rust/wasm-runner.html`, { waitUntil: 'networkidle0' });
  const result = await page.evaluate(
    async ({ text, runOptions }) => globalThis.runRustWasmBenchmark(text, runOptions),
    {
      text: issText,
      runOptions: {
        warmup: options.warmup,
        samples: options.samples,
        maxSolutions: options.maxSolutions,
        traceLimit: options.traceLimit,
      },
    },
  );

  result.environment = await page.evaluate(() => ({
    userAgent: navigator.userAgent,
    hardwareConcurrency: navigator.hardwareConcurrency,
  }));

  console.log(JSON.stringify(result, null, 2));
} finally {
  await browser.close();
  server.close();
}

function parseArgs(args) {
  const options = {
    warmup: 5,
    samples: 20,
    maxSolutions: 0,
    traceLimit: 0,
    channel: 'chrome',
    headed: false,
  };

  for (let index = 0; index < args.length; index++) {
    const arg = args[index];
    const [rawName, inlineValue] = arg.split('=', 2);
    const nextValue = () => inlineValue ?? args[++index];

    switch (rawName) {
      case '--puzzle':
        options.puzzle = nextValue();
        break;
      case '--warmup':
        options.warmup = Number(nextValue());
        break;
      case '--samples':
        options.samples = Number(nextValue());
        break;
      case '--max-solutions':
        options.maxSolutions = Number(nextValue());
        break;
      case '--trace-limit':
        options.traceLimit = Number(nextValue());
        break;
      case '--channel':
        options.channel = nextValue();
        break;
      case '--headed':
        options.headed = true;
        break;
      default:
        throw new Error(`Unknown argument: ${rawName}`);
    }
  }

  return options;
}

function createStaticServer(root) {
  return createServer(async (request, response) => {
    try {
      const requestUrl = new URL(request.url, 'http://localhost');
      if (requestUrl.pathname === '/favicon.ico') {
        response.writeHead(204).end();
        return;
      }

      let filePath = path.normalize(decodeURIComponent(requestUrl.pathname)).replace(/^([/\\])+/, '');
      if (filePath === '') filePath = 'index.html';

      const absolutePath = path.resolve(root, filePath);
      if (!absolutePath.startsWith(root)) {
        response.writeHead(403).end('Forbidden');
        return;
      }

      const data = await readFile(absolutePath);
      response.writeHead(200, { 'content-type': contentType(absolutePath) });
      response.end(data);
    } catch (error) {
      response.writeHead(404).end('Not found');
    }
  });
}

function contentType(filePath) {
  switch (path.extname(filePath)) {
    case '.html': return 'text/html; charset=utf-8';
    case '.js':
    case '.mjs': return 'text/javascript; charset=utf-8';
    case '.wasm': return 'application/wasm';
    default: return 'application/octet-stream';
  }
}