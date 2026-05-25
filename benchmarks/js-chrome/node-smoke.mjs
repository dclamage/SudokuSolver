import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

import { runArrowSolve } from './solver.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const defaultPuzzlePath = resolve(here, '../../iss-arrow-data.txt');
const puzzlePath = process.argv[2] ? resolve(process.argv[2]) : defaultPuzzlePath;
const issText = await readFile(puzzlePath, 'utf8');

const result = runArrowSolve(issText, {
  maxSolutions: Number(process.env.MAX_SOLUTIONS ?? 0),
  traceLimit: Number(process.env.TRACE_LIMIT ?? 0),
});

console.log(JSON.stringify(result, null, 2));