#!/usr/bin/env node
/**
 * Compiles an ISS-style JS constraint spec to a base64url NFA or binary lookup
 * table suitable for embedding in f-puzzles puzzle JSON.
 *
 * Usage:
 *   node scripts/compile-constraint.mjs <constraint-file.mjs>
 *   node scripts/compile-constraint.mjs <constraint-file.mjs> --num-values 9
 *
 * The constraint file must be an ES module (.mjs) exporting:
 *
 *   type      : 'nfa' | 'binary'   (default: 'nfa')
 *   numValues : number              (default: 9)
 *   name      : string              (optional display name)
 *
 *   For type='nfa', export one of:
 *     spec  : { startState, transition(state, value) => nextState | undefined,
 *               accept(state) => bool }
 *     regex : string   (ISS regex syntax, e.g. '(1|2)(3|4)')
 *
 *   For type='binary', export:
 *     isAllowed : (v1, v2) => bool
 *       where v1 is the value of the first cell and v2 the second (1-based).
 *
 * Output (stdout): JSON ready to paste into the puzzle's nfaConstraints[] or
 * binaryLookupConstraints[] array.  Add "cells" yourself:
 *
 *   { "cells": ["R1C1","R2C1"], "nfa": "...", "name": "..." }
 *   { "cells": ["R1C1","R2C1"], "table": "...", "name": "..." }
 *
 * The NFA compiler/serializer is local to this repo and does not require the
 * Interactive Sudoku Solver checkout.
 */

import { resolve } from 'path';
import { pathToFileURL } from 'url';
import {
    javascriptSpecToNFA,
    regexToNFA,
    NFASerializer,
} from './constraint-nfa-builder.mjs';

// ---------------------------------------------------------------------------
// Argument parsing
// ---------------------------------------------------------------------------

function usage() {
    console.error(
        'Usage: node compile-constraint.mjs <constraint-file.mjs>\n' +
        '       [--num-values <n>]'
    );
    process.exit(1);
}

const argv = process.argv.slice(2);
let constraintFile = null;
let numValuesOverride = null;

for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--num-values') {
        numValuesOverride = parseInt(argv[++i], 10);
        if (isNaN(numValuesOverride)) usage();
    } else if (!argv[i].startsWith('--')) {
        constraintFile = argv[i];
    } else {
        usage();
    }
}
if (!constraintFile) usage();

// ---------------------------------------------------------------------------
// Load the user's constraint file
// ---------------------------------------------------------------------------

const constraintUrl = pathToFileURL(resolve(process.cwd(), constraintFile)).href;
let constraint;

try {
    constraint = await import(constraintUrl);
} catch (err) {
    console.error(`Failed to import constraint file: ${constraintFile}`);
    console.error(err.message);
    process.exit(1);
}

const type       = constraint.type      ?? 'nfa';
const numValues  = numValuesOverride    ?? constraint.numValues ?? 9;
const name       = constraint.name      ?? null;

// ---------------------------------------------------------------------------
// Compile
// ---------------------------------------------------------------------------

if (type === 'nfa') {
    let nfa;
    if (constraint.regex != null) {
        nfa = regexToNFA(constraint.regex, numValues);
    } else if (constraint.spec != null) {
        nfa = javascriptSpecToNFA(constraint.spec, numValues);
    } else {
        console.error('NFA constraint file must export "spec" or "regex".');
        process.exit(1);
    }

    const encoded = NFASerializer.serialize(nfa);
    const result = { type: 'nfa', nfa: encoded };
    if (name) result.name = name;
    console.log(JSON.stringify(result, null, 2));

} else if (type === 'binary') {
    if (typeof constraint.isAllowed !== 'function') {
        console.error('Binary constraint file must export isAllowed(v1, v2).');
        process.exit(1);
    }

    // tableAB[v1-1] = bitmask of allowed v2 values when cell0 = v1
    const table = new Uint32Array(numValues);
    for (let v1 = 1; v1 <= numValues; v1++) {
        let mask = 0;
        for (let v2 = 1; v2 <= numValues; v2++) {
            if (constraint.isAllowed(v1, v2)) mask |= (1 << (v2 - 1));
        }
        table[v1 - 1] = mask;
    }

    // Little-endian uint32 array → base64url (no padding)
    const b64 = Buffer.from(table.buffer)
        .toString('base64')
        .replace(/\+/g, '-')
        .replace(/\//g, '_')
        .replace(/=/g, '');

    const result = { type: 'binary', table: b64 };
    if (name) result.name = name;
    console.log(JSON.stringify(result, null, 2));

} else {
    console.error(`Unknown type: "${type}". Must be "nfa" or "binary".`);
    process.exit(1);
}
