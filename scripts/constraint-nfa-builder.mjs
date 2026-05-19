/*
 * Portions of this file are adapted from Interactive Sudoku Solver's
 * js/nfa_builder.js and js/util.js.
 *
 * MIT License
 *
 * Copyright (c) 2023 sigh <https://github.com/sigh>
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

const REMOVE_STATE = -1;
const MAX_STATE_COUNT = 1 << 12;

const memoize = (fn, keyFunc = null) => {
    const map = new Map();
    return (...args) => {
        const key = keyFunc ? keyFunc(...args) :
            (args.length <= 1 ? args[0] : JSON.stringify(args));

        if (map.has(key)) return map.get(key);

        const result = fn(...args);
        map.set(key, result);
        return result;
    };
};

const requiredBits = (maxValue) => {
    if (maxValue <= 0) return 1;
    return 32 - Math.clz32(maxValue);
};

const canonicalJSON = (value) => {
    const replacer = (key, val) => {
        if (val && typeof val === 'object' && !Array.isArray(val)) {
            const sortedEntries = Object.entries(val).sort(
                (a, b) => a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0
            );
            return Object.fromEntries(sortedEntries);
        }
        return val;
    };
    return JSON.stringify(value, replacer);
};

class BitWriter {
    constructor() {
        this._bytes = [];
        this._bitLength = 0;
    }

    writeBits(value, bitCount) {
        if (!Number.isInteger(bitCount) || bitCount < 0) {
            throw new Error('Bit count must be a non-negative integer');
        }
        if (bitCount === 0) return;
        const normalized = value >>> 0;
        for (let i = bitCount - 1; i >= 0; i--) {
            const bit = (normalized >>> i) & 1;
            const byteIndex = this._bitLength >> 3;
            if (byteIndex === this._bytes.length) {
                this._bytes.push(0);
            }
            const bitIndex = 7 - (this._bitLength & 7);
            if (bit) {
                this._bytes[byteIndex] |= 1 << bitIndex;
            }
            this._bitLength++;
        }
    }

    toUint8Array() {
        return Uint8Array.from(this._bytes);
    }
}

class BitSet {
    constructor(capacity) {
        this.words = new Uint32Array(Math.ceil(capacity / 32));
    }

    add(bitIndex) {
        const wordIndex = bitIndex >>> 5;
        const mask = 1 << (bitIndex & 31);
        this.words[wordIndex] |= mask;
    }

    remove(bitIndex) {
        const wordIndex = bitIndex >>> 5;
        const mask = 1 << (bitIndex & 31);
        this.words[wordIndex] &= ~mask;
    }

    has(bitIndex) {
        const wordIndex = bitIndex >>> 5;
        const mask = 1 << (bitIndex & 31);
        return (this.words[wordIndex] & mask) !== 0;
    }
}

class NFA {
    constructor({ stateLimit = null } = {}) {
        this._startIds = new Set();
        this._acceptIds = new Set();
        this._transitions = [];
        this._epsilon = [];
        this._sealed = false;
        this._stateLimit = stateLimit;
    }

    _assertUnsealed() {
        if (this._sealed) {
            throw new Error('NFA has been sealed and cannot be modified');
        }
    }

    _assertSealed() {
        if (!this._sealed) {
            throw new Error('NFA must be sealed before processing');
        }
    }

    _assertNoEpsilon() {
        if (this._epsilon.length) {
            throw new Error('Epsilon transitions must be closed first');
        }
    }

    seal() {
        this._sealed = true;
    }

    addStartId(startId) {
        this._assertUnsealed();
        this._ensureStateExists(startId);
        this._startIds.add(startId);
    }

    addAcceptId(acceptId) {
        this._assertUnsealed();
        this._ensureStateExists(acceptId);
        this._acceptIds.add(acceptId);
    }

    addState() {
        this._assertUnsealed();
        if (this._stateLimit !== null && this._transitions.length >= this._stateLimit) {
            throw new Error(`State limit of ${this._stateLimit} exceeded.`);
        }
        this._transitions.push([]);
        return this._transitions.length - 1;
    }

    _ensureStateExists(stateId) {
        while (this._transitions.length <= stateId) {
            this.addState();
        }
    }

    addTransition(fromStateId, toStateId, ...symbols) {
        this._assertUnsealed();
        this._ensureStateExists(fromStateId);
        this._ensureStateExists(toStateId);

        const stateTransitions = this._transitions[fromStateId];
        for (const symbol of symbols) {
            if (!(symbol instanceof NFA.Symbol)) {
                throw new Error('Transitions require NFA.Symbol objects');
            }
            const symbolIndex = symbol.index;
            if (!stateTransitions[symbolIndex]) {
                stateTransitions[symbolIndex] = [toStateId];
            } else if (!stateTransitions[symbolIndex].includes(toStateId)) {
                stateTransitions[symbolIndex].push(toStateId);
            }
        }
    }

    addEpsilon(fromStateId, toStateId) {
        this._assertUnsealed();
        this._ensureStateExists(fromStateId);
        this._ensureStateExists(toStateId);

        if (!this._epsilon[fromStateId]) {
            this._epsilon[fromStateId] = [];
        }
        this._epsilon[fromStateId].push(toStateId);
    }

    static Symbol = class NFASymbol {
        constructor(value) {
            this._value = value;
        }

        get index() {
            return this._value - 1;
        }

        get value() {
            return this._value;
        }

        static all = memoize((numSymbols) => {
            const symbols = [];
            for (let value = 1; value <= numSymbols; value++) {
                symbols.push(new NFA.Symbol(value));
            }
            return symbols;
        });
    };

    isAccepting(stateId) {
        return this._acceptIds.has(stateId);
    }

    getStartIds() {
        return this._startIds;
    }

    getAcceptIds() {
        return this._acceptIds;
    }

    getStateTransitions(stateId) {
        return this._transitions[stateId] ?? [];
    }

    getEpsilons(stateId) {
        return this._epsilon[stateId] ?? [];
    }

    hasTransitions(stateId) {
        return this.getStateTransitions(stateId).length > 0;
    }

    numStates() {
        return this._transitions.length;
    }

    numSymbols() {
        let max = 0;
        for (const trans of this._transitions) {
            if (trans && trans.length > max) {
                max = trans.length;
            }
        }
        return max;
    }

    remapStates(remap) {
        this._assertSealed();
        this._assertNoEpsilon();

        const newTransitions = [];
        for (let oldIndex = 0; oldIndex < remap.length; oldIndex++) {
            const newIndex = remap[oldIndex];
            if (newIndex === REMOVE_STATE) continue;
            if (newTransitions[newIndex] !== undefined) continue;

            const stateTrans = this._transitions[oldIndex] ?? [];
            for (let symbolIndex = 0; symbolIndex < stateTrans.length; symbolIndex++) {
                const targets = stateTrans[symbolIndex];
                if (!targets) continue;

                let writeIndex = 0;
                if (targets.length === 1) {
                    const mapped = remap[targets[0]];
                    if (mapped !== REMOVE_STATE) targets[writeIndex++] = mapped;
                } else {
                    targetLoop: for (let i = 0; i < targets.length; i++) {
                        const mapped = remap[targets[i]];
                        if (mapped === REMOVE_STATE) continue;
                        for (let j = 0; j < writeIndex; j++) {
                            if (targets[j] === mapped) continue targetLoop;
                        }
                        targets[writeIndex++] = mapped;
                    }
                }
                targets.length = writeIndex;
            }
            newTransitions[newIndex] = stateTrans;
        }
        this._transitions = newTransitions;

        const newStartIds = new Set();
        for (const id of this._startIds) {
            const mapped = remap[id];
            if (mapped !== REMOVE_STATE) newStartIds.add(mapped);
        }
        this._startIds = newStartIds;

        const newAcceptIds = new Set();
        for (const id of this._acceptIds) {
            const mapped = remap[id];
            if (mapped !== REMOVE_STATE) newAcceptIds.add(mapped);
        }
        this._acceptIds = newAcceptIds;
    }

    closeOverEpsilonTransitions() {
        this._assertSealed();
        if (!this._epsilon.length) return;

        const numStates = this._transitions.length;
        for (let i = 0; i < numStates; i++) {
            if (!this._epsilon[i]?.length) continue;
            const stateTransitions = this._transitions[i];

            const visited = new Set();
            const stack = [i];
            while (stack.length) {
                const stateId = stack.pop();
                if (visited.has(stateId)) continue;
                visited.add(stateId);
                for (const epsilonTarget of this.getEpsilons(stateId)) {
                    stack.push(epsilonTarget);
                }
            }

            visited.delete(i);

            for (const targetId of visited) {
                const targetTransitions = this._transitions[targetId] ?? [];
                for (let symbolIndex = 0; symbolIndex < targetTransitions.length; symbolIndex++) {
                    const targets = targetTransitions[symbolIndex];
                    if (!targets) continue;
                    if (!stateTransitions[symbolIndex]) {
                        stateTransitions[symbolIndex] = [];
                    }
                    stateTransitions[symbolIndex].push(...targets);
                }
                if (this._acceptIds.has(targetId)) {
                    this._acceptIds.add(i);
                }
            }

            for (let symbolIndex = 0; symbolIndex < stateTransitions.length; symbolIndex++) {
                const targets = stateTransitions[symbolIndex];
                if (targets && targets.length > 1) {
                    stateTransitions[symbolIndex] = [...new Set(targets)];
                }
            }
        }

        this._epsilon = [];
    }

    _createReversed() {
        this._assertNoEpsilon();

        const numStates = this._transitions.length;
        const reversed = new NFA();

        for (let i = 0; i < numStates; i++) {
            reversed.addState();
        }

        for (let stateId = 0; stateId < numStates; stateId++) {
            const transitions = this.getStateTransitions(stateId);
            for (let symbolIndex = 0; symbolIndex < transitions.length; symbolIndex++) {
                const targets = transitions[symbolIndex];
                if (!targets) continue;
                for (const target of targets) {
                    reversed.addTransition(target, stateId, new NFA.Symbol(symbolIndex + 1));
                }
            }
        }

        for (const acceptId of this._acceptIds) {
            reversed.addStartId(acceptId);
        }
        for (const startId of this._startIds) {
            reversed.addAcceptId(startId);
        }

        reversed.seal();
        return reversed;
    }

    _computeDepthsFromStart() {
        const numStates = this._transitions.length;
        const depths = new Array(numStates).fill(Infinity);
        let currentLevel = [...this._startIds];

        for (const id of currentLevel) depths[id] = 0;

        for (let depth = 0; currentLevel.length; depth++) {
            const nextLevel = [];
            for (const stateId of currentLevel) {
                const transitions = this.getStateTransitions(stateId);
                for (let symbolIndex = 0; symbolIndex < transitions.length; symbolIndex++) {
                    const targets = transitions[symbolIndex];
                    if (!targets) continue;
                    for (const target of targets) {
                        if (depths[target] === Infinity) {
                            depths[target] = depth + 1;
                            nextLevel.push(target);
                        }
                    }
                }
            }
            currentLevel = nextLevel;
        }
        return depths;
    }

    removeDeadStates({ maxDepth = Infinity, allStatesAreReachable = false } = {}) {
        this._assertSealed();
        this._assertNoEpsilon();

        const numStates = this.numStates();
        if (numStates === 0) return;

        const maxValidPath = 2 * numStates - 2;
        const effectiveMaxDepth = Math.min(maxDepth, maxValidPath);
        const distToAccept = this._createReversed()._computeDepthsFromStart();
        const depths = (!allStatesAreReachable || effectiveMaxDepth < maxValidPath)
            ? this._computeDepthsFromStart()
            : null;

        const deadStates = new Set();
        for (let i = 0; i < numStates; i++) {
            const d = depths ? depths[i] : 0;
            if (d + distToAccept[i] > effectiveMaxDepth) {
                deadStates.add(i);
            }
        }

        if (deadStates.size === 0) return;

        const remap = new Array(numStates).fill(REMOVE_STATE);
        let newIndex = 0;
        for (let i = 0; i < numStates; i++) {
            if (!deadStates.has(i)) {
                remap[i] = newIndex++;
            }
        }

        this.remapStates(remap);
    }

    reduceBySimulation() {
        this._assertSealed();
        this._assertNoEpsilon();

        const numStates = this._transitions.length;
        if (numStates <= 1) return;

        const numSymbols = this.numSymbols();
        if (numSymbols === 0) return;

        const sim = new Array(numStates);
        for (let a = 0; a < numStates; a++) {
            sim[a] = new BitSet(numStates);
            const aAccepts = this._acceptIds.has(a);
            for (let b = 0; b < numStates; b++) {
                if (!this._acceptIds.has(b) || aAccepts) {
                    sim[a].add(b);
                }
            }
        }

        const transitions = this._transitions;
        let changed = true;
        while (changed) {
            changed = false;
            for (let a = 0; a < numStates; a++) {
                const simA = sim[a];
                const aTrans = transitions[a] ?? [];

                for (let b = 0; b < numStates; b++) {
                    if (a === b || !simA.has(b)) continue;

                    const bTrans = transitions[b] ?? [];

                    for (let s = 0; s < numSymbols; s++) {
                        const bTargets = bTrans[s];
                        if (!bTargets || !bTargets.length) continue;

                        const aTargets = aTrans[s];

                        if (!aTargets || !aTargets.length) {
                            simA.remove(b);
                            changed = true;
                            break;
                        }

                        if (aTargets.length === 1 && bTargets.length === 1) {
                            if (!sim[aTargets[0]].has(bTargets[0])) {
                                simA.remove(b);
                                changed = true;
                                break;
                            }
                        } else if (!bTargets.every(
                            bPrime => aTargets.some(aPrime => sim[aPrime].has(bPrime)))) {
                            simA.remove(b);
                            changed = true;
                            break;
                        }
                    }
                }
            }
        }

        const dominated = (b, targets) => {
            const simB = sim[b];
            for (const a of targets) {
                if (sim[a].has(b) && (a < b || !simB.has(a))) return true;
            }
            return false;
        };
        for (let state = 0; state < numStates; state++) {
            const trans = transitions[state] ?? [];
            for (let s = 0; s < numSymbols; s++) {
                const targets = trans[s];
                if (!targets || targets.length <= 1) continue;
                for (let i = targets.length - 1; i >= 0; i--) {
                    if (dominated(targets[i], targets)) {
                        targets.splice(i, 1);
                    }
                }
            }
        }

        const remap = new Array(numStates);
        let nextIndex = 0;
        for (let b = 0; b < numStates; b++) {
            let canonical = b;
            for (let a = 0; a < b; a++) {
                if (sim[a].has(b) && sim[b].has(a)) {
                    canonical = a;
                    break;
                }
            }
            if (canonical === b) {
                remap[b] = nextIndex++;
            } else {
                remap[b] = remap[canonical];
            }
        }

        if (nextIndex === numStates) return;

        this.remapStates(remap);
    }
}

const optimizeNFA = (nfa, { allStatesAreReachable = false, maxDepth = Infinity } = {}) => {
    nfa.closeOverEpsilonTransitions();
    nfa.removeDeadStates({ maxDepth, allStatesAreReachable });
    nfa.reduceBySimulation();
};

export const regexToNFA = (pattern, numSymbols, valueOffset = 0) => {
    try {
        const parser = new RegexParser(pattern);
        const ast = parser.parse();
        const charToSymbol = createCharToSymbol(numSymbols, valueOffset);
        const builder = new RegexToNFABuilder(charToSymbol, numSymbols);
        const nfa = builder.build(ast);
        optimizeNFA(nfa);
        return nfa;
    } catch (e) {
        throw new Error(`Regex "${pattern}" could not be compiled: ${e.message}`);
    }
};

export const javascriptSpecToNFA = (config, numSymbols, valueOffset = 0) => {
    const builder = new JavascriptNFABuilder(config, numSymbols, valueOffset);
    const nfa = builder.build();

    optimizeNFA(nfa, {
        allStatesAreReachable: true,
        maxDepth: config.maxDepth ?? Infinity,
    });

    return nfa;
};

export class NFASerializer {
    static MIN_SYMBOLS = 1;
    static SYMBOL_COUNT_FIELD_BITS = 4;
    static MAX_SYMBOLS = 1 << this.SYMBOL_COUNT_FIELD_BITS;
    static STATE_BITS_FIELD_BITS = 4;
    static MIN_STATE_BITS = 1;
    static MAX_STATE_BITS = 1 << this.STATE_BITS_FIELD_BITS;
    static FORMAT = Object.freeze({
        PLAIN: 0,
        PACKED: 1,
    });
    static HEADER_FORMAT_BITS = 2;

    static serialize(nfa) {
        if (!nfa.numStates() || !nfa.getStartIds().size) {
            return '';
        }

        this._normalizeStates(nfa);

        const numStates = nfa.numStates();
        const startCount = nfa.getStartIds().size;

        let startIsAccept = 0;
        let acceptCount = nfa.getAcceptIds().size;
        for (let i = 0; i < startCount; i++) {
            if (nfa.isAccepting(i)) {
                startIsAccept |= 1 << i;
                acceptCount--;
            }
        }

        const symbolCount = Math.max(nfa.numSymbols(), this.MIN_SYMBOLS);
        if (symbolCount > this.MAX_SYMBOLS) {
            throw new Error(`NFA requires ${symbolCount} symbols but only ${this.MAX_SYMBOLS} are supported`);
        }
        const stateBits = Math.max(1, requiredBits(numStates - 1));
        if (stateBits > this.MAX_STATE_BITS) {
            throw new Error(`NFA exceeds maximum supported state count (${1 << this.MAX_STATE_BITS})`);
        }
        const symbolBits = requiredBits(symbolCount - 1);

        const { format, transitionCountBits } =
            this._chooseStateFormat(nfa, symbolCount, symbolBits, stateBits);

        const writer = new BitWriter();
        this._writeHeader(writer, {
            format,
            symbolCount,
            stateBits,
            startCount,
            startIsAccept,
            acceptCount,
            transitionCountBits,
        });
        if (format === this.FORMAT.PACKED) {
            this._writePackedBody(writer, nfa, symbolCount, stateBits);
        } else {
            this._writePlainBody(writer, nfa, transitionCountBits, symbolBits, stateBits);
        }

        return this._encodeBytes(writer.toUint8Array());
    }

    static _normalizeStates(nfa) {
        const numStates = nfa.numStates();
        const startSet = nfa.getStartIds();

        const acceptIds = [];
        const otherIds = [];
        for (let i = 0; i < numStates; i++) {
            if (startSet.has(i)) continue;

            if (nfa.isAccepting(i)) {
                acceptIds.push(i);
            } else {
                otherIds.push(i);
            }
        }

        const remap = new Array(numStates);
        let nextIndex = 0;
        for (const id of startSet) {
            remap[id] = nextIndex++;
        }
        for (const id of acceptIds) {
            remap[id] = nextIndex++;
        }
        for (const id of otherIds) {
            remap[id] = nextIndex++;
        }

        nfa.remapStates(remap);
    }

    static _writeHeader(writer, {
        format,
        symbolCount,
        stateBits,
        startCount,
        startIsAccept,
        acceptCount,
        transitionCountBits,
    }) {
        if (!this._isValidFormat(format)) {
            throw new Error('NFA serialization format is unsupported');
        }
        if (symbolCount < this.MIN_SYMBOLS || symbolCount > this.MAX_SYMBOLS) {
            throw new Error('Symbol count is out of range for header encoding');
        }
        if (stateBits < this.MIN_STATE_BITS || stateBits > this.MAX_STATE_BITS) {
            throw new Error('State bit width is out of range for header encoding');
        }
        if (acceptCount >= (1 << stateBits)) {
            throw new Error('Accept state count exceeds encodable range for header');
        }
        if (startCount < 1 || startCount >= (1 << stateBits)) {
            throw new Error('Start state count exceeds encodable range for header');
        }
        writer.writeBits(format, this.HEADER_FORMAT_BITS);
        writer.writeBits(stateBits - 1, this.STATE_BITS_FIELD_BITS);
        writer.writeBits(symbolCount - 1, this.SYMBOL_COUNT_FIELD_BITS);
        writer.writeBits(startCount, stateBits);
        writer.writeBits(acceptCount, stateBits);
        writer.writeBits(startIsAccept, startCount);
        if (format === this.FORMAT.PLAIN) {
            writer.writeBits(transitionCountBits, this.SYMBOL_COUNT_FIELD_BITS);
        }
    }

    static _writePlainBody(writer, nfa, transitionCountBits, symbolBits, stateBits) {
        for (let stateId = 0; stateId < nfa.numStates(); stateId++) {
            const transitions = nfa.getStateTransitions(stateId);
            let transitionCount = 0;
            for (let symbolIndex = 0; symbolIndex < transitions.length; symbolIndex++) {
                const targets = transitions[symbolIndex];
                if (targets) transitionCount += targets.length;
            }
            writer.writeBits(transitionCount, transitionCountBits);
            for (let symbolIndex = 0; symbolIndex < transitions.length; symbolIndex++) {
                const targets = transitions[symbolIndex];
                if (!targets) continue;
                for (const target of targets) {
                    writer.writeBits(symbolIndex, symbolBits);
                    writer.writeBits(target, stateBits);
                }
            }
        }
    }

    static _writePackedBody(writer, nfa, symbolCount, stateBits) {
        for (let stateId = 0; stateId < nfa.numStates(); stateId++) {
            const transitions = nfa.getStateTransitions(stateId);
            let symbolMask = 0;
            for (let symbolIndex = 0; symbolIndex < transitions.length; symbolIndex++) {
                if (transitions[symbolIndex]?.length) {
                    symbolMask |= 1 << symbolIndex;
                }
            }
            writer.writeBits(symbolMask, symbolCount);
            for (let symbolIndex = 0; symbolIndex < transitions.length; symbolIndex++) {
                const targets = transitions[symbolIndex];
                if (targets?.length) {
                    writer.writeBits(targets[0], stateBits);
                }
            }
        }
    }

    static _chooseStateFormat(nfa, symbolCount, symbolBits, stateBits) {
        const numStates = nfa.numStates();

        let maxTransitions = 0;
        let totalTransitions = 0;
        let canPack = true;
        for (let stateId = 0; stateId < numStates; stateId++) {
            const transitions = nfa.getStateTransitions(stateId);
            let transitionCount = 0;
            for (let symbolIndex = 0; symbolIndex < transitions.length; symbolIndex++) {
                const targets = transitions[symbolIndex];
                if (!targets) continue;
                if (targets.length > 1) canPack = false;
                transitionCount += targets.length;
            }
            if (transitionCount > maxTransitions) maxTransitions = transitionCount;
            totalTransitions += transitionCount;
        }

        const transitionCountBits = requiredBits(maxTransitions);

        if (!canPack) {
            return { format: this.FORMAT.PLAIN, transitionCountBits };
        }

        const plainSizeEstimate =
            numStates * transitionCountBits + totalTransitions * (symbolBits + stateBits);
        const packedSizeEstimate = numStates * symbolCount + totalTransitions * stateBits;

        const format = packedSizeEstimate < plainSizeEstimate
            ? this.FORMAT.PACKED
            : this.FORMAT.PLAIN;
        return { format, transitionCountBits };
    }

    static _isValidFormat(format) {
        return format === this.FORMAT.PLAIN || format === this.FORMAT.PACKED;
    }

    static _encodeBytes(bytes) {
        return Buffer.from(bytes)
            .toString('base64')
            .replace(/\+/g, '-')
            .replace(/\//g, '_')
            .replace(/=/g, '');
    }
}

const charToValue = (char) => {
    if (char >= '0' && char <= '9') {
        return char.charCodeAt(0) - '0'.charCodeAt(0);
    }
    if (char >= 'A' && char <= 'Z') {
        return char.charCodeAt(0) - 'A'.charCodeAt(0) + 10;
    }
    if (char >= 'a' && char <= 'z') {
        return char.charCodeAt(0) - 'a'.charCodeAt(0) + 10;
    }
    throw new Error(`Unsupported character '${char}' in regex constraint`);
};

const createCharToSymbol = (numValues, valueOffset = 0) => {
    const minValue = 1 + valueOffset;
    const maxValue = numValues + valueOffset;
    return (char) => {
        const value = charToValue(char);
        if (value < minValue || value > maxValue) {
            throw new Error(`Character '${char}' is out of range (${minValue}-${maxValue})`);
        }
        return new NFA.Symbol(value - valueOffset);
    };
};

class RegexAstNode {
    static Charset = class {
        constructor(chars, negated = false) {
            this.chars = chars;
            this.negated = negated;
        }
    };

    static Concat = class {
        constructor(parts) {
            this.parts = parts;
        }
    };

    static Alternate = class {
        constructor(options) {
            this.options = options;
        }
    };

    static Quantifier = class {
        constructor(child, min, max) {
            this.child = child;
            this.min = min;
            this.max = max;
        }
    };
}

class RegexParser {
    static SEQUENCE_TERMINATORS = '|)';
    static QUANTIFIERS = '*+?{';

    constructor(pattern) {
        this.pattern = pattern;
        this.pos = 0;
    }

    parse() {
        const expr = this._parseExpression();
        if (!this._isEOF()) {
            throw new Error(`Unexpected token at position ${this.pos}`);
        }
        return expr;
    }

    _parseExpression() {
        const node = this._parseSequence();
        const alternatives = [node];
        while (this._peek() === '|') {
            this._next();
            alternatives.push(this._parseSequence());
        }
        if (alternatives.length === 1) return node;
        return new RegexAstNode.Alternate(alternatives);
    }

    _parseSequence() {
        const parts = [];
        while (!this._isEOF() && !RegexParser.SEQUENCE_TERMINATORS.includes(this._peek())) {
            parts.push(this._parseQuantified());
        }
        if (parts.length === 1) return parts[0];
        return new RegexAstNode.Concat(parts);
    }

    _parseQuantified() {
        let node = this._parsePrimary();
        while (!this._isEOF()) {
            const ch = this._peek();
            if (ch === '*') {
                this._next();
                node = new RegexAstNode.Quantifier(node, 0, null);
            } else if (ch === '+') {
                this._next();
                node = new RegexAstNode.Quantifier(node, 1, null);
            } else if (ch === '?') {
                this._next();
                node = new RegexAstNode.Quantifier(node, 0, 1);
            } else if (ch === '{') {
                node = this._parseBraceQuantifier(node);
            } else {
                break;
            }
        }
        return node;
    }

    _parseBraceQuantifier(node) {
        const startPos = this.pos;
        this._expect('{');

        const min = this._parseNumber();
        if (min === null) {
            throw new Error(`Expected number after '{' at position ${startPos}`);
        }

        let max = min;
        if (this._peek() === ',') {
            this._next();
            max = this._peek() === '}' ? null : this._parseNumber();
            if (max === undefined) {
                throw new Error(`Expected number or '}' after ',' at position ${this.pos}`);
            }
            if (max !== null && max < min) {
                throw new Error(`Invalid quantifier: max (${max}) < min (${min}) at position ${startPos}`);
            }
        }

        this._expect('}');

        return new RegexAstNode.Quantifier(node, min, max);
    }

    _parseNumber() {
        let numStr = '';
        while (this._peek() >= '0' && this._peek() <= '9') {
            numStr += this._next();
        }
        if (!numStr) return null;
        return parseInt(numStr, 10);
    }

    _parsePrimary() {
        const ch = this._peek();
        if (ch === '(') {
            this._next();
            const expr = this._parseExpression();
            if (this._peek() !== ')') {
                throw new Error(`Unclosed group at position ${this.pos}`);
            }
            this._next();
            return expr;
        }
        if (ch === '[') {
            return this._parseCharClass();
        }
        if (ch === '.') {
            this._next();
            return new RegexAstNode.Charset([], true);
        }
        if (ch === undefined) {
            throw new Error('Unexpected end of pattern');
        }
        if (RegexParser.QUANTIFIERS.includes(ch) || RegexParser.SEQUENCE_TERMINATORS.includes(ch)) {
            throw new Error(`Unexpected token '${ch}' at position ${this.pos}`);
        }
        this._next();
        return new RegexAstNode.Charset([ch]);
    }

    _parseCharClass() {
        this._expect('[');
        const isNegated = this._peek() === '^';
        if (isNegated) {
            this._next();
        }
        const chars = new Set();
        while (!this._isEOF() && this._peek() !== ']') {
            const start = this._next();
            if (this._peek() === '-') {
                this._next();
                const end = this._next();
                const startCode = start.charCodeAt(0);
                const endCode = end.charCodeAt(0);
                if (endCode < startCode) {
                    throw new Error('Invalid character range in class');
                }
                for (let code = startCode; code <= endCode; code++) {
                    chars.add(String.fromCharCode(code));
                }
            } else {
                chars.add(start);
            }
        }
        this._expect(']');
        if (!chars.size) {
            throw new Error('Empty character class');
        }
        return new RegexAstNode.Charset([...chars], isNegated);
    }

    _expect(ch) {
        if (this._next() !== ch) {
            throw new Error(`Expected '${ch}' at position ${this.pos - 1}`);
        }
    }

    _peek() {
        return this.pattern[this.pos];
    }

    _next() {
        if (this.pos >= this.pattern.length) return undefined;
        return this.pattern[this.pos++];
    }

    _isEOF() {
        return this.pos >= this.pattern.length;
    }
}

class RegexToNFABuilder {
    constructor(charToSymbol, numSymbols) {
        this._nfa = new NFA({ stateLimit: MAX_STATE_COUNT });
        this._charToSymbol = charToSymbol;
        this._numSymbols = numSymbols;
    }

    static _Fragment = class {
        constructor(startId, acceptId) {
            this.startId = startId;
            this.acceptId = acceptId;
        }
    };

    _newFragment(startId, acceptId) {
        return new RegexToNFABuilder._Fragment(startId, acceptId);
    }

    build(ast) {
        const fragment = this._buildNode(ast);
        this._nfa.addStartId(fragment.startId);
        this._nfa.addAcceptId(fragment.acceptId);
        this._nfa.seal();
        return this._nfa;
    }

    _buildNode(node) {
        switch (node.constructor) {
            case RegexAstNode.Charset:
                return this._buildCharset(node.chars, node.negated);
            case RegexAstNode.Concat:
                return this._buildConcat(node.parts);
            case RegexAstNode.Alternate:
                return this._buildAlternate(node.options);
            case RegexAstNode.Quantifier:
                return this._buildQuantifier(node.child, node.min, node.max);
            default:
                throw new Error('Unknown AST node type');
        }
    }

    _buildEmpty() {
        const stateId = this._nfa.addState();
        return this._newFragment(stateId, stateId);
    }

    _buildCharset(chars, negated = false) {
        const startId = this._nfa.addState();
        const acceptId = this._nfa.addState();
        let symbols;
        if (negated) {
            const excludeIndices = new Set(chars.map(c => this._charToSymbol(c).index));
            symbols = NFA.Symbol.all(this._numSymbols).filter(s => !excludeIndices.has(s.index));
        } else {
            symbols = chars.map(c => this._charToSymbol(c));
        }
        this._nfa.addTransition(startId, acceptId, ...symbols);
        return this._newFragment(startId, acceptId);
    }

    _buildConcat(parts) {
        if (!parts.length) return this._buildEmpty();
        const first = this._buildNode(parts[0]);
        let acceptId = first.acceptId;
        for (let i = 1; i < parts.length; i++) {
            const next = this._buildNode(parts[i]);
            this._nfa.addEpsilon(acceptId, next.startId);
            acceptId = next.acceptId;
        }
        return this._newFragment(first.startId, acceptId);
    }

    _buildAlternate(options) {
        const startId = this._nfa.addState();
        const acceptId = this._nfa.addState();
        for (const option of options) {
            const optionFragment = this._buildNode(option);
            this._nfa.addEpsilon(startId, optionFragment.startId);
            this._nfa.addEpsilon(optionFragment.acceptId, acceptId);
        }
        return this._newFragment(startId, acceptId);
    }

    _buildQuantifier(child, min, max) {
        let result = min === 0 ? this._buildEmpty() : this._buildNode(child);

        for (let i = 1; i < min; i++) {
            const next = this._buildNode(child);
            this._nfa.addEpsilon(result.acceptId, next.startId);
            result = this._newFragment(result.startId, next.acceptId);
        }

        if (max === null) {
            const inner = this._buildNode(child);
            this._nfa.addEpsilon(result.acceptId, inner.startId);
            this._nfa.addEpsilon(inner.acceptId, inner.startId);
            this._nfa.addEpsilon(inner.acceptId, result.acceptId);
        } else {
            for (let i = min; i < max; i++) {
                const inner = this._buildNode(child);
                this._nfa.addEpsilon(result.acceptId, inner.startId);
                this._nfa.addEpsilon(result.acceptId, inner.acceptId);
                result = this._newFragment(result.startId, inner.acceptId);
            }
        }

        return result;
    }
}

class JavascriptNFABuilder {
    constructor(definition, numValues, valueOffset = 0) {
        const { startState, transition, accept, maxDepth } = definition;
        this._startState = startState;
        this._transitionFn = transition;
        this._acceptFn = accept;
        this._numValues = numValues;
        this._valueOffset = valueOffset;
        this._maxDepth = maxDepth ?? Infinity;
    }

    build() {
        const nfa = new NFA({ stateLimit: MAX_STATE_COUNT });
        const stateStrToIndex = new Map();
        const indexToStateStr = [];

        const addState = (stateStr, accepting) => {
            const index = nfa.addState();
            stateStrToIndex.set(stateStr, index);
            indexToStateStr.push(stateStr);
            if (accepting) nfa.addAcceptId(index);
            return index;
        };

        const transitionFn = this._wrapTransitionFn(this._transitionFn);
        const acceptFn = this._wrapAcceptFn(this._acceptFn);

        let currentLevel = [];
        let depth = 0;

        const startStateStrs = this._generateStartStatesFromValue(this._startState);
        for (const startStateStr of startStateStrs) {
            const index = addState(startStateStr, acceptFn(startStateStr));
            currentLevel.push(index);
            nfa.addStartId(index);
        }

        while (currentLevel.length) {
            const nextLevel = [];
            for (const index of currentLevel) {
                const stateStr = indexToStateStr[index];
                for (let value = 1; value <= this._numValues; value++) {
                    const nextStateStrs = transitionFn(stateStr, value + this._valueOffset);
                    for (const nextStateStr of nextStateStrs) {
                        let targetIndex = stateStrToIndex.get(nextStateStr);
                        if (targetIndex === undefined) {
                            if (depth >= this._maxDepth) continue;
                            targetIndex = addState(nextStateStr, acceptFn(nextStateStr));
                            nextLevel.push(targetIndex);
                        }
                        nfa.addTransition(index, targetIndex, new NFA.Symbol(value));
                    }
                }
            }
            currentLevel = nextLevel;
            depth++;
        }

        nfa.seal();

        return nfa;
    }

    _fnResultToStateArray(result) {
        if (Array.isArray(result)) return result;
        if (result !== undefined) return [result];
        return [];
    }

    _generateStartStatesFromValue(startState) {
        const states = this._fnResultToStateArray(startState);
        return states.map(item => this._stringifyState(item));
    }

    _wrapTransitionFn(fn) {
        return (stateStr, value) => {
            const stateValue = this._deserializeState(stateStr);
            try {
                const result = fn(stateValue, value);
                const nextStates = this._fnResultToStateArray(result);
                return nextStates.map(item => this._stringifyState(item));
            } catch (err) {
                throw new Error(
                    `Transition function threw for input (${stateStr}, ${value}): ${err?.message || err}`);
            }
        };
    }

    _wrapAcceptFn(fn) {
        return (stateStr) => {
            const stateValue = this._deserializeState(stateStr);
            try {
                return !!fn(stateValue);
            } catch (err) {
                throw new Error(
                    `Accept function threw for input ${stateStr}: ${err?.message || err}`);
            }
        };
    }

    _stringifyState(state) {
        if (Array.isArray(state)) {
            throw new Error('State must not be an array');
        }

        try {
            const serialized = canonicalJSON(state);
            if (serialized === undefined) {
                throw new Error('Could not JSON-serialize');
            }
            return serialized;
        } catch (err) {
            throw new Error(`Invalid state: ${err?.message || err}`);
        }
    }

    _deserializeState(serialized) {
        return JSON.parse(serialized);
    }
}
