// ==UserScript==
// @name         Fpuzzles-NewConstraints
// @namespace    http://tampermonkey.net/
// @version      1.17
// @description  Adds more constraints to f-puzzles.
// @author       Rangsk
// @match        https://*.f-puzzles.com/*
// @match        https://f-puzzles.com/*
// @icon         data:image/gif;base64,R0lGODlhAQABAAAAACH5BAEKAAEALAAAAAABAAEAAAICTAEAOw==
// @grant        none
// @run-at       document-end
// ==/UserScript==

(function() {
    // Adding a new constraint:
    // 1. Add a new entry to the newConstraintInfo array
    // 2. If the type is not already supported, add it to the following:
    //      a. exportPuzzle
    //      b. importPuzzle
    //      c. categorizeTools
    //      d. Add a drawing helper function for what it looks like
    // 3. Add conflict highlighting logic to candidatePossibleInCell
    // 4. Add a new constraint class (see 'Constraint classes' comment)
    const newConstraintInfo = [
        {
            name: "Renban",
            type: "line",
            color: "#F067F0",
            colorDark: "#642B64",
            lineWidth: 0.25,
            tooltip: [
                "Numbers on a renban line must be consecutive, but in any order.",
                "Digits cannot repeat on a renban line.",
                "",
                "Click and drag to draw a renban line.",
                "Click on a renban line to remove it.",
                "Shift click and drag to draw overlapping renban lines.",
            ],
        },
        {
            name: "German Whispers",
            type: "line",
            color: "#67F067",
            colorDark: "#357D35",
            lineWidth: 0.1875,
            tooltip: [
                "Adjacent numbers on a German whispers line must have a difference of 5 or greater.",
                "[For non-9x9 grid sizes, this adjusts to be (size / 2) rounded up.]",
                "",
                "Click and drag to draw a German whispers line.",
                "Click on a German whispers line to remove it.",
                "Shift click and drag to draw overlapping German whispers lines.",
            ],
        },
        {
            name: "Dutch Whispers",
            type: "line",
            color: "#FF9A00",
            colorDark: "#B26B00",
            lineWidth: 0.1875,
            tooltip: [
                "Adjacent numbers on a Dutch whispers line must have a difference of 4 or greater.",
                "[For non-9x9 grid sizes, this adjusts to be (size / 2) - 1 rounded up.]",
                "",
                "Click and drag to draw a Dutch whispers line.",
                "Click on a Dutch whispers line to remove it.",
                "Shift click and drag to draw overlapping Dutch whispers lines.",
            ],
        },
        {
            name: "Entropic Line",
            type: "line",
            color: "#FFCCAA",
            colorDark: "#FFCCAA",
            lineWidth: 0.15625,
            tooltip: [
                "Any set of three sequential cells along an entropic line must contain",
                "a low digit, a middle digit, and a high digit.",
                "For 9x9 this is 1-3, 4-6, 7-9.",
                "For grid sizes not divisible by 3, the middle rank has the different number of values.",
                "Digits my repeat on a line, if allowed by other rules.",
                "An entropic line of length two may not contain two digits from the same rank.",
                "",
                "Click and drag to draw an entropic line.",
                "Click on an entropic line to remove it.",
                "Shift click and drag to draw overlapping entropic lines.",
            ],
        },
        {
            name: "Modular Line",
            type: "line",
            color: "#33BBBB",
            colorDark: "#1E6E6E",
            lineWidth: 0.15625,
            tooltip: [
                "Any set of three sequential cells along an modular line must contain",
                "a digit that is 0 mod 3, 1 mod 3, and 2 mod 3.",
                "For 9x9 this is (1,4,7), (2,5,8), (3,6,9).",
                "Digits my repeat on a line, if allowed by other rules.",
                "A modular line of length two may not contain two digits which are the same mod 3.",
                "",
                "Click and drag to draw an modular line.",
                "Click on an modular line to remove it.",
                "Shift click and drag to draw overlapping modular lines.",
            ],
        },
        {
            name: "Region Sum Line",
            type: "line",
            color: "#2ECBFF",
            colorDark: "#1E86A8",
            lineWidth: 0.15625,
            tooltip: [
                "Digits have an equal sum within each box the line passes through.",
                "If the line re-enters a region, then it starts a new sum.",
                "",
                "Click and drag to draw a region sum line.",
                "Click on a region sum line to remove it.",
                "Shift click and drag to draw overlapping region sum lines.",
            ],
        },
        {
            name: "Nabner",
            type: "line",
            color: "#C9C883",
            colorDark: "#787856",
            lineWidth: 0.25,
            tooltip: [
                "Digits on a Nabner line cannot repeat.",
                "If a digit appears on the line, then the digits consecutive with it must not appear.",
                "",
                "Click and drag to draw a nabner line.",
                "Click on a nabner to remove it.",
                "Shift click and drag to draw overlapping nabner.",
            ],
        },
        {
            name: "Double Arrow",
            type: "line",
            color: "#C54B8B",
            colorDark: "#763B5A",
            endpoints: "circle",
            lineWidth: 0.15,
            tooltip: [
                "The sum of the digits on the line is equal to the sum of the circles at the end of each line.",
                "",
                "Click and drag to draw a double arrow.",
                "Click on a double arrow to remove it.",
                "Shift click and drag to draw overlapping double arrow.",
            ],
        },
        {
            name: "Zipper Line",
            type: "line",
            color: "#cdb1ec",
            colorDark: "#cdb1ec",
            lineWidth: 0.15625,
            tooltip: [
                "Digits equidistant from the line's center must sum to the same value.",
                "If the line has an odd number of cells, the central cell's digit defines this sum.",
                "",
                "Click and drag to draw a zipper line.",
                "Click on a zipper line to remove it.",
                "Shift click and drag to draw overlapping zipper line.",
            ],
        },
        {
            name: "Slow Thermometer",
            type: "line",
            // Colors for drawing the actual constraint:
            outerColor: "#6495ED",      // CornflowerBlue (light mode)
            outerColorDark: "#4682B4",  // SteelBlue (dark mode)
            innerColor: "#B0E0E6",      // PowderBlue (light mode)
            innerColorDark: "#87CEEB",  // SkyBlue (dark mode, but lighter than outer dark)
            // lineWidths for drawing:
            outerLineWidth: 0.3,
            innerLineWidth: 0.12, // slightly thicker for better visibility
            // bulbRadii for drawing:
            outerBulbRadiusFactor: 0.4,
            innerBulbRadiusFactor: 0.28, // slightly larger inner bulb
            // For cosmetic export (simpler representation):
            exportLineColor: "#6495ED", // CornflowerBlue
            exportLineWidth: 0.3,
            exportBulbColor: "#6495ED", // CornflowerBlue
            exportBulbSize: 0.8, // (diameter factor)
            tooltip: [
                "Digits on a slow thermometer must not decrease as they move away from the bulb.",
                "Digits may repeat.",
                "",
                "Click and drag to draw a slow thermometer.",
                "Click on a slow thermometer to remove it.",
                "Shift click and drag to draw overlapping slow thermometers.",
            ],
        },
        {
            name: "Row Indexer",
            type: "cage",
            color: "#7CC77C",
            colorDark: "#307130",
            tooltip: [
                "If this cell (R, C) has value V then cell (V, C) has value R",
                "",
                "Click on a cell to add a row indexer.",
                "Click on a row indexer to remove it.",
            ],
        },
        {
            name: "Column Indexer",
            type: "cage",
            color: "#C77C7C",
            colorDark: "#713030",
            tooltip: [
                "If this cell (R, C) has value V then cell (R, V) has value C",
                "(This one is used by the standard 159 constraint)",
                "",
                "Click on a cell to add a column indexer.",
                "Click on a column indexer to remove it.",
            ],
        },
        {
            name: "Box Indexer",
            type: "cage",
            color: "#7C7CC7",
            colorDark: "#303071",
            tooltip: [
                "If this cell at box position I has value V then",
                "the cell in the same box at box position V has value I",
                "",
                "Click on a cell to add a box indexer.",
                "Click on a box indexer to remove it.",
            ],
        },
        {
            name: "X Sum",
            type: "outside",
            symbol: "\u25EF",
            tooltip: [
                "Indicates the sum of the first X numbers in the row or column, ",
                "where X is equal to the first number placed in that direction.",
                "",
                "Click outside the grid to add an x-sum.",
                "Click on an x-sum to remove it.",
                "Shift click on an x-sum to select it.",
                "Type to enter a total into the selected x-sum (or the most recently edited one).",
            ],
        },
        {
            name: "Skyscraper",
            type: "outside",
            symbol: "\u25AF",
            tooltip: [
                "Indicates the count of numbers in the row or column which increase from the previous highest value.",
                "",
                "Click outside the grid to add a skyscraper.",
                "Click on a skyscraper to remove it.",
                "Shift click on a skyscraper to select it.",
                "Type to enter a total into the selected skyscraper (or the most recently edited one).",
            ],
        },
    ];

    const customConstraintStorageKey = "sudokuSolverCustomConstraintsV1";
    let customConstraintDefinitions = [];
    let customCompileCache = new Map();

    const fallbackCustomConstraintSource = [
        "export const type = 'nfa';",
        "export const spec = {",
        "    startState: 0,",
        "    transition: (state, value) => value > state ? value : undefined,",
        "    accept: (state) => state > 0,",
        "};",
    ].join("\n");

    const customSlug = function(name) {
        return String(name || "customconstraint").toLowerCase().replace(/[^a-z0-9]+/g, "");
    };

    const normalizeCustomVariable = function(variable) {
        if (typeof variable === "string") {
            return { name: variable, label: variable, type: "text", defaultValue: "" };
        }
        const name = String(variable && variable.name ? variable.name : "value").trim();
        return {
            name,
            label: String(variable && variable.label ? variable.label : name),
            type: variable && variable.type === "number" ? "number" : "text",
            defaultValue: variable && variable.defaultValue !== undefined
                ? String(variable.defaultValue)
                : variable && variable.default !== undefined
                    ? String(variable.default)
                    : "",
        };
    };

    const normalizeCustomDefinition = function(definition) {
        const rawName = definition && definition.name ? String(definition.name) : "Custom Constraint";
        const placement = ["line", "cell", "cage", "outside"].includes(definition && definition.type)
            ? definition.type
            : definition && definition.type === "region"
                ? "cage"
                : "line";
        const logicType = ["nfa", "binary", "precompiled-nfa", "precompiled-binary"].includes(definition && definition.logicType)
            ? definition.logicType
            : definition && definition.nfa
                ? "precompiled-nfa"
                : definition && definition.table
                    ? "precompiled-binary"
                    : definition && definition.type === "binary"
                        ? "binary"
                        : "nfa";
        const variables = Array.isArray(definition && definition.variables)
            ? definition.variables.map(normalizeCustomVariable).filter(v => v.name.length > 0)
            : [];
        return {
            version: 1,
            id: definition && definition.id ? String(definition.id) : customSlug(rawName),
            name: rawName,
            type: placement,
            logicType,
            source: definition && definition.source ? String(definition.source) : fallbackCustomConstraintSource,
            nfa: definition && definition.nfa ? String(definition.nfa) : "",
            table: definition && definition.table ? String(definition.table) : "",
            color: definition && definition.color ? String(definition.color) : "#7A7CFF",
            colorDark: definition && definition.colorDark ? String(definition.colorDark) : definition && definition.color ? String(definition.color) : "#9EA0FF",
            lineWidth: definition && definition.lineWidth ? Number(definition.lineWidth) : 0.18,
            symbol: definition && definition.symbol ? String(definition.symbol) : "*",
            variables,
        };
    };

    const loadCustomConstraintDefinitions = function() {
        try {
            const raw = localStorage.getItem(customConstraintStorageKey);
            if (!raw) return [];
            const parsed = JSON.parse(raw);
            const list = Array.isArray(parsed) ? parsed : parsed && Array.isArray(parsed.definitions) ? parsed.definitions : [];
            return list.map(normalizeCustomDefinition);
        } catch (e) {
            console.warn("Fpuzzles-NewConstraints: Could not load custom constraints.", e);
            return [];
        }
    };

    const saveCustomConstraintDefinitions = function() {
        localStorage.setItem(customConstraintStorageKey, JSON.stringify(customConstraintDefinitions, null, 2));
        customCompileCache = new Map();
    };

    const customInfoFromDefinition = function(definition) {
        return {
            name: definition.name,
            type: definition.type,
            color: definition.color,
            colorDark: definition.colorDark,
            lineWidth: definition.lineWidth,
            symbol: definition.symbol,
            tooltip: [
                `${definition.name} custom constraint.`,
                definition.type === "outside" ? "Click outside the grid to place it." :
                    definition.type === "cell" ? "Click a cell to place it." :
                        "Click and drag to place it.",
                "Use Custom Constraints to edit the definition and instance variables.",
            ],
            isCustomConstraint: true,
            customDefinitionId: definition.id,
            customDefinition: definition,
        };
    };

    const syncCustomConstraintInfo = function() {
        customConstraintDefinitions = loadCustomConstraintDefinitions();
        for (let i = newConstraintInfo.length - 1; i >= 0; i--) {
            if (newConstraintInfo[i].isCustomConstraint) {
                newConstraintInfo.splice(i, 1);
            }
        }
        const existingNames = new Set(newConstraintInfo.map(info => info.name));
        for (let definition of customConstraintDefinitions) {
            let name = definition.name;
            let suffix = 2;
            while (existingNames.has(name)) {
                name = `${definition.name} ${suffix++}`;
            }
            definition = { ...definition, name };
            existingNames.add(name);
            newConstraintInfo.push(customInfoFromDefinition(definition));
        }
    };

    syncCustomConstraintInfo();

    // ===== Per-puzzle constraint picker state =====
    // Which constraints appear in the Constraint Tools popup for the current puzzle.
    // Stored in puzzle JSON (activeConstraints field); empty for a new puzzle.
    let puzzleActiveConstraints = new Set();

    // Set by doShim on first categorizeTools run; used by toolbox to enumerate all constraints
    let builtinToolConstraints = null;

    // ===== Per-user MRU / frequency tracking (localStorage) =====
    // Tracks how often the user has added each constraint to a puzzle so the
    // toolbox can surface frequently/recently used ones.
    const usageStorageKey = "sudokuSolverConstraintUsageV1";

    const loadConstraintUsage = function() {
        try {
            const raw = localStorage.getItem(usageStorageKey);
            return raw ? JSON.parse(raw) : {};
        } catch (e) { return {}; }
    };

    let constraintUsage = loadConstraintUsage();

    const saveConstraintUsage = function() {
        localStorage.setItem(usageStorageKey, JSON.stringify(constraintUsage));
    };

    const recordConstraintUsed = function(name) {
        if (!constraintUsage[name]) constraintUsage[name] = { count: 0, lastUsed: 0 };
        constraintUsage[name].count++;
        constraintUsage[name].lastUsed = Date.now();
        saveConstraintUsage();
    };

    const customDefaultArgs = function(definition) {
        const args = {};
        for (const variable of definition.variables || []) {
            args[variable.name] = variable.defaultValue || "";
        }
        return args;
    };

    const customArgsForInstance = function(definition, instance) {
        const args = customDefaultArgs(definition);
        if (instance && instance.customArgs) {
            Object.assign(args, instance.customArgs);
        }
        for (const variable of definition.variables || []) {
            if (variable.type === "number" && args[variable.name] !== "") {
                const parsed = Number(args[variable.name]);
                args[variable.name] = Number.isFinite(parsed) ? parsed : args[variable.name];
            }
        }
        return args;
    };

    const requiredBits = function(maxValue) {
        if (maxValue <= 0) return 1;
        return 32 - Math.clz32(maxValue);
    };

    const canonicalJSON = function(value) {
        const replacer = function(key, val) {
            if (val && typeof val === "object" && !Array.isArray(val)) {
                return Object.fromEntries(Object.entries(val).sort((a, b) => a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0));
            }
            return val;
        };
        return JSON.stringify(value, replacer);
    };

    class CustomBitWriter {
        constructor() {
            this.bytes = [];
            this.bitLength = 0;
        }

        writeBits(value, bitCount) {
            const normalized = value >>> 0;
            for (let i = bitCount - 1; i >= 0; i--) {
                const bit = (normalized >>> i) & 1;
                const byteIndex = this.bitLength >> 3;
                if (byteIndex === this.bytes.length) {
                    this.bytes.push(0);
                }
                if (bit) {
                    this.bytes[byteIndex] |= 1 << (7 - (this.bitLength & 7));
                }
                this.bitLength++;
            }
        }

        toUint8Array() {
            return Uint8Array.from(this.bytes);
        }
    }

    class CustomNFA {
        constructor() {
            this.startIds = new Set();
            this.acceptIds = new Set();
            this.transitions = [];
        }

        addState() {
            this.transitions.push([]);
            return this.transitions.length - 1;
        }

        addStartId(id) {
            this.startIds.add(id);
        }

        addAcceptId(id) {
            this.acceptIds.add(id);
        }

        addTransition(from, to, value) {
            const symbolIndex = value - 1;
            if (!this.transitions[from][symbolIndex]) {
                this.transitions[from][symbolIndex] = [to];
            } else if (!this.transitions[from][symbolIndex].includes(to)) {
                this.transitions[from][symbolIndex].push(to);
            }
        }

        isAccepting(id) {
            return this.acceptIds.has(id);
        }

        getStartIds() {
            return this.startIds;
        }

        getAcceptIds() {
            return this.acceptIds;
        }

        getStateTransitions(id) {
            return this.transitions[id] || [];
        }

        numStates() {
            return this.transitions.length;
        }

        numSymbols() {
            let max = 0;
            for (const transitions of this.transitions) {
                if (transitions.length > max) max = transitions.length;
            }
            return max;
        }

        remapStates(remap) {
            const newTransitions = [];
            for (let oldIndex = 0; oldIndex < remap.length; oldIndex++) {
                const newIndex = remap[oldIndex];
                const oldTransitions = this.transitions[oldIndex] || [];
                const newStateTransitions = [];
                for (let symbolIndex = 0; symbolIndex < oldTransitions.length; symbolIndex++) {
                    const targets = oldTransitions[symbolIndex];
                    if (targets) newStateTransitions[symbolIndex] = [...new Set(targets.map(t => remap[t]))];
                }
                newTransitions[newIndex] = newStateTransitions;
            }
            this.transitions = newTransitions;
            this.startIds = new Set([...this.startIds].map(id => remap[id]));
            this.acceptIds = new Set([...this.acceptIds].map(id => remap[id]));
        }
    }

    class CustomNFASerializer {
        static FORMAT = { PLAIN: 0, PACKED: 1 };

        static serialize(nfa) {
            if (!nfa.numStates() || !nfa.getStartIds().size) {
                return "";
            }
            this.normalizeStates(nfa);
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
            const symbolCount = Math.max(nfa.numSymbols(), 1);
            if (symbolCount > 16) {
                throw new Error(`NFA requires ${symbolCount} symbols but only 16 are supported.`);
            }
            const stateBits = Math.max(1, requiredBits(numStates - 1));
            if (stateBits > 16) {
                throw new Error("NFA exceeds maximum supported state count.");
            }
            const symbolBits = requiredBits(symbolCount - 1);
            const formatInfo = this.chooseStateFormat(nfa, symbolCount, symbolBits, stateBits);
            const writer = new CustomBitWriter();
            writer.writeBits(formatInfo.format, 2);
            writer.writeBits(stateBits - 1, 4);
            writer.writeBits(symbolCount - 1, 4);
            writer.writeBits(startCount, stateBits);
            writer.writeBits(acceptCount, stateBits);
            writer.writeBits(startIsAccept, startCount);
            if (formatInfo.format === this.FORMAT.PLAIN) {
                writer.writeBits(formatInfo.transitionCountBits, 4);
                this.writePlainBody(writer, nfa, formatInfo.transitionCountBits, symbolBits, stateBits);
            } else {
                this.writePackedBody(writer, nfa, symbolCount, stateBits);
            }
            return this.encodeBytes(writer.toUint8Array());
        }

        static normalizeStates(nfa) {
            const startSet = nfa.getStartIds();
            const acceptIds = [];
            const otherIds = [];
            for (let i = 0; i < nfa.numStates(); i++) {
                if (startSet.has(i)) continue;
                if (nfa.isAccepting(i)) acceptIds.push(i);
                else otherIds.push(i);
            }
            const remap = new Array(nfa.numStates());
            let next = 0;
            for (const id of startSet) remap[id] = next++;
            for (const id of acceptIds) remap[id] = next++;
            for (const id of otherIds) remap[id] = next++;
            nfa.remapStates(remap);
        }

        static chooseStateFormat(nfa, symbolCount, symbolBits, stateBits) {
            let maxTransitions = 0;
            let totalTransitions = 0;
            let canPack = true;
            for (let stateId = 0; stateId < nfa.numStates(); stateId++) {
                const transitions = nfa.getStateTransitions(stateId);
                let transitionCount = 0;
                for (const targets of transitions) {
                    if (!targets) continue;
                    if (targets.length > 1) canPack = false;
                    transitionCount += targets.length;
                }
                maxTransitions = Math.max(maxTransitions, transitionCount);
                totalTransitions += transitionCount;
            }
            const transitionCountBits = requiredBits(maxTransitions);
            if (!canPack) {
                return { format: this.FORMAT.PLAIN, transitionCountBits };
            }
            const plainSizeEstimate = nfa.numStates() * transitionCountBits + totalTransitions * (symbolBits + stateBits);
            const packedSizeEstimate = nfa.numStates() * symbolCount + totalTransitions * stateBits;
            return {
                format: packedSizeEstimate < plainSizeEstimate ? this.FORMAT.PACKED : this.FORMAT.PLAIN,
                transitionCountBits,
            };
        }

        static writePlainBody(writer, nfa, transitionCountBits, symbolBits, stateBits) {
            for (let stateId = 0; stateId < nfa.numStates(); stateId++) {
                const transitions = nfa.getStateTransitions(stateId);
                let transitionCount = 0;
                for (const targets of transitions) {
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

        static writePackedBody(writer, nfa, symbolCount, stateBits) {
            for (let stateId = 0; stateId < nfa.numStates(); stateId++) {
                const transitions = nfa.getStateTransitions(stateId);
                let symbolMask = 0;
                for (let symbolIndex = 0; symbolIndex < transitions.length; symbolIndex++) {
                    if (transitions[symbolIndex] && transitions[symbolIndex].length) {
                        symbolMask |= 1 << symbolIndex;
                    }
                }
                writer.writeBits(symbolMask, symbolCount);
                for (let symbolIndex = 0; symbolIndex < transitions.length; symbolIndex++) {
                    const targets = transitions[symbolIndex];
                    if (targets && targets.length) {
                        writer.writeBits(targets[0], stateBits);
                    }
                }
            }
        }

        static encodeBytes(bytes) {
            let binary = "";
            for (const b of bytes) binary += String.fromCharCode(b);
            return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=/g, "");
        }
    }

    const transformCustomModuleSource = function(source) {
        if (/^\s*import\s/m.test(source)) {
            throw new Error("Custom constraint sources cannot import other modules in the userscript editor.");
        }
        return source
            .replace(/export\s+default\s+/g, "exports.default = ")
            .replace(/export\s+(const|let|var)\s+([A-Za-z_$][\w$]*)\s*=/g, "exports.$2 =")
            .replace(/export\s+function\s+([A-Za-z_$][\w$]*)\s*\(/g, "exports.$1 = function $1(")
            .replace(/export\s+class\s+([A-Za-z_$][\w$]*)/g, "exports.$1 = class $1");
    };

    const loadCustomConstraintModule = function(definition, params, numValues) {
        const exports = {};
        const module = { exports };
        const transformed = transformCustomModuleSource(definition.source || "");
        const fn = new Function("exports", "module", "params", "numValues", "size", `"use strict";\n${transformed}\n;return module.exports;`);
        return fn(exports, module, params, numValues, numValues);
    };

    const buildCustomNFAFromSpec = function(spec, numValues) {
        if (!spec || typeof spec.transition !== "function" || typeof spec.accept !== "function") {
            throw new Error("NFA custom constraints must provide spec.transition and spec.accept.");
        }
        const maxStateCount = 1 << 12;
        const nfa = new CustomNFA();
        const stateToIndex = new Map();
        const indexToState = [];
        const toStateArray = result => Array.isArray(result) ? result : result !== undefined ? [result] : [];
        const stringifyState = state => {
            if (Array.isArray(state)) throw new Error("NFA states must not be arrays.");
            const serialized = canonicalJSON(state);
            if (serialized === undefined) throw new Error("NFA state could not be JSON serialized.");
            return serialized;
        };
        const addState = function(stateString) {
            if (stateToIndex.size >= maxStateCount) {
                throw new Error("NFA state limit exceeded.");
            }
            const index = nfa.addState();
            stateToIndex.set(stateString, index);
            indexToState[index] = stateString;
            if (spec.accept(JSON.parse(stateString))) {
                nfa.addAcceptId(index);
            }
            return index;
        };
        const startStates = toStateArray(spec.startState);
        let currentLevel = [];
        for (const startState of startStates) {
            const stateString = stringifyState(startState);
            const index = addState(stateString);
            nfa.addStartId(index);
            currentLevel.push(index);
        }
        let depth = 0;
        const maxDepth = spec.maxDepth === undefined ? Infinity : spec.maxDepth;
        while (currentLevel.length) {
            const nextLevel = [];
            for (const index of currentLevel) {
                const stateValue = JSON.parse(indexToState[index]);
                for (let value = 1; value <= numValues; value++) {
                    const nextStates = toStateArray(spec.transition(stateValue, value));
                    for (const nextState of nextStates) {
                        const nextStateString = stringifyState(nextState);
                        let targetIndex = stateToIndex.get(nextStateString);
                        if (targetIndex === undefined) {
                            if (depth >= maxDepth) continue;
                            targetIndex = addState(nextStateString);
                            nextLevel.push(targetIndex);
                        }
                        nfa.addTransition(index, targetIndex, value);
                    }
                }
            }
            currentLevel = nextLevel;
            depth++;
        }
        return nfa;
    };

    const encodeBinaryLookupTable = function(table) {
        const bytes = new Uint8Array(table.length * 4);
        for (let i = 0; i < table.length; i++) {
            const value = table[i] >>> 0;
            bytes[i * 4] = value & 0xff;
            bytes[i * 4 + 1] = (value >>> 8) & 0xff;
            bytes[i * 4 + 2] = (value >>> 16) & 0xff;
            bytes[i * 4 + 3] = (value >>> 24) & 0xff;
        }
        let binary = "";
        for (const b of bytes) binary += String.fromCharCode(b);
        return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=/g, "");
    };

    const decodeBinaryLookupTable = function(encoded, numValues) {
        if (!encoded) return null;
        const padded = encoded.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(encoded.length / 4) * 4, "=");
        const binary = atob(padded);
        const table = new Uint32Array(numValues);
        for (let i = 0; i < numValues && i * 4 + 3 < binary.length; i++) {
            table[i] =
                binary.charCodeAt(i * 4) |
                (binary.charCodeAt(i * 4 + 1) << 8) |
                (binary.charCodeAt(i * 4 + 2) << 16) |
                (binary.charCodeAt(i * 4 + 3) << 24);
        }
        return table;
    };

    const compileCustomConstraint = function(definition, params, numValues) {
        const cacheKey = JSON.stringify({
            id: definition.id,
            logicType: definition.logicType,
            source: definition.source,
            nfa: definition.nfa,
            table: definition.table,
            params,
            numValues,
        });
        if (customCompileCache.has(cacheKey)) {
            return customCompileCache.get(cacheKey);
        }
        let result;
        if (definition.logicType === "precompiled-nfa") {
            result = { type: "nfa", nfa: definition.nfa, runtimeNFA: null };
        } else if (definition.logicType === "precompiled-binary") {
            result = { type: "binary", table: definition.table, tableMasks: decodeBinaryLookupTable(definition.table, numValues) };
        } else {
            const module = loadCustomConstraintModule(definition, params, numValues);
            const type = module.type || definition.logicType;
            if (type === "nfa") {
                if (module.nfa || module.encodedNFA) {
                    result = { type: "nfa", nfa: module.nfa || module.encodedNFA, runtimeNFA: null };
                } else if (module.regex) {
                    throw new Error("Browser compilation currently supports JS state-machine specs and binary lookup functions. Precompile regex constraints with scripts/compile-constraint.mjs and use Precompiled NFA.");
                } else {
                    const nfa = buildCustomNFAFromSpec(module.spec, numValues);
                    result = { type: "nfa", nfa: CustomNFASerializer.serialize(nfa), runtimeNFA: nfa };
                }
            } else if (type === "binary") {
                if (typeof module.isAllowed !== "function") {
                    throw new Error("Binary custom constraints must export isAllowed(v1, v2).");
                }
                const table = new Uint32Array(numValues);
                for (let v1 = 1; v1 <= numValues; v1++) {
                    let mask = 0;
                    for (let v2 = 1; v2 <= numValues; v2++) {
                        if (module.isAllowed(v1, v2)) mask |= 1 << (v2 - 1);
                    }
                    table[v1 - 1] = mask;
                }
                result = { type: "binary", table: encodeBinaryLookupTable(table), tableMasks: table };
            } else {
                throw new Error(`Unknown custom constraint logic type: ${type}`);
            }
        }
        customCompileCache.set(cacheKey, result);
        return result;
    };

    // Drawing helpers
    // Outline code provided by Sven Neumann
    const getOutline = function(cells, os) {
        let edgePoints = [],
            grid = [],
            segs = [],
            shapes = [];
        const checkRC = (r, c) => ((grid[r] !== undefined) && (grid[r][c] !== undefined)) || false;
        const pointOS = {
            tl: [os, os],
            tr: [os, 1 - os],
            bl: [1 - os, os],
            br: [1 - os, 1 - os],
            tc: [os, 0.5],
            rc: [0.5, 1 - os],
            bc: [1 - os, 0.5],
            lc: [0.5, os],
        };
        const dirRC = { t: [-1, 0], r: [0, 1], b: [1, 0], l: [0, -1] };
        const flipDir = { t: 'b', r: 'l', b: 't', l: 'r' };
        const patterns = [
            { name: 'otl', bits: '_0_011_1_', enter: 'bl', exit: 'rt', points: 'tl' },
            { name: 'otr', bits: '_0_110_1_', enter: 'lt', exit: 'br', points: 'tr' },
            { name: 'obr', bits: '_1_110_0_', enter: 'tr', exit: 'lb', points: 'br' },
            { name: 'obl', bits: '_1_011_0_', enter: 'rb', exit: 'tl', points: 'bl' },
            { name: 'itl', bits: '01_11____', enter: 'lt', exit: 'tl', points: 'tl' },
            { name: 'itr', bits: '_10_11___', enter: 'tr', exit: 'rt', points: 'tr' },
            { name: 'ibr', bits: '____11_10', enter: 'rb', exit: 'br', points: 'br' },
            { name: 'ibl', bits: '___11_01_', enter: 'bl', exit: 'lb', points: 'bl' },
            { name: 'et', bits: '_0_111___', enter: 'lt', exit: 'rt', points: 'tc' },
            { name: 'er', bits: '_1__10_1_', enter: 'tr', exit: 'br', points: 'rc' },
            { name: 'eb', bits: '___111_0_', enter: 'rb', exit: 'lb', points: 'bc' },
            { name: 'el', bits: '_1_01__1_', enter: 'bl', exit: 'tl', points: 'lc' },
            { name: 'out', bits: '_0_010_1_', enter: 'bl', exit: 'br', points: 'tl,tr' },
            { name: 'our', bits: '_0_110_0_', enter: 'lt', exit: 'lb', points: 'tr,br' },
            { name: 'oub', bits: '_1_010_0_', enter: 'tr', exit: 'tl', points: 'br,bl' },
            { name: 'oul', bits: '_0_011_0_', enter: 'rb', exit: 'rt', points: 'bl,tl' },
            { name: 'solo', bits: '_0_010_0_', enter: '', exit: '', points: 'tl,tr,br,bl' },
        ];
        const checkPatterns = (row, col) => patterns
            .filter(({ name, bits }) => {
                let matches = true;
                bits.split('').forEach((b, i) => {
                    let r = row + Math.floor(i / 3) - 1,
                        c = col + i % 3 - 1,
                        check = checkRC(r, c);
                    matches = matches && ((b === '_') || (b === '1' && check) || (b === '0' && !check));
                });
                return matches;
            });
        const getSeg = (segs, rc, enter) => segs.find(([r, c, _, pat]) => r === rc[0] && c === rc[1] && pat.enter === enter);
        const followShape = segs => {
            let shape = [],
                seg = segs[0];
            const getNext = ([r, c, cell, pat]) => {
                if (pat.exit === '') return;
                let [exitDir, exitSide] = pat.exit.split('');
                let nextRC = [r + dirRC[exitDir][0], c + dirRC[exitDir][1]];
                let nextEnter = flipDir[exitDir] + exitSide;
                return getSeg(segs, nextRC, nextEnter);
            };
            do {
                shape.push(seg);
                segs.splice(segs.indexOf(seg), 1);
                seg = getNext(seg);
            } while (seg !== undefined && shape.indexOf(seg) === -1);
            return shape;
        };
        const shapeToPoints = shape => {
            let points = [];
            shape.forEach(([r, c, cell, pat]) => pat.points
                .split(',')
                .map(point => pointOS[point])
                .map(([ros, cos]) => [r + ros, c + cos])
                .forEach(rc => points.push(rc))
            );
            return points;
        };
        cells.forEach(cell => {
            const { i: col, j: row } = cell;
            grid[row] = grid[row] || [];
            grid[row][col] = { cell };
        });
        cells.forEach(cell => {
            const { i: col, j: row } = cell, matchedPatterns = checkPatterns(row, col);
            matchedPatterns.forEach(pat => segs.push([row, col, cell, pat]));
        });
        while (segs.length > 0) {
            const shape = followShape(segs);
            if (shape.length > 0) shapes.push(shape);
        }
        shapes.forEach(shape => {
            edgePoints = edgePoints.concat(shapeToPoints(shape).map(([r, c], idx) => [idx === 0 ? 'M' : 'L', r, c]));
            edgePoints.push(['Z']);
        });
        return edgePoints;
    };

    const drawLine = function(line, color, colorDark, lineWidth) {
        ctx.lineWidth = cellSL * lineWidth * 0.5;
        ctx.fillStyle = boolSettings['Dark Mode'] ? colorDark : color;
        ctx.strokeStyle = boolSettings['Dark Mode'] ? colorDark : color;
        ctx.beginPath();
        ctx.arc(line[0].x + cellSL / 2, line[0].y + cellSL / 2, ctx.lineWidth / 2, 0, Math.PI * 2);
        ctx.fill();
        ctx.beginPath();
        ctx.moveTo(line[0].x + cellSL / 2, line[0].y + cellSL / 2);
        for (var b = 1; b < line.length; b++) {
            ctx.lineTo(line[b].x + cellSL / 2, line[b].y + cellSL / 2);
        }
        ctx.stroke();
        ctx.beginPath();
        ctx.arc(line[line.length - 1].x + cellSL / 2, line[line.length - 1].y + cellSL / 2, ctx.lineWidth / 2, 0, Math.PI * 2);
        ctx.fill();
    }

    const drawSolidCage = function(cells, colorLight, colorDark) {
        if (cells.length === 0) return;
        const color = boolSettings['Dark Mode'] ? colorDark : colorLight;
        const lineOffset = 1.0 / 32.0;
        const lineWidth = cellSL * lineOffset * 2.0;
        const outline = getOutline(cells, lineOffset);
        const prevStrokeStyle = ctx.strokeStyle;
        const prevLineWidth = ctx.lineWidth;
        const prevLineCap = ctx.lineCap;

        ctx.beginPath();
        for (let i = 0; i < outline.length; i++) {
            const point = outline[i];
            if (point[0] === 'Z') {
                ctx.closePath();
            } else if (point[0] == 'M') {
                ctx.moveTo(gridX + point[1] * cellSL, gridY + point[2] * cellSL);
            } else {
                ctx.lineTo(gridX + point[1] * cellSL, gridY + point[2] * cellSL);
            }
        }

        ctx.fillStyle = color;
        ctx.globalAlpha = 0.1;
        ctx.fill();

        ctx.strokeStyle = color;
        ctx.lineWidth = lineWidth;
        ctx.lineCap = 'round';
        ctx.globalAlpha = 1.00;
        ctx.stroke();

        ctx.globalAlpha = 1.00;
        ctx.strokeStyle = prevStrokeStyle;
        ctx.lineWidth = prevLineWidth;
        ctx.lineCap = prevLineCap;
    }

    const customDisplayColor = function(info) {
        return boolSettings["Dark Mode"] ? info.colorDark || info.color : info.color;
    };

    const drawCustomCellMarker = function(cell, info, label) {
        if (!cell) return;
        const color = customDisplayColor(info);
        const radius = cellSL * 0.32;
        ctx.save();
        ctx.globalAlpha = 0.22;
        ctx.fillStyle = color;
        ctx.beginPath();
        ctx.arc(cell.x + cellSL / 2, cell.y + cellSL / 2, radius, 0, Math.PI * 2);
        ctx.fill();
        ctx.globalAlpha = 1;
        ctx.strokeStyle = color;
        ctx.lineWidth = Math.max(2, cellSL * 0.035);
        ctx.stroke();
        if (label) {
            ctx.fillStyle = boolSettings["Dark Mode"] ? "#F0F0F0" : "#000000";
            ctx.font = `${cellSL * 0.32}px Arial`;
            ctx.fillText(label, cell.x + cellSL / 2, cell.y + cellSL * 0.62);
        }
        ctx.restore();
    };

    const drawCustomOutside = function(instance, info) {
        if (!instance || !instance.cell) return;
        ctx.save();
        ctx.fillStyle = boolSettings["Dark Mode"] ? "#F0F0F0" : "#000000";
        ctx.font = cellSL * 0.85 + "px Arial";
        ctx.fillText(info.symbol || "*", instance.cell.x + cellSL / 2, instance.cell.y + cellSL * 0.82);
        const value = instance.value || "";
        if (value.length) {
            ctx.font = cellSL * 0.52 + "px Arial";
            ctx.fillText(value, instance.cell.x + cellSL / 2, instance.cell.y + cellSL * 0.48);
        }
        ctx.restore();
    };

    const showCustomConstraintInstance = function(instance, info) {
        if (info.type === "line") {
            for (const line of instance.lines || []) {
                if (line.length > 0) {
                    drawLine(line, info.color, info.colorDark, info.lineWidth);
                }
            }
        } else if (info.type === "cell") {
            for (const cell of instance.cells || []) {
                drawCustomCellMarker(cell, info, info.symbol);
            }
        } else if (info.type === "outside") {
            drawCustomOutside(instance, info);
        }
    };

    const doShim = function() {
        ("use strict");

        syncCustomConstraintInfo();

        const customConstraintInfos = function() {
            return newConstraintInfo.filter(info => info.isCustomConstraint);
        };

        const customDefinitionForInfo = function(info) {
            return customConstraintDefinitions.find(def => def.id === info.customDefinitionId) || info.customDefinition;
        };

        const ensureCustomConstraintStorage = function() {
            for (const info of customConstraintInfos()) {
                const id = cID(info.name);
                if (!constraints[id]) {
                    constraints[id] = [];
                }
            }
        };

        const refreshCustomConstraintRegistration = function() {
            syncCustomConstraintInfo();
            registerCustomConstraintClasses();
            categorizeTools();
            ensureCustomConstraintStorage();
            createSidebars();
        };

        const formatCustomCells = function(cells) {
            return (cells || []).filter(cell => cell && !cell.outside).map(cell => formatCell(cell));
        };

        const customGroupsForInstance = function(info, instance) {
            if (!instance) return [];
            if (info.type === "line") {
                return (instance.lines || []).map(line => line.filter(cell => cell && !cell.outside));
            }
            if (info.type === "outside") {
                return instance.set ? [instance.set] : [];
            }
            return [instance.cells || []];
        };

        const appendCustomConstraintPayloads = function(puzzle) {
            const usedDefinitions = new Map();
            for (const info of customConstraintInfos()) {
                const definition = customDefinitionForInfo(info);
                const id = cID(info.name);
                const instances = constraints[id] || [];
                for (const instance of instances) {
                    const params = customArgsForInstance(definition, instance);
                    let compiled;
                    try {
                        compiled = compileCustomConstraint(definition, params, size);
                    } catch (e) {
                        console.warn(`Could not compile custom constraint "${definition.name}".`, e);
                        continue;
                    }
                    for (const group of customGroupsForInstance(info, instance)) {
                        const cells = formatCustomCells(group);
                        if (compiled.type === "binary") {
                            if (cells.length !== 2) {
                                console.warn(`Binary custom constraint "${definition.name}" requires exactly two cells; got ${cells.length}.`);
                                continue;
                            }
                            if (!puzzle.binaryLookupConstraints) puzzle.binaryLookupConstraints = [];
                            puzzle.binaryLookupConstraints.push({
                                cells,
                                table: compiled.table,
                                name: definition.name,
                            });
                        } else {
                            if (cells.length === 0) continue;
                            if (!puzzle.nfaConstraints) puzzle.nfaConstraints = [];
                            puzzle.nfaConstraints.push({
                                cells,
                                nfa: compiled.nfa,
                                name: definition.name,
                            });
                        }
                        usedDefinitions.set(definition.id, definition);
                    }
                }
            }
            if (usedDefinitions.size) {
                puzzle.customConstraintDefinitions = [...usedDefinitions.values()];
            }
        };

        const appendCustomInstanceMetadata = function(puzzle) {
            for (const info of customConstraintInfos()) {
                const definition = customDefinitionForInfo(info);
                if (!definition.variables || !definition.variables.length) continue;
                const id = cID(info.name);
                const puzzleEntries = puzzle[id] || [];
                const instances = constraints[id] || [];
                for (let i = 0; i < Math.min(puzzleEntries.length, instances.length); i++) {
                    puzzleEntries[i].customArgs = customArgsForInstance(definition, instances[i]);
                }
            }
        };

        const restoreCustomInstanceMetadata = function(puzzle) {
            for (const info of customConstraintInfos()) {
                const definition = customDefinitionForInfo(info);
                const id = cID(info.name);
                const puzzleEntries = puzzle[id] || [];
                const instances = constraints[id] || [];
                for (let i = 0; i < Math.min(puzzleEntries.length, instances.length); i++) {
                    instances[i].customArgs = {
                        ...customDefaultArgs(definition),
                        ...(puzzleEntries[i].customArgs || {}),
                    };
                    bindCustomValueProperty(instances[i], definition);
                    if (instances[i].updateSet) {
                        instances[i].updateSet();
                    }
                }
            }
        };

        const mergeCustomDefinitionsFromPuzzle = function(puzzle) {
            if (!Array.isArray(puzzle.customConstraintDefinitions)) return false;
            let changed = false;
            const defsById = new Map(customConstraintDefinitions.map(def => [def.id, def]));
            for (const rawDefinition of puzzle.customConstraintDefinitions) {
                const definition = normalizeCustomDefinition(rawDefinition);
                if (!defsById.has(definition.id)) {
                    customConstraintDefinitions.push(definition);
                    defsById.set(definition.id, definition);
                    changed = true;
                }
            }
            if (changed) {
                saveCustomConstraintDefinitions();
                syncCustomConstraintInfo();
                registerCustomConstraintClasses();
                categorizeTools();
                ensureCustomConstraintStorage();
            }
            delete puzzle.customConstraintDefinitions;
            return changed;
        };

        // Additional import/export data
        const origExportPuzzle = exportPuzzle;
        exportPuzzle = function (includeCandidates) {
            const compressed = origExportPuzzle(includeCandidates);
            const puzzle = JSON.parse(compressor.decompressFromBase64(compressed));

            // Add cosmetic version of constraints for those not using the solver plugin
            for (let constraintInfo of newConstraintInfo) {
                const id = cID(constraintInfo.name);
                const puzzleEntry = puzzle[id];
                if (puzzleEntry && puzzleEntry.length > 0) {
                    if (constraintInfo.type === "line") {
                        if (!puzzle.line) {
                            puzzle.line = [];
                        }
                        for (let instance of puzzleEntry) {
                             puzzle.line.push({
                                lines: instance.lines,
                                outlineC: constraintInfo.exportLineColor || constraintInfo.color,
                                width: constraintInfo.exportLineWidth || constraintInfo.lineWidth,
                                fromConstraint: constraintInfo.name,
                            });
                        }
                        if (constraintInfo.endpoints === "circle" || constraintInfo.name === "Slow Thermometer") {
                            if (!puzzle.circle) {
                                puzzle.circle = [];
                            }

                            for (let instance of puzzleEntry) {
                                puzzle.circle.push({
                                    cells: [instance.lines[0][0]], // Bulb is always the first cell of the first line segment
                                    baseC: constraintInfo.exportBulbColor || constraintInfo.color,
                                    outlineC: constraintInfo.exportBulbColor || constraintInfo.color,
                                    width: constraintInfo.exportBulbSize || 0.85,
                                    height: constraintInfo.exportBulbSize || 0.85,
                                    fromConstraint: constraintInfo.name,
                                });

                                if (constraintInfo.endpoints === "circle") { // Only for Double Arrow type endpoints
                                    const lastIndex = instance.lines[0].length - 1;
                                    puzzle.circle.push({
                                        cells: [instance.lines[0][lastIndex]],
                                        baseC: constraintInfo.exportBulbColor || constraintInfo.color,
                                        outlineC: constraintInfo.exportBulbColor || constraintInfo.color,
                                        width: constraintInfo.exportBulbSize || 0.85,
                                        height: constraintInfo.exportBulbSize || 0.85,
                                        fromConstraint: constraintInfo.name,
                                    });
                                }
                            }
                        }
                    } else if (constraintInfo.type === "cage") {
                        if (!puzzle.cage) {
                            puzzle.cage = [];
                        }

                        puzzle.cage.push({
                            cells: puzzleEntry.flatMap((inst) => inst.cells),
                            outlineC: constraintInfo.color,
                            fontC: "#000000",
                            fromConstraint: constraintInfo.name,
                        });
                    } else if (constraintInfo.type === "cell") {
                        if (!puzzle.circle) {
                            puzzle.circle = [];
                        }

                        for (let instance of puzzleEntry) {
                            for (let cell of instance.cells || []) {
                                puzzle.circle.push({
                                    cells: [cell],
                                    baseC: constraintInfo.color,
                                    outlineC: constraintInfo.color,
                                    fontC: "#000000",
                                    value: constraintInfo.symbol || "",
                                    width: 0.65,
                                    height: 0.65,
                                    fromConstraint: constraintInfo.name,
                                });
                            }
                        }
                    } else if (constraintInfo.type === "outside") {
                        if (!puzzle.text) {
                            puzzle.text = [];
                        }

                        for (let instance of puzzleEntry) {
                             puzzle.text.push({
                                cells: [instance.cell],
                                value: constraintInfo.symbol,
                                fontC: "#000000",
                                size: 1.2,
                                fromConstraint: constraintInfo.name,
                            });

                            puzzle.text.push({
                                cells: [instance.cell],
                                value: instance.value,
                                fontC: "#000000",
                                size: 0.7,
                                fromConstraint: constraintInfo.name,
                            });
                        }
                    }
                }
            }

            appendCustomInstanceMetadata(puzzle);
            appendCustomConstraintPayloads(puzzle);

            // Export as a single whisper constraint with a configurable difference
            const allWhispers = [];

            for (let germanwhisper of puzzle.germanwhispers || []) {
                allWhispers.push({
                    lines: germanwhisper.lines,
                    value: "" + Math.ceil(size / 2),
                });
            }

            for (let dutchwhisper of puzzle.dutchwhispers || []) {
                allWhispers.push({
                    lines: dutchwhisper.lines,
                    value: "" + (Math.ceil(size / 2) - 1),
                });
            }

            if (allWhispers.length > 0) {
                puzzle.whispers = allWhispers;
                delete puzzle.germanwhispers;
                delete puzzle.dutchwhispers;
            }

            // Persist the puzzle's active constraint set
            puzzle.activeConstraints = [...puzzleActiveConstraints];

            return compressor.compressToBase64(JSON.stringify(puzzle));
        };

        const origImportPuzzle = importPuzzle;
        importPuzzle = function (string, clearHistory) {
            // Remove any generated cosmetics
            const puzzle = JSON.parse(compressor.decompressFromBase64(string));
            mergeCustomDefinitionsFromPuzzle(puzzle);
            let constraintNames = newConstraintInfo.map((c) => c.name);
            if (puzzle.line) {
                let filteredLines = [];
                for (let line of puzzle.line) {
                    // Upgrade from old boolean
                    if (line.isNewConstraint) {
                        line.fromConstraint = line.outlineC === "#C060C0" ? "Renban" : line.outlineC === "#67F067" ? "German Whispers" : line.outlineC === "#6495ED" ? "Slow Thermometer" : "Entropic";
                        delete line.isNewConstraint;
                    }

                    // Upgrade "Whispers" to "German Whispers"
                    if (line.fromConstraint === "Whispers") {
                        line.fromConstraint = "German Whispers";
                    }

                    if (!line.fromConstraint || !constraintNames.includes(line.fromConstraint)) {
                        filteredLines.push(line);
                    }
                }
                if (filteredLines.length > 0) {
                    puzzle.line = filteredLines;
                } else {
                    delete puzzle.line;
                }
            }

            if (puzzle.cage) {
                let filteredCages = [];
                for (let cage of puzzle.cage) {
                    if (!cage.fromConstraint || !constraintNames.includes(cage.fromConstraint)) {
                        filteredCages.push(cage);
                    }
                }
                if (filteredCages.length > 0) {
                    puzzle.cage = filteredCages;
                } else {
                    delete puzzle.cage;
                }
            }

            if (puzzle.text) {
                let filteredText = [];
                for (let text of puzzle.text) {
                    if (!text.fromConstraint || !constraintNames.includes(text.fromConstraint)) {
                        filteredText.push(text);
                    }
                }
                if (filteredText.length > 0) {
                    puzzle.text = filteredText;
                } else {
                    delete puzzle.text;
                }
            }

            if (puzzle.circle) {
                let filteredCircle = [];
                for (let circle of puzzle.circle) {
                    if (!circle.fromConstraint || !constraintNames.includes(circle.fromConstraint)) {
                        filteredCircle.push(circle);
                    }
                }

                if (filteredCircle.length > 0) {
                    puzzle.circle = filteredCircle;
                } else {
                    delete puzzle.circle;
                }
            }

            if (puzzle.whispers) {
                let germanwhispers = [];
                let dutchwhispers = [];
                const germanWhisperDiff = "" + Math.ceil(size / 2);
                const dutchWhisperDiff = "" + (Math.ceil(size / 2) - 1);
                for (let whispers of puzzle.whispers) {
                    if (!whispers.value || whispers.value === germanWhisperDiff) {
                        germanwhispers.push(whispers);
                        delete germanwhispers[germanwhispers.length - 1].value;
                    } else if (whispers.value === dutchWhisperDiff) {
                        dutchwhispers.push(whispers);
                        delete dutchwhispers[dutchwhispers.length - 1].value;
                    }
                }

                if (germanwhispers.length > 0) {
                    puzzle.germanwhispers = germanwhispers;
                }
                if (dutchwhispers.length > 0) {
                    puzzle.dutchwhispers = dutchwhispers;
                }
                delete puzzle.whispers;
            }

            // Determine active constraints BEFORE calling origImportPuzzle.
            // f-puzzles calls createGrid → createConstraints inside origImportPuzzle, which
            // initialises the constraints[] map from toolConstraints.  We must update
            // puzzleActiveConstraints (and therefore toolConstraints) first so every
            // constraint that needs to be placed has a slot in constraints[].
            const allKnownNames = [...(builtinToolConstraints || []), ...newConstraintInfo.map(i => i.name)];
            if (Array.isArray(puzzle.activeConstraints)) {
                puzzleActiveConstraints = new Set(puzzle.activeConstraints);
                refreshCustomConstraintRegistration();
            } else {
                // Old puzzle: activate everything so createConstraints registers all slots,
                // then we'll trim to only what was actually placed afterwards.
                puzzleActiveConstraints = new Set(allKnownNames);
                refreshCustomConstraintRegistration();
            }

            // Suppress the createGrid override's reset while origImportPuzzle runs
            importingPuzzle = true;
            try {
                string = compressor.compressToBase64(JSON.stringify(puzzle));
                origImportPuzzle(string, clearHistory);
            } finally {
                importingPuzzle = false;
            }
            restoreCustomInstanceMetadata(puzzle);

            // For old puzzles without stored activeConstraints: trim to only used constraints
            if (!Array.isArray(puzzle.activeConstraints)) {
                puzzleActiveConstraints = new Set();
                for (const name of allKnownNames) {
                    const id = cID(name);
                    const v = constraints[id];
                    if (Array.isArray(v) ? v.length > 0 : !!v) puzzleActiveConstraints.add(name);
                }
                refreshCustomConstraintRegistration();
            }
        };

        // Flag set during importPuzzle to prevent createGrid from resetting puzzle state
        let importingPuzzle = false;

        // Reset active constraints when the user starts a new puzzle (not during import)
        const origCreateGrid = createGrid;
        createGrid = function(newSize, clearHistory, refreshCandidates) {
            origCreateGrid(newSize, clearHistory, refreshCandidates);
            if (!importingPuzzle) {
                puzzleActiveConstraints = new Set();
                refreshCustomConstraintRegistration();
            }
        };

        // Draw the new constraints
        const origDrawConstraints = drawConstraints;
        drawConstraints = function (layer) {
            if (layer === "Bottom") {
                for (let info of newConstraintInfo) {
                    const id = cID(info.name);
                    const constraint = constraints[id];
                    if (constraint) {
                        if (info.type !== "cage") {
                            for (let a = 0; a < constraint.length; a++) {
                                constraint[a].show();
                            }
                        } else {
                            let cells = constraint.flatMap((inst) => inst.cells).filter((cell) => cell);
                            if (cells.length > 0) {
                                drawSolidCage(cells, info.color, info.colorDark);
                            }
                        }
                    }
                }
            }
            origDrawConstraints(layer);
        };

        const nfaAllowsValues = function(nfa, values, numValues) {
            if (!nfa) return true;
            let current = new Set(nfa.getStartIds());
            for (const value of values) {
                const allowedValues = value ? [value] : digits.slice(0, numValues);
                const next = new Set();
                for (const stateId of current) {
                    const transitions = nfa.getStateTransitions(stateId);
                    for (const allowedValue of allowedValues) {
                        const targets = transitions[allowedValue - 1];
                        if (targets) {
                            for (const target of targets) {
                                next.add(target);
                            }
                        }
                    }
                }
                if (!next.size) return false;
                current = next;
            }
            for (const stateId of current) {
                if (nfa.isAccepting(stateId)) return true;
            }
            return false;
        };

        const binaryAllowsCandidate = function(tableMasks, group, cell, value) {
            if (!tableMasks || group.length !== 2) return true;
            const index = group.indexOf(cell);
            if (index < 0) return true;
            const other = group[1 - index];
            const otherValues = other.value ? [other.value] : other.candidates && other.candidates.length ? other.candidates : digits;
            if (index === 0) {
                const mask = tableMasks[value - 1] || 0;
                return otherValues.some(v => (mask & (1 << (v - 1))) !== 0);
            }
            return otherValues.some(v => ((tableMasks[v - 1] || 0) & (1 << (value - 1))) !== 0);
        };

        const customConstraintCandidatePossible = function(n, cell) {
            for (const info of customConstraintInfos()) {
                const definition = customDefinitionForInfo(info);
                const instances = constraints[cID(info.name)] || [];
                for (const instance of instances) {
                    const groups = customGroupsForInstance(info, instance);
                    if (!groups.some(group => group.includes(cell))) continue;
                    let compiled;
                    try {
                        compiled = compileCustomConstraint(definition, customArgsForInstance(definition, instance), size);
                    } catch (e) {
                        console.warn(`Could not evaluate custom constraint "${definition.name}".`, e);
                        continue;
                    }
                    for (const group of groups) {
                        const index = group.indexOf(cell);
                        if (index < 0) continue;
                        if (compiled.type === "binary") {
                            if (!binaryAllowsCandidate(compiled.tableMasks, group, cell, n)) return false;
                        } else if (compiled.runtimeNFA) {
                            const values = group.map((groupCell, groupIndex) => groupIndex === index ? n : groupCell.value || 0);
                            if (!nfaAllowsValues(compiled.runtimeNFA, values, size)) return false;
                        }
                    }
                }
            }
            return true;
        };

        // Conflict highlighting for new constraints
        const origCandidatePossibleInCell = candidatePossibleInCell;
        candidatePossibleInCell = function (n, cell, options) {
            if (!options) {
                options = {};
            }
            if (!options.bruteForce && cell.value) {
                return cell.value === n;
            }

            if (!origCandidatePossibleInCell(n, cell, options)) {
                return false;
            }

            // Renban
            const constraintsRenban = constraints[cID("Renban")];
            if (constraintsRenban && constraintsRenban.length > 0) {
                for (let renban of constraintsRenban) {
                    for (let line of renban.lines) {
                        const index = line.indexOf(cell);
                        if (index > -1) {
                            let numMatchingValue = 0;
                            let minValue = -1;
                            let maxValue = -1;
                            for (let lineCell of line) {
                                if (lineCell.value) {
                                    minValue = minValue === -1 || minValue > lineCell.value ? lineCell.value : minValue;
                                    maxValue = maxValue === -1 || maxValue < lineCell.value ? lineCell.value : maxValue;
                                    if (lineCell.value === n) {
                                        numMatchingValue++;
                                        if (numMatchingValue > 1) {
                                            return false;
                                        }
                                    }
                                }
                            }
                            if (minValue !== -1 && maxValue !== -1) {
                                if (n - minValue > line.length - 1 || maxValue - n > line.length - 1) {
                                     return false;
                                }
                            }
                        }
                    }
                }
            }

            // German Whispers
            const constraintsGermanWhispers = constraints[cID("German Whispers")];
            if (constraintsGermanWhispers && constraintsGermanWhispers.length > 0) {
                const whispersDiff = Math.ceil(size / 2);
                for (let whispers of constraintsGermanWhispers) {
                    for (let line of whispers.lines) {
                        const index = line.indexOf(cell);
                        if (index > -1) {
                            if (n - whispersDiff <= 0 && n + whispersDiff > size) {
                                return false;
                            }

                            if (index > 0) {
                                const prevCell = line[index - 1];
                                if (prevCell.value && Math.abs(prevCell.value - n) < whispersDiff) {
                                    return false;
                                }
                            }
                            if (index < line.length - 1) {
                                const nextCell = line[index + 1];
                                if (nextCell.value && Math.abs(nextCell.value - n) < whispersDiff) {
                                    return false;
                                }
                            }
                        }
                    }
                }
            }

            // Dutch Whispers
            const constraintsDutchWhispers = constraints[cID("Dutch Whispers")];
            if (constraintsDutchWhispers && constraintsDutchWhispers.length > 0) {
                const whispersDiff = Math.ceil(size / 2) - 1;
                for (let whispers of constraintsDutchWhispers) {
                    for (let line of whispers.lines) {
                        const index = line.indexOf(cell);
                        if (index > -1) {
                             if (n - whispersDiff <= 0 && n + whispersDiff > size) {
                                return false;
                            }

                            if (index > 0) {
                                const prevCell = line[index - 1];
                                if (prevCell.value && Math.abs(prevCell.value - n) < whispersDiff) {
                                    return false;
                                }
                            }
                            if (index < line.length - 1) {
                                const nextCell = line[index + 1];
                                if (nextCell.value && Math.abs(nextCell.value - n) < whispersDiff) {
                                    return false;
                                }
                            }
                        }
                    }
                }
            }

            // Entropic Line
            const constraintsEntropicLine = constraints[cID("Entropic Line")];
            if (constraintsEntropicLine && constraintsEntropicLine.length > 0) {
                // Always split into three group of "equal" size.
                // If the size isn't divisible by 3, then the middle group is the one that differs in size from the others.
                const smallGroupSize = Math.floor(size / 3);
                const largeGroupSize = Math.ceil(size / 3);
                const group1Size = size % 3 === 1 ? smallGroupSize : largeGroupSize;
                const group2Size = size % 3 === 1 ? largeGroupSize : smallGroupSize;
                const group3Size = size % 3 === 1 ? smallGroupSize : largeGroupSize;

                let currentNumber = 1;
                const entropicLineGroups = [group1Size, group2Size, group3Size].map((groupSize) => {
                    const group = [];
                    for (let i = 0; i < groupSize; i++) {
                        group.push(currentNumber++);
                    }
                    return group;
                });

                function getGroup(val) {
                    return entropicLineGroups.find((group) => group.includes(val));
                }

                for (let entropicLine of constraintsEntropicLine) {
                    for (let line of entropicLine.lines) {
                        const index = line.indexOf(cell);
                        const nGroup = getGroup(n);
                        if (nGroup !== null && index > -1) {
                            const startIndex = Math.max(0, index - 2);
                            const endIndex = Math.min(line.length - 1, index + 2);
                            for (let i = startIndex; i <= endIndex; i++) {
                                if (i === index || line[i].value === 0) {
                                    continue;
                                }

                                const cellGroup = getGroup(line[i].value);
                                if (cellGroup === nGroup) {
                                    return false;
                                }
                            }
                        }
                    }
                }
            }

            // Modular Line
            const constraintsModularLine = constraints[cID("Modular Line")];
            if (constraintsModularLine && constraintsModularLine.length > 0) {
                const modularLineGroups = [[], [], []];
                for (let v = 1; v <= size; v++) {
                    modularLineGroups[v % 3].push(v);
                }

                function getGroup(val) {
                    return modularLineGroups.find((group) => group.includes(val));
                }

                for (let modularLine of constraintsModularLine) {
                    for (let line of modularLine.lines) {
                        const index = line.indexOf(cell);
                        const nGroup = getGroup(n);
                        if (nGroup !== null && index > -1) {
                            const startIndex = Math.max(0, index - 2);
                            const endIndex = Math.min(line.length - 1, index + 2);
                            for (let i = startIndex; i <= endIndex; i++) {
                                if (i === index || line[i].value === 0) {
                                    continue;
                                }

                                const cellGroup = getGroup(line[i].value);
                                if (cellGroup === nGroup) {
                                    return false;
                                }
                            }
                        }
                    }
                }
            }

            // Region Sum Lines
            const constraintsRegionSumLines = constraints[cID("Region Sum Line")];
            if (constraintsRegionSumLines && constraintsRegionSumLines.length > 0) {
                for (let regionSumLine of constraintsRegionSumLines) {
                    for (let line of regionSumLine.lines) {
                        const index = line.indexOf(cell);
                        if (index > -1) {
                            const completedSums = [];
                            let lastRegion = null;
                            let currentSum = 0;
                            let currentIsComplete = true;
                            for (let lineIndex = 0; lineIndex < line.length; lineIndex++) {
                                const lineCell = line[lineIndex];
                                const region = lineCell.region;
                                if (region !== lastRegion) {
                                    if (region >= 0 && currentIsComplete && currentSum > 0) {
                                        completedSums.push(currentSum);
                                    }
                                    currentSum = 0;
                                    currentIsComplete = true;
                                }
                                if (region !== null && currentIsComplete) {
                                    const value = lineIndex === index ? n : lineCell.value;
                                    if (value) {
                                        currentSum += value;
                                } else {
                                        currentIsComplete = false;
                                }
                            }
                                lastRegion = region;
                            }

                            // Check the last region
                            if (lastRegion >= 0 && currentIsComplete && currentSum > 0) {
                                completedSums.push(currentSum);
                            }

                            if (completedSums.length > 1 && !completedSums.every((sum) => sum === completedSums[0])) {
                                    return false;
                            }
                        }
                    }
                }
            }

            // Nabner
            const constraintsNabner = constraints[cID("Nabner")];
            if (constraintsNabner && constraintsNabner.length > 0) {
                for (let nabner of constraintsNabner) {
                    for (let line of nabner.lines) {
                        const index = line.indexOf(cell);
                        if (index > -1) {
                            for (let lineCell of line) {
                                if (lineCell !== cell && lineCell.value) {
                                    if (Math.abs(lineCell.value - n) <= 1) {
                                        return false;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Double Arrow
            const constraintsDoubleArrow = constraints[cID("Double Arrow")];
            if (constraintsDoubleArrow && constraintsDoubleArrow.length > 0) {
                for (let doubleArrow of constraintsDoubleArrow) {
                    for (let line of doubleArrow.lines) {
                        const index = line.indexOf(cell);
                        if (index > -1) {
                            let missingValue = false;
                            let circleSum = 0;
                            if (index === 0) {
                                circleSum += n;
                            } else if (!line[0].value) {
                                missingValue = true;
                            } else {
                                circleSum += line[0].value;
                            }

                            if (index === line.length - 1) {
                                circleSum += n;
                            } else if (!line[line.length - 1].value) {
                                missingValue = true;
                            } else {
                                circleSum += line[line.length - 1].value;
                            }

                            let lineSum = 0;
                            for (let lineIndex = 1; lineIndex < line.length - 1; lineIndex++) {
                                if (index === lineIndex) {
                                    lineSum += n;
                                } else {
                                    if (!line[lineIndex].value) {
                                        missingValue = true;
                                        break;
                                    }
                                    lineSum += line[lineIndex].value;
                                }
                            }
                            if (!missingValue && lineSum !== circleSum) {
                                return false;
                            }
                        }
                    }
                }
            }

            // Zipper Line
            const constraintsZipperLine = constraints[cID("Zipper Line")];
            if (constraintsZipperLine && constraintsZipperLine.length > 0) {
                for (let zipperLine of constraintsZipperLine) {
                    for (let line of zipperLine.lines) {
                        let sum = -1;
                        const index = line.indexOf(cell);
                        if (index > -1) {
                            for (let i = 0; i < (line.length + 1) / 2; i++) {
                                let index0 = i;
                                let index1 = line.length - i - 1;
                                let value0 = index0 == index ? n : line[index0].value;
                                let value1 = index1 == index ? n : line[index1].value;
                                if (value0 && value1) {
                                    if (index0 == index1) {
                                        if (sum == -1) {
                                            sum = value0;
                                        } else if (sum != value0) {
                                        return false;
                                    }
                                    } else if (sum == -1) {
                                        sum = value0 + value1;
                                    } else if (sum != value0 + value1) {
                                    return false;
                                }
                            }
                        }
                    }
                }
            }
        }
            
            // Slow Thermometer
            const constraintsSlowThermo = constraints[cID("Slow Thermometer")];
            if (constraintsSlowThermo && constraintsSlowThermo.length > 0) {
                for (let slowThermo of constraintsSlowThermo) {
                    for (let line of slowThermo.lines) {
                        const index = line.indexOf(cell);
                        if (index > -1) {
                            // Check against previous cell
                            if (index > 0) {
                                const prevCell = line[index - 1];
                                if (prevCell.value !== 0) {
                                    if (n < prevCell.value) return false;
                                } else { // prevCell is unsolved
                                    if (prevCell.candidates.every(pc => n < pc)) return false;
                                }
                            }
                            // Check against next cell
                            if (index < line.length - 1) {
                                const nextCell = line[index + 1];
                                if (nextCell.value !== 0) {
                                    if (n > nextCell.value) return false;
                                } else { // nextCell is unsolved
                                    if (nextCell.candidates.every(nc => n > nc)) return false;
                                }
                            }
                        }
                    }
                }
            }


            // Row Indexer
            const constraintsRowIndexer = constraints[cID("Row Indexer")];
            if (constraintsRowIndexer && constraintsRowIndexer.some((rowIndexer) => rowIndexer.cells.indexOf(cell) > -1)) {
                            let targetCell = grid[n - 1][cell.j];
                            if (targetCell !== cell && targetCell.value) {
                                if (targetCell.value > 0 && targetCell.value !== cell.i + 1) {
                                    return false;
                    }
                }
            }

            // Column Indexer
            const constraintsColumnIndexer = constraints[cID("Column Indexer")];
            if (constraintsColumnIndexer && constraintsColumnIndexer.some((columnIndexer) => columnIndexer.cells.indexOf(cell) > -1)) {
                            let targetCell = grid[cell.i][n - 1];
                            if (targetCell !== cell && targetCell.value) {
                                if (targetCell.value > 0 && targetCell.value !== cell.j + 1) {
                                    return false;
                    }
                }
            }

            // Box Indexer
            const constraintsBoxIndexer = constraints[cID("Box Indexer")];
            if (constraintsBoxIndexer && constraintsBoxIndexer.some((boxIndexer) => boxIndexer.cells.indexOf(cell) > -1)) {
                        let region = cell.region;
                        if (region >= 0) {
                            let regionCells = [];
                    for (let i = 0; i < size; i++) {
                        for (let j = 0; j < size; j++) {
                            if (grid[i][j].region === region) {
                                regionCells.push(grid[i][j]);
                                    }
                                }
                            }
                    if (regionCells.length == size) {
                        let cellRegionIndex = regionCells.indexOf(cell);
                        let targetCell = regionCells[n - 1];
                                    if (targetCell !== cell && targetCell.value) {
                            if (targetCell.value > 0 && targetCell.value !== cellRegionIndex + 1) {
                                            return false;
                            }
                        }
                    }
                }
            }

            // X-Sums
            const constraintsXSum = constraints[cID("X Sum")];
            if (constraintsXSum && constraintsXSum.length > 0) {
                for (let xSum of constraintsXSum) {
                    if (xSum.value.length && !isNaN(parseInt(xSum.value))) {
                        const index = xSum.set.indexOf(cell);
                        if (index > -1) {
                            const numCells = index === 0 ? n : xSum.set[0].value;
                            if (numCells !== 0 && index < numCells) {
                                const xSumValue = parseInt(xSum.value);

                                if (index === 0) {
                                    let minVal = numCells;
                                    let numVals = 1;
                                    if (numCells > 1) {
                                        for (let v = 1; v <= size; v++) {
                                            if (v !== numCells) {
                                                minVal += v;
                                                numVals++;
                                                if (numVals == numCells) {
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                    let maxVal = numCells;
                                    numVals = 1;
                                    if (numCells > 1) {
                                        for (let v = size; v > 0; v--) {
                                            if (v !== numCells) {
                                                maxVal += v;
                                                numVals++;
                                                if (numVals == numCells) {
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                    if (xSumValue > maxVal || xSumValue < minVal) {
                                        return false;
                                    }
                                }

                                let sumValid = true;
                                let sum = 0;
                                for (let ci = 0; ci < numCells; ci++) {
                                    let cell = xSum.set[ci];
                                    if (ci === index) {
                                        sum += n;
                                    } else if (cell.value) {
                                        sum += cell.value;
                                    } else {
                                        sumValid = false;
                                    }
                                }

                                if ((sumValid && sum !== xSumValue) || (!sumValid && sum > xSumValue)) {
                                    return false;
                                }
                            }
                        }
                    }
                }
            }

            // Skyscraper
            const constraintsSkyscraper = constraints[cID("Skyscraper")];
            if (constraintsSkyscraper && constraintsSkyscraper.length > 0) {
                for (let skyscraper of constraintsSkyscraper) {
                    if (skyscraper.value.length && !isNaN(parseInt(skyscraper.value))) {
                        const index = skyscraper.set.indexOf(cell);
                        if (index > -1) {
                            const skyscraperValue = parseInt(skyscraper.value);
                            if (index + size - n + 1 < skyscraperValue) {
                                return false;
                            }

                            let seenCells = 0;
                            let maxValIndex = -1;
                            let maxVal = 0;
                            let haveAllVals = true;
                            for (let ci = 0; ci < skyscraper.set.length; ci++) {
                                let value = ci === index ? n : skyscraper.set[ci].value;
                                if (value !== 0) {
                                    if (maxVal < value) {
                                        seenCells++;
                                        maxVal = value;
                                        maxValIndex = ci;
                                    }
                                } else {
                                    haveAllVals = false;
                                }
                            }

                            if ((haveAllVals && seenCells !== skyscraperValue) || seenCells > skyscraperValue) {
                                return false;
                            }
                        }
                    }
                }
            }

            if (!customConstraintCandidatePossible(n, cell)) {
                return false;
            }

            return true;
        };

        // Constraint classes

        // Renban
        window.renban = function (cell) {
            this.lines = [[cell]];

            this.show = function () {
                const renbanInfo = newConstraintInfo.filter((c) => c.name === "Renban")[0];
                for (var a = 0; a < this.lines.length; a++) {
                    drawLine(this.lines[a], renbanInfo.color, renbanInfo.colorDark, renbanInfo.lineWidth);
                }
            };

            this.addCellToLine = function (cell) {
                if (this.lines[this.lines.length - 1].length < size) {
                    this.lines[this.lines.length - 1].push(cell);
                }
            };
        };

        // German Whispers
        window.germanwhispers = function (cell) {
            this.lines = [[cell]];

            this.show = function () {
                const whispersInfo = newConstraintInfo.filter((c) => c.name === "German Whispers")[0];
                for (var a = 0; a < this.lines.length; a++) {
                    drawLine(this.lines[a], whispersInfo.color, whispersInfo.colorDark, whispersInfo.lineWidth);
                }
            };

            this.addCellToLine = function (cell) {
                this.lines[this.lines.length - 1].push(cell);
            };
        };

        // Dutch Whispers
        window.dutchwhispers = function (cell) {
            this.lines = [[cell]];

            this.show = function () {
                const whispersInfo = newConstraintInfo.filter((c) => c.name === "Dutch Whispers")[0];
                for (var a = 0; a < this.lines.length; a++) {
                    drawLine(this.lines[a], whispersInfo.color, whispersInfo.colorDark, whispersInfo.lineWidth);
                }
            };

            this.addCellToLine = function (cell) {
                this.lines[this.lines.length - 1].push(cell);
            };
        };

        // Entropic Line
        window.entropicline = function (cell) {
            this.lines = [[cell]];

            this.show = function () {
                const entropicLineInfo = newConstraintInfo.filter((c) => c.name === "Entropic Line")[0];
                for (var a = 0; a < this.lines.length; a++) {
                    drawLine(this.lines[a], entropicLineInfo.color, entropicLineInfo.colorDark, entropicLineInfo.lineWidth);
                }
            };

            this.addCellToLine = function (cell) {
                this.lines[this.lines.length - 1].push(cell);
            };
        };

        // Modular Line
        window.modularline = function (cell) {
            this.lines = [[cell]];

            this.show = function () {
                const modularLineInfo = newConstraintInfo.filter((c) => c.name === "Modular Line")[0];
                for (var a = 0; a < this.lines.length; a++) {
                    drawLine(this.lines[a], modularLineInfo.color, modularLineInfo.colorDark, modularLineInfo.lineWidth);
                }
            };

            this.addCellToLine = function (cell) {
                this.lines[this.lines.length - 1].push(cell);
            };
        };

        // Region Sum Lines
        window.regionsumline = function (cell) {
            this.lines = [[cell]];

            this.show = function () {
                const regionSumLineInfo = newConstraintInfo.filter((c) => c.name === "Region Sum Line")[0];
                for (var a = 0; a < this.lines.length; a++) {
                    drawLine(this.lines[a], regionSumLineInfo.color, regionSumLineInfo.colorDark, regionSumLineInfo.lineWidth);
                }
            };

            this.addCellToLine = function (cell) {
                this.lines[this.lines.length - 1].push(cell);
            };
        };

        // Nabner Lines
        window.nabner = function (cell) {
            this.lines = [[cell]];

            this.show = function () {
                const nabnerLineInfo = newConstraintInfo.filter((c) => c.name === "Nabner")[0];
                for (var a = 0; a < this.lines.length; a++) {
                    drawLine(this.lines[a], nabnerLineInfo.color, nabnerLineInfo.colorDark, nabnerLineInfo.lineWidth);
                }
            };

            this.addCellToLine = function (cell) {
                this.lines[this.lines.length - 1].push(cell);
            };
        };

        // Double Arrows
        window.doublearrow = function (cell) {
            this.lines = [[cell]];

            this.show = function () {
                const doubleArrowInfo = newConstraintInfo.filter((c) => c.name === "Double Arrow")[0];
                const doubleArrowColor = boolSettings["Dark Mode"] ? doubleArrowInfo.colorDark : doubleArrowInfo.color;
                for (let i = 0; i < this.lines.length; i++) {
                    ctx.lineWidth = cellSL * doubleArrowInfo.lineWidth * 0.5;

                    ctx.strokeStyle = doubleArrowColor;
                    ctx.beginPath();
                    ctx.moveTo(this.lines[i][0].x + cellSL / 2, this.lines[i][0].y + cellSL / 2);
                    for (let j = 1; j < this.lines[i].length; j++) ctx.lineTo(this.lines[i][j].x + cellSL / 2, this.lines[i][j].y + cellSL / 2);
                    ctx.stroke();

                    ctx.fillStyle = boolSettings["Dark Mode"] ? "#888888" : "#EAEAEA";
                    for (let j = 0, k = 0; j < this.lines[i].length && (this.lines[i].length > 1 || !k); j += this.lines[i].length - 1, k++) {
                    ctx.beginPath();
                        ctx.arc(this.lines[i][j].x + cellSL / 2, this.lines[i][j].y + cellSL / 2, cellSL / 2 - ctx.lineWidth / 2, 0, Math.PI * 2);
                        ctx.fill();
                        ctx.stroke();
                    }
                }
            };

            this.addCellToLine = function (cell) {
                this.lines[this.lines.length - 1].push(cell);
            };
        };

        // Zipper Lines
        window.zipperline = function (cell) {
            this.lines = [[cell]];

            this.show = function () {
                const zipperLineInfo = newConstraintInfo.filter((c) => c.name === "Zipper Line")[0];
                const zipperLineColor = boolSettings["Dark Mode"] ? zipperLineInfo.colorDark : zipperLineInfo.color;
                for (var a = 0; a < this.lines.length; a++) {
                    drawLine(this.lines[a], zipperLineColor, zipperLineInfo.colorDark, zipperLineInfo.lineWidth);
                }
            };

            this.addCellToLine = function (cell) {
                this.lines[this.lines.length - 1].push(cell);
            };
        };

        // Slow Thermometer
        window.slowthermometer = function (cell) {
            this.lines = [[cell]]; // Each sub-array is a segment of the thermometer

            this.show = function () {
                const thermoInfo = newConstraintInfo.filter((c) => c.name === "Slow Thermometer")[0];
                for (let i = 0; i < 2; i++) { // Two passes for double line
                    for (let a = 0; a < this.lines.length; a++) {
                        const currentLine = this.lines[a];
                        if (currentLine.length === 0) continue;

                        ctx.lineWidth = cellSL * (i ? thermoInfo.innerLineWidth : thermoInfo.outerLineWidth);
                        const bulbRadiusFactor = i ? thermoInfo.innerBulbRadiusFactor : thermoInfo.outerBulbRadiusFactor;
                        
                        if (i) { // Inner line color
                            ctx.fillStyle = boolSettings['Dark Mode'] ? thermoInfo.innerColorDark : thermoInfo.innerColor;
                            ctx.strokeStyle = boolSettings['Dark Mode'] ? thermoInfo.innerColorDark : thermoInfo.innerColor;
                        } else { // Outer line color
                            ctx.fillStyle = boolSettings['Dark Mode'] ? thermoInfo.outerColorDark : thermoInfo.outerColor;
                            ctx.strokeStyle = boolSettings['Dark Mode'] ? thermoInfo.outerColorDark : thermoInfo.outerColor;
                        }
                        
                        // Bulb
                        ctx.beginPath();
                        ctx.arc(currentLine[0].x + cellSL / 2, currentLine[0].y + cellSL / 2, cellSL * bulbRadiusFactor, 0, Math.PI * 2);
                        ctx.fill();

                        // Line
                        if (currentLine.length > 1) {
                            ctx.beginPath();
                            ctx.moveTo(currentLine[0].x + cellSL / 2, currentLine[0].y + cellSL / 2);
                            for (let b = 1; b < currentLine.length; b++) {
                                ctx.lineTo(currentLine[b].x + cellSL / 2, currentLine[b].y + cellSL / 2);
                            }
                            ctx.stroke();
                            // End cap for the line itself (not a second bulb)
                            ctx.beginPath();
                            ctx.arc(currentLine[currentLine.length - 1].x + cellSL / 2, currentLine[currentLine.length - 1].y + cellSL / 2, ctx.lineWidth / 2, 0, Math.PI * 2);
                            ctx.fill();
                        }
                    }
                }
            };

            this.addCellToLine = function (cell) {
                this.lines[this.lines.length - 1].push(cell);
            };
        };


        // Row Indexer
        window.rowindexer = function (cell) {
            this.cells = [cell];

            this.addCellToRegion = function (cell) {
                this.cells.push(cell);
                this.sortCells();
            };

            this.sortCells = function () {
                this.cells.sort((a, b) => a.i * size + a.j - (b.i * size + b.j));
            };
        };

        // Column Indexer
        window.columnindexer = window.rowindexer;

        // Box Indexer
        window.boxindexer = window.rowindexer;

        window.xsum = function (cells) {
            if (cells) this.cell = cells[0];
            this.set = null;
            this.value = "";
            this.isReverse = false;
            this.isRow = false;

            this.show = function () {
                ctx.fillStyle = boolSettings["Dark Mode"] ? "#F0F0F0" : "#000000";
                ctx.font = cellSL * 1.0 + "px Arial";
                const iconOffset = cellSL * 0.1 * (this.isReverse ? -1 : 1);
                const iconBaseX = this.cell.x + cellSL / 2;
                const iconBaseY = this.cell.y + cellSL * 0.87;
                if (this.isRow) {
                    ctx.fillText("\u25EF", iconBaseX - iconOffset, iconBaseY);
                } else {
                    ctx.fillText("\u25EF", iconBaseX, iconBaseY - iconOffset);
                }
                ctx.font = cellSL * 0.6 + "px Arial";
                let textOffset = 0;
                if (this.value.length <= 1) {
                    textOffset = this.isReverse ? cellSL * -0.1 : cellSL * 0.09;
                } else {
                    textOffset = this.isReverse ? cellSL * -0.11 : cellSL * 0.08;
                }

                let textOffsetX = 0;
                if (this.value.length == 2 && this.value[0] == "1" && this.value[1] != "1") {
                    textOffsetX = cellSL * 0.03;
                } else if (this.value.length == 2 && this.value[0] != "1" && this.value[1] == "1") {
                    textOffsetX = cellSL * -0.03;
                }

                const textBaseX = this.cell.x + cellSL / 2;
                const textBaseY = this.cell.y + cellSL * 0.75;
                if (this.isRow) {
                    ctx.fillText(this.value.length ? this.value : "-", textBaseX - textOffset - textOffsetX, textBaseY);
                } else {
                    ctx.fillText(this.value.length ? this.value : "-", textBaseX - textOffsetX, textBaseY - textOffset);
                }
            };

            this.updateSet = function () {
                if (this.cell) {
                    if (this.cell.i >= 0 && this.cell.i < size) {
                        this.isRow = true;
                        this.set = getCellsInRow(this.cell.i);
                        if (this.cell.j >= size) {
                            this.set = this.set.slice(0);
                            this.set.reverse();
                            this.isReverse = true;
                        }
                    }
                    if (this.cell.j >= 0 && this.cell.j < size) {
                        this.isRow = false;
                        this.set = getCellsInColumn(this.cell.j);
                        if (this.cell.i >= size) {
                            this.set = this.set.slice(0);
                            this.set.reverse();
                            this.isReverse = true;
                        }
                    }
                }
            };
            this.updateSet();

            this.typeNumber = function (num) {
                if (this.value.length === 0 && num === "0") {
                    return;
                }

                if (parseInt(this.value + String(num)) <= maxInNCells(size)) {
                    this.value += String(num);
                }
            };
        };

        window.skyscraper = function (cells) {
            if (cells) this.cell = cells[0];
            this.set = null;
            this.value = "";
            this.isReverse = false;
            this.isRow = false;

            this.show = function () {
                ctx.fillStyle = boolSettings["Dark Mode"] ? "#F0F0F0" : "#000000";
                ctx.font = cellSL * 1.0 + "px Arial";
                const iconOffset = cellSL * 0.13 * (this.isReverse ? -1 : 1);
                const iconBaseX = this.cell.x + cellSL / 2;
                const iconBaseY = this.cell.y + cellSL * 0.8;
                if (this.isRow) {
                    ctx.fillText("\u25AF", iconBaseX - iconOffset, iconBaseY);
                } else {
                    ctx.fillText("\u25AF", iconBaseX, iconBaseY - iconOffset);
                }
                ctx.font = cellSL * 0.6 + "px Arial";
                const textOffset = cellSL * 0.13 * (this.isReverse ? -1 : 1);
                const textBaseX = this.cell.x + cellSL / 2;
                const textBaseY = this.cell.y + cellSL * 0.75;
                if (this.isRow) {
                    ctx.fillText(this.value.length ? this.value : "-", textBaseX - textOffset, textBaseY);
                } else {
                    ctx.fillText(this.value.length ? this.value : "-", textBaseX, textBaseY - textOffset);
                }
            };

            this.updateSet = function () {
                if (this.cell) {
                    if (this.cell.i >= 0 && this.cell.i < size) {
                        this.isRow = true;
                        this.set = getCellsInRow(this.cell.i);
                        if (this.cell.j >= size) {
                            this.set = this.set.slice(0);
                            this.set.reverse();
                            this.isReverse = true;
                        }
                    }
                    if (this.cell.j >= 0 && this.cell.j < size) {
                        this.isRow = false;
                        this.set = getCellsInColumn(this.cell.j);
                        if (this.cell.i >= size) {
                            this.set = this.set.slice(0);
                            this.set.reverse();
                            this.isReverse = true;
                        }
                    }
                }
            };
            this.updateSet();

            this.typeNumber = function (num) {
                if (this.value.length === 0 && num === "0") {
                    return;
                }

                if (parseInt(this.value + String(num)) <= size) {
                    this.value += String(num);
                }
            };
        };

        const bindCustomValueProperty = function(instance, definition) {
            const firstVariable = definition.variables && definition.variables.length ? definition.variables[0] : null;
            if (!firstVariable) {
                instance.value = "";
                return;
            }
            Object.defineProperty(instance, "value", {
                get: function() {
                    return this.customArgs[firstVariable.name] === undefined ? "" : String(this.customArgs[firstVariable.name]);
                },
                set: function(value) {
                    this.customArgs[firstVariable.name] = String(value || "");
                },
                configurable: true,
            });
        };

        const updateCustomOutsideSet = function(instance) {
            if (instance.cell) {
                instance.isReverse = false;
                instance.isRow = false;
                if (instance.cell.i >= 0 && instance.cell.i < size) {
                    instance.isRow = true;
                    instance.set = getCellsInRow(instance.cell.i);
                    if (instance.cell.j >= size) {
                        instance.set = instance.set.slice(0);
                        instance.set.reverse();
                        instance.isReverse = true;
                    }
                }
                if (instance.cell.j >= 0 && instance.cell.j < size) {
                    instance.isRow = false;
                    instance.set = getCellsInColumn(instance.cell.j);
                    if (instance.cell.i >= size) {
                        instance.set = instance.set.slice(0);
                        instance.set.reverse();
                        instance.isReverse = true;
                    }
                }
            }
        };

        const registerCustomConstraintClasses = function() {
            for (const info of customConstraintInfos()) {
                const definition = customDefinitionForInfo(info);
                window[cID(info.name)] = function(cellsOrCell) {
                    this.customConstraintId = definition.id;
                    this.customArgs = customDefaultArgs(definition);
                    if (info.type === "line") {
                        this.lines = cellsOrCell ? [[cellsOrCell]] : [[]];
                    } else if (info.type === "outside") {
                        if (cellsOrCell) this.cell = cellsOrCell[0];
                        this.set = null;
                        this.isReverse = false;
                        this.isRow = false;
                        bindCustomValueProperty(this, definition);
                        this.updateSet = function() {
                            updateCustomOutsideSet(this);
                        };
                        this.updateSet();
                        this.typeNumber = function(num) {
                            if (this.value.length === 0 && num === "0") return;
                            this.value += String(num);
                        };
                    } else {
                        this.cells = Array.isArray(cellsOrCell) ? cellsOrCell.filter(Boolean) : cellsOrCell ? [cellsOrCell] : [];
                        this.addCellToRegion = function(cell) {
                            this.cells.push(cell);
                            this.sortCells();
                        };
                        this.sortCells = function() {
                            this.cells.sort((a, b) => a.i * size + a.j - (b.i * size + b.j));
                        };
                    }
                    this.show = function() {
                        showCustomConstraintInstance(this, info);
                    };
                    this.addCellToLine = function(cell) {
                        if (!this.lines) this.lines = [[]];
                        this.lines[this.lines.length - 1].push(cell);
                    };
                };
            }
        };

        registerCustomConstraintClasses();
        ensureCustomConstraintStorage();


        const origCategorizeTools = categorizeTools;
        categorizeTools = function () {
            origCategorizeTools();

            // Capture the built-in list once so the Toolbox UI can enumerate them
            if (!builtinToolConstraints) {
                builtinToolConstraints = [...toolConstraints];
            }

            // Filter the built-in picker list down to only puzzle-active constraints
            toolConstraints = toolConstraints.filter(n => puzzleActiveConstraints.has(n));

            // Find the last visible representative of each insertion zone
            const lastVisibleOf = function(candidates) {
                let idx = -1;
                for (const name of candidates) {
                    const i = toolConstraints.lastIndexOf(name);
                    if (i > idx) idx = i;
                }
                return idx;
            };

            const builtinLineNames    = ["Thermometer", "Palindrome", "Arrow", "Between Line"];
            const builtinCageNames    = ["Extra Region", "Killer Cage", "Clone", "Odd", "Even", "Minimum", "Maximum"];
            const builtinOutsideNames = ["Little Killer Sum", "Sandwich Sum"];

            let toolLineIndex    = lastVisibleOf(builtinLineNames);
            let toolPerCellIndex = lastVisibleOf(builtinCageNames);
            let toolOutsideIndex = lastVisibleOf(builtinOutsideNames);

            // Fallback: if reference constraints were hidden, place new ones at the end
            const safeEnd = toolConstraints.length - 1;
            if (toolLineIndex    < 0) toolLineIndex    = safeEnd;
            if (toolPerCellIndex < 0) toolPerCellIndex = safeEnd;
            if (toolOutsideIndex < 0) toolOutsideIndex = safeEnd;

            for (let info of newConstraintInfo) {
                // Always register in behavior arrays regardless of toolbox visibility
                if (info.type === "line") {
                    if (!lineConstraints.includes(info.name)) lineConstraints.push(info.name);
                } else if (info.type === "cage") {
                    if (!regionConstraints.includes(info.name)) regionConstraints.push(info.name);
                } else if (info.type === "cell") {
                    if (!perCellConstraints.includes(info.name)) perCellConstraints.push(info.name);
                } else if (info.type === "outside") {
                    if (!outsideConstraints.includes(info.name)) outsideConstraints.push(info.name);
                    if (!typableConstraints.includes(info.name)) typableConstraints.push(info.name);
                }

                // Skip the picker if not active for this puzzle
                if (!puzzleActiveConstraints.has(info.name)) continue;
                if (toolConstraints.includes(info.name)) continue;

                if (info.type === "line") {
                    if (toolLineIndex < toolPerCellIndex) toolPerCellIndex++;
                    if (toolLineIndex < toolOutsideIndex) toolOutsideIndex++;
                    toolConstraints.splice(++toolLineIndex, 0, info.name);
                } else if (info.type === "cage" || info.type === "cell") {
                    if (toolPerCellIndex < toolLineIndex) toolLineIndex++;
                    if (toolPerCellIndex < toolOutsideIndex) toolOutsideIndex++;
                    toolConstraints.splice(++toolPerCellIndex, 0, info.name);
                } else if (info.type === "outside") {
                    if (toolOutsideIndex < toolLineIndex) toolLineIndex++;
                    if (toolOutsideIndex < toolPerCellIndex) toolPerCellIndex++;
                    toolConstraints.splice(++toolOutsideIndex, 0, info.name);
                }
            }

            draggableConstraints = [...new Set([...lineConstraints, ...regionConstraints])];
            multicellConstraints = [...new Set([...lineConstraints, ...regionConstraints, ...borderConstraints, ...cornerConstraints, ...perCellConstraints.filter(name => newConstraintInfo.find(info => info.name === name && info.type === "cage"))])];
            betweenCellConstraints = [...borderConstraints, ...cornerConstraints];
            allConstraints = [...boolConstraints, ...toolConstraints];

            tools = [...toolConstraints, ...toolCosmetics];
            selectableTools = [...selectableConstraints, ...selectableCosmetics];
            lineTools = [...lineConstraints, ...lineCosmetics];
            regionTools = [...regionConstraints, ...regionCosmetics];
            diagonalRegionTools = [...diagonalRegionConstraints, ...diagonalRegionCosmetics];
            outsideTools = [...outsideConstraints, ...outsideCosmetics];
            outsideCornerTools = [...outsideCornerConstraints, ...outsideCornerCosmetics];
            oneCellAtATimeTools = [...perCellConstraints, ...draggableConstraints, ...draggableCosmetics, ...newConstraintInfo.filter(info => info.type === "cage").map(info => info.name)];
            draggableTools = [...draggableConstraints, ...draggableCosmetics];
            multicellTools = [...multicellConstraints, ...multicellCosmetics];
            ensureCustomConstraintStorage();
        };

        // Tooltips
        for (let info of newConstraintInfo) {
            descriptions[info.name] = info.tooltip;
        }

        let selectedCustomDefinitionIndex = 0;
        let customConstraintManagerOverlay = null;
        let customManagerPreviousDisableInputs = null;

        const escapeHtml = function(value) {
            return String(value || "")
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;");
        };

        const customVariablesJson = function(definition) {
            return JSON.stringify((definition.variables || []).map(variable => ({
                name: variable.name,
                label: variable.label,
                type: variable.type,
                defaultValue: variable.defaultValue,
            })), null, 2);
        };

        const customInstancesForDefinition = function(definition) {
            const info = customConstraintInfos().find(candidate => candidate.customDefinitionId === definition.id);
            if (!info) return { info: null, instances: [] };
            return { info, instances: constraints[cID(info.name)] || [] };
        };

        const describeCustomInstance = function(info, instance) {
            const groups = customGroupsForInstance(info, instance);
            if (!groups.length) return "No cells";
            return groups.map(group => formatCustomCells(group).join(" ")).join(" | ");
        };

        const closeCustomConstraintManager = function() {
            if (customConstraintManagerOverlay) {
                customConstraintManagerOverlay.style.display = "none";
            }
            if (customManagerPreviousDisableInputs !== null) {
                disableInputs = customManagerPreviousDisableInputs;
                customManagerPreviousDisableInputs = null;
            }
        };

        const ensureCustomManagerOverlay = function() {
            if (customConstraintManagerOverlay) return;
            const style = document.createElement("style");
            style.textContent = `
                #fpcc-overlay {
                    position: fixed;
                    inset: 0;
                    z-index: 100000;
                    display: none;
                    align-items: center;
                    justify-content: center;
                    background: rgba(0, 0, 0, 0.45);
                    font-family: Arial, sans-serif;
                    outline: none;
                }
                #fpcc-panel {
                    width: min(1180px, calc(100vw - 48px));
                    height: min(760px, calc(100vh - 48px));
                    background: #f4f4f4;
                    color: #111;
                    border: 1px solid #222;
                    box-shadow: 0 16px 40px rgba(0, 0, 0, 0.35);
                    display: grid;
                    grid-template-columns: 280px 1fr;
                }
                #fpcc-overlay.fpcc-dark #fpcc-panel {
                    background: #252525;
                    color: #eee;
                    border-color: #777;
                }
                .fpcc-list {
                    border-right: 1px solid #999;
                    padding: 14px;
                    overflow: auto;
                }
                .fpcc-main {
                    padding: 14px 16px;
                    overflow: auto;
                }
                .fpcc-header {
                    display: flex;
                    justify-content: space-between;
                    align-items: center;
                    gap: 8px;
                    margin-bottom: 12px;
                }
                .fpcc-title {
                    font-size: 20px;
                    font-weight: 700;
                }
                .fpcc-row {
                    display: grid;
                    grid-template-columns: 150px minmax(0, 1fr);
                    gap: 8px;
                    align-items: center;
                    margin-bottom: 8px;
                }
                .fpcc-row label {
                    font-weight: 700;
                    font-size: 13px;
                }
                .fpcc-row input,
                .fpcc-row select,
                .fpcc-row textarea {
                    width: 100%;
                    box-sizing: border-box;
                    font: 13px Arial, sans-serif;
                    padding: 6px 7px;
                    border: 1px solid #999;
                    border-radius: 3px;
                    background: #fff;
                    color: #111;
                }
                #fpcc-overlay.fpcc-dark .fpcc-row input,
                #fpcc-overlay.fpcc-dark .fpcc-row select,
                #fpcc-overlay.fpcc-dark .fpcc-row textarea {
                    background: #111;
                    color: #eee;
                    border-color: #666;
                }
                .fpcc-row textarea {
                    min-height: 150px;
                    resize: vertical;
                    font-family: Consolas, monospace;
                }
                .fpcc-source textarea {
                    min-height: 260px;
                }
                .fpcc-actions {
                    display: flex;
                    flex-wrap: wrap;
                    gap: 8px;
                    margin: 10px 0;
                }
                .fpcc-actions button,
                .fpcc-list button,
                .fpcc-close {
                    border: 1px solid #777;
                    background: #fff;
                    color: #111;
                    padding: 7px 10px;
                    border-radius: 3px;
                    cursor: pointer;
                    font: 13px Arial, sans-serif;
                }
                #fpcc-overlay.fpcc-dark .fpcc-actions button,
                #fpcc-overlay.fpcc-dark .fpcc-list button,
                #fpcc-overlay.fpcc-dark .fpcc-close {
                    background: #333;
                    color: #eee;
                    border-color: #777;
                }
                .fpcc-list-item {
                    width: 100%;
                    text-align: left;
                    margin-bottom: 6px;
                }
                .fpcc-list-item.fpcc-selected {
                    border-color: #335cff;
                    background: #dfe6ff;
                }
                #fpcc-overlay.fpcc-dark .fpcc-list-item.fpcc-selected {
                    background: #26315f;
                }
                .fpcc-help {
                    font-size: 12px;
                    opacity: 0.78;
                    margin: -2px 0 10px 158px;
                }
                .fpcc-instance {
                    border-top: 1px solid #aaa;
                    padding: 9px 0;
                }
                .fpcc-instance-title {
                    font-weight: 700;
                    margin-bottom: 6px;
                }
                .fpcc-json-output {
                    min-height: 80px;
                }
            `;
            document.head.appendChild(style);
            customConstraintManagerOverlay = document.createElement("div");
            customConstraintManagerOverlay.id = "fpcc-overlay";
            customConstraintManagerOverlay.tabIndex = -1;
            document.body.appendChild(customConstraintManagerOverlay);
            const blockFpuzzlesEvent = function(event) {
                event.stopPropagation();
                if (event.type === "wheel" || event.type === "contextmenu") {
                    event.preventDefault();
                }
                if (event.type === "keydown" && event.key === "Escape") {
                    event.preventDefault();
                    closeCustomConstraintManager();
                }
            };
            [
                "mousedown",
                "mouseup",
                "mousemove",
                "click",
                "dblclick",
                "contextmenu",
                "wheel",
                "touchstart",
                "touchmove",
                "touchend",
                "keydown",
                "keyup",
                "keypress",
            ].forEach(eventName => customConstraintManagerOverlay.addEventListener(eventName, blockFpuzzlesEvent));
        };

        const readCustomDefinitionFromForm = function(existingDefinition) {
            const variablesRaw = document.getElementById("fpcc-variables").value.trim();
            let variables = [];
            if (variablesRaw) {
                variables = JSON.parse(variablesRaw);
                if (!Array.isArray(variables)) throw new Error("Variables must be a JSON array.");
            }
            return normalizeCustomDefinition({
                id: existingDefinition ? existingDefinition.id : undefined,
                name: document.getElementById("fpcc-name").value,
                type: document.getElementById("fpcc-placement").value,
                logicType: document.getElementById("fpcc-logic").value,
                source: document.getElementById("fpcc-source").value,
                nfa: document.getElementById("fpcc-nfa").value.trim(),
                table: document.getElementById("fpcc-table").value.trim(),
                color: document.getElementById("fpcc-color").value,
                colorDark: document.getElementById("fpcc-color-dark").value,
                lineWidth: parseFloat(document.getElementById("fpcc-line-width").value),
                symbol: document.getElementById("fpcc-symbol").value,
                variables,
            });
        };

        const renderCustomConstraintManager = function() {
            ensureCustomManagerOverlay();
            selectedCustomDefinitionIndex = Math.max(0, Math.min(selectedCustomDefinitionIndex, customConstraintDefinitions.length - 1));
            const definition = customConstraintDefinitions[selectedCustomDefinitionIndex] || normalizeCustomDefinition({
                name: "Custom Constraint",
                source: fallbackCustomConstraintSource,
            });
            const { info, instances } = customInstancesForDefinition(definition);
            const listHtml = customConstraintDefinitions.map((def, index) => `
                <button class="fpcc-list-item ${index === selectedCustomDefinitionIndex ? "fpcc-selected" : ""}" data-fpcc-select="${index}">
                    ${escapeHtml(def.name)}
                    <br><small>${escapeHtml(def.type)} / ${escapeHtml(def.logicType)}</small>
                </button>
            `).join("");
            const instanceHtml = info ? instances.map((instance, index) => {
                const args = customArgsForInstance(definition, instance);
                const variableInputs = (definition.variables || []).map(variable => `
                    <div class="fpcc-row">
                        <label>${escapeHtml(variable.label)}</label>
                        <input data-fpcc-instance="${index}" data-fpcc-variable="${escapeHtml(variable.name)}" value="${escapeHtml(args[variable.name])}">
                    </div>
                `).join("");
                return `
                    <div class="fpcc-instance">
                        <div class="fpcc-instance-title">Instance ${index + 1}: ${escapeHtml(describeCustomInstance(info, instance))}</div>
                        ${variableInputs || "<div class=\"fpcc-help\" style=\"margin-left:0\">No variables configured for this definition.</div>"}
                    </div>
                `;
            }).join("") : "";

            customConstraintManagerOverlay.className = boolSettings["Dark Mode"] ? "fpcc-dark" : "";
            customConstraintManagerOverlay.innerHTML = `
                <div id="fpcc-panel">
                    <div class="fpcc-list">
                        <div class="fpcc-header">
                            <div class="fpcc-title">Custom</div>
                        </div>
                        ${listHtml || "<div class=\"fpcc-help\" style=\"margin-left:0\">No custom constraints yet.</div>"}
                        <div class="fpcc-actions">
                            <button id="fpcc-add">New</button>
                            <button id="fpcc-import">Import</button>
                            <button id="fpcc-export">Export</button>
                        </div>
                        <textarea id="fpcc-json-output" class="fpcc-json-output" readonly placeholder="Exported JSON appears here."></textarea>
                    </div>
                    <div class="fpcc-main">
                        <div class="fpcc-header">
                            <div class="fpcc-title">Custom Constraint Manager</div>
                            <button class="fpcc-close" id="fpcc-close">Close</button>
                        </div>
                        <div class="fpcc-row">
                            <label>Name</label>
                            <input id="fpcc-name" value="${escapeHtml(definition.name)}">
                        </div>
                        <div class="fpcc-row">
                            <label>Placement</label>
                            <select id="fpcc-placement">
                                <option value="line" ${definition.type === "line" ? "selected" : ""}>Line</option>
                                <option value="cell" ${definition.type === "cell" ? "selected" : ""}>Cell marker</option>
                                <option value="cage" ${definition.type === "cage" ? "selected" : ""}>Region/cage</option>
                                <option value="outside" ${definition.type === "outside" ? "selected" : ""}>Outside clue</option>
                            </select>
                        </div>
                        <div class="fpcc-row">
                            <label>Logic</label>
                            <select id="fpcc-logic">
                                <option value="nfa" ${definition.logicType === "nfa" ? "selected" : ""}>JS NFA state machine</option>
                                <option value="binary" ${definition.logicType === "binary" ? "selected" : ""}>JS binary lookup</option>
                                <option value="precompiled-nfa" ${definition.logicType === "precompiled-nfa" ? "selected" : ""}>Precompiled NFA</option>
                                <option value="precompiled-binary" ${definition.logicType === "precompiled-binary" ? "selected" : ""}>Precompiled binary lookup</option>
                            </select>
                        </div>
                        <div class="fpcc-row">
                            <label>Color</label>
                            <input id="fpcc-color" value="${escapeHtml(definition.color)}">
                        </div>
                        <div class="fpcc-row">
                            <label>Dark color</label>
                            <input id="fpcc-color-dark" value="${escapeHtml(definition.colorDark)}">
                        </div>
                        <div class="fpcc-row">
                            <label>Line width</label>
                            <input id="fpcc-line-width" type="number" step="0.01" min="0.03" max="1" value="${escapeHtml(definition.lineWidth)}">
                        </div>
                        <div class="fpcc-row">
                            <label>Symbol</label>
                            <input id="fpcc-symbol" value="${escapeHtml(definition.symbol)}">
                        </div>
                        <div class="fpcc-row">
                            <label>Variables JSON</label>
                            <textarea id="fpcc-variables">${escapeHtml(customVariablesJson(definition))}</textarea>
                        </div>
                        <div class="fpcc-help">Variables are available to source as params.name. The first variable is typable for outside clues.</div>
                        <div class="fpcc-row fpcc-source">
                            <label>Source</label>
                            <textarea id="fpcc-source" spellcheck="false">${escapeHtml(definition.source)}</textarea>
                        </div>
                        <div class="fpcc-row">
                            <label>Precompiled NFA</label>
                            <textarea id="fpcc-nfa" spellcheck="false">${escapeHtml(definition.nfa)}</textarea>
                        </div>
                        <div class="fpcc-row">
                            <label>Precompiled table</label>
                            <textarea id="fpcc-table" spellcheck="false">${escapeHtml(definition.table)}</textarea>
                        </div>
                        <div class="fpcc-actions">
                            <button id="fpcc-save">Save</button>
                            <button id="fpcc-validate">Validate compile</button>
                            <button id="fpcc-delete">Delete</button>
                        </div>
                        <div class="fpcc-title" style="font-size:16px;margin-top:14px;">Placed Instances</div>
                        ${instanceHtml || "<div class=\"fpcc-help\" style=\"margin-left:0\">No placed instances for this definition.</div>"}
                    </div>
                </div>
            `;

            customConstraintManagerOverlay.querySelectorAll("[data-fpcc-select]").forEach(button => {
                button.addEventListener("click", () => {
                    selectedCustomDefinitionIndex = parseInt(button.getAttribute("data-fpcc-select"), 10);
                    renderCustomConstraintManager();
                });
            });
            customConstraintManagerOverlay.querySelector("#fpcc-close").addEventListener("click", closeCustomConstraintManager);
            customConstraintManagerOverlay.querySelector("#fpcc-add").addEventListener("click", () => {
                customConstraintDefinitions.push(normalizeCustomDefinition({
                    name: `Custom Constraint ${customConstraintDefinitions.length + 1}`,
                    source: fallbackCustomConstraintSource,
                }));
                selectedCustomDefinitionIndex = customConstraintDefinitions.length - 1;
                saveCustomConstraintDefinitions();
                refreshCustomConstraintRegistration();
                renderCustomConstraintManager();
            });
            customConstraintManagerOverlay.querySelector("#fpcc-save").addEventListener("click", () => {
                try {
                    const oldDefinition = customConstraintDefinitions[selectedCustomDefinitionIndex];
                    const newDefinition = readCustomDefinitionFromForm(oldDefinition);
                    if (oldDefinition && cID(oldDefinition.name) !== cID(newDefinition.name) && constraints[cID(oldDefinition.name)] && !constraints[cID(newDefinition.name)]) {
                        constraints[cID(newDefinition.name)] = constraints[cID(oldDefinition.name)];
                    }
                    customConstraintDefinitions[selectedCustomDefinitionIndex] = newDefinition;
                    saveCustomConstraintDefinitions();
                    refreshCustomConstraintRegistration();
                    renderCustomConstraintManager();
                } catch (e) {
                    alert(e.message);
                }
            });
            customConstraintManagerOverlay.querySelector("#fpcc-delete").addEventListener("click", () => {
                const oldDefinition = customConstraintDefinitions[selectedCustomDefinitionIndex];
                if (!oldDefinition || !confirm(`Delete "${oldDefinition.name}"? Placed instances for this custom tool will be removed from the active tool list.`)) return;
                delete constraints[cID(oldDefinition.name)];
                customConstraintDefinitions.splice(selectedCustomDefinitionIndex, 1);
                selectedCustomDefinitionIndex = Math.max(0, selectedCustomDefinitionIndex - 1);
                saveCustomConstraintDefinitions();
                refreshCustomConstraintRegistration();
                renderCustomConstraintManager();
            });
            customConstraintManagerOverlay.querySelector("#fpcc-validate").addEventListener("click", () => {
                try {
                    const candidateDefinition = readCustomDefinitionFromForm(definition);
                    const compiled = compileCustomConstraint(candidateDefinition, customDefaultArgs(candidateDefinition), size);
                    alert(compiled.type === "binary" ? "Compiled binary lookup table." : `Compiled NFA (${compiled.nfa.length} chars).`);
                } catch (e) {
                    alert(e.message);
                }
            });
            customConstraintManagerOverlay.querySelector("#fpcc-export").addEventListener("click", async () => {
                const text = JSON.stringify(customConstraintDefinitions, null, 2);
                customConstraintManagerOverlay.querySelector("#fpcc-json-output").value = text;
                try {
                    await navigator.clipboard.writeText(text);
                } catch (_) {
                    // The textarea is the fallback for browsers that block clipboard writes.
                }
            });
            customConstraintManagerOverlay.querySelector("#fpcc-import").addEventListener("click", () => {
                const pasted = prompt("Paste custom constraint JSON:");
                if (!pasted) return;
                try {
                    const parsed = JSON.parse(pasted);
                    const importedRaw = Array.isArray(parsed)
                        ? parsed
                        : parsed.definitions
                            ? parsed.definitions
                            : parsed.nfa || parsed.table || parsed.source || parsed.name
                                ? [parsed]
                                : [];
                    const imported = importedRaw.map(normalizeCustomDefinition);
                    const byId = new Map(customConstraintDefinitions.map((def, index) => [def.id, index]));
                    for (const definition of imported) {
                        if (byId.has(definition.id)) {
                            customConstraintDefinitions[byId.get(definition.id)] = definition;
                        } else {
                            customConstraintDefinitions.push(definition);
                        }
                    }
                    saveCustomConstraintDefinitions();
                    refreshCustomConstraintRegistration();
                    renderCustomConstraintManager();
                } catch (e) {
                    alert(e.message);
                }
            });
            customConstraintManagerOverlay.querySelectorAll("[data-fpcc-instance]").forEach(input => {
                input.addEventListener("input", () => {
                    const instanceIndex = parseInt(input.getAttribute("data-fpcc-instance"), 10);
                    const variableName = input.getAttribute("data-fpcc-variable");
                    if (!instances[instanceIndex].customArgs) {
                        instances[instanceIndex].customArgs = customDefaultArgs(definition);
                    }
                    instances[instanceIndex].customArgs[variableName] = input.value;
                    generateCandidates();
                });
            });
        };

        // ===== Constraint Toolbox Dialog =====
        let toolboxOverlay = null;
        let toolboxPrevDisableInputs = null;

        const closeToolbox = function() {
            if (toolboxOverlay) toolboxOverlay.style.display = "none";
            if (toolboxPrevDisableInputs !== null) {
                disableInputs = toolboxPrevDisableInputs;
                toolboxPrevDisableInputs = null;
            }
        };

        const getConstraintCategory = function(name) {
            const info = newConstraintInfo.find(i => i.name === name);
            if (info) return info.type;
            if (lineConstraints   && lineConstraints.includes(name))    return "line";
            if (perCellConstraints && perCellConstraints.includes(name)) return "cell";
            if (regionConstraints  && regionConstraints.includes(name))  return "cage";
            if (outsideConstraints && outsideConstraints.includes(name)) return "outside";
            return "other";
        };

        const categoryLabel = { line: "Line", cage: "Region / Cage", cell: "Cell Marker", outside: "Outside Clue", other: "Other" };
        const categoryOrder = ["line", "cage", "cell", "outside", "other"];

        const getConstraintDescription = function(name) {
            const d = typeof descriptions !== "undefined" && descriptions[name];
            if (Array.isArray(d)) return d[0] || "";
            if (typeof d === "string") return d;
            const info = newConstraintInfo.find(i => i.name === name);
            return (info && info.tooltip && info.tooltip[0]) || "";
        };

        const getConstraintColor = function(name) {
            const info = newConstraintInfo.find(i => i.name === name);
            if (!info) return null;
            return (boolSettings["Dark Mode"] ? info.colorDark : info.color) || info.color || null;
        };

        const getAllToolboxItems = function() {
            const items = [];
            const seen = new Set();
            const add = function(name) {
                if (seen.has(name)) return;
                seen.add(name);
                const info = newConstraintInfo.find(i => i.name === name);
                items.push({
                    name,
                    isCustom: !!(info && info.isCustomConstraint),
                    category: getConstraintCategory(name),
                    description: getConstraintDescription(name),
                    color: getConstraintColor(name),
                });
            };
            if (builtinToolConstraints) for (const n of builtinToolConstraints) add(n);
            for (const info of newConstraintInfo) add(info.name);
            return items;
        };

        const ensureToolboxOverlay = function() {
            if (toolboxOverlay) return;
            const style = document.createElement("style");
            style.textContent = `
                #fptb-overlay {
                    position: fixed; inset: 0; z-index: 100001;
                    display: none; align-items: center; justify-content: center;
                    background: rgba(0,0,0,0.5);
                    font-family: Arial, sans-serif; outline: none;
                }
                #fptb-panel {
                    width: min(920px, calc(100vw - 40px));
                    height: min(720px, calc(100vh - 40px));
                    background: #f0f0f0; color: #111;
                    border: 1px solid #333; border-radius: 4px;
                    box-shadow: 0 16px 48px rgba(0,0,0,0.4);
                    display: flex; flex-direction: column; overflow: hidden;
                }
                #fptb-overlay.fptb-dark #fptb-panel {
                    background: #2a2a2a; color: #eee; border-color: #666;
                }
                #fptb-header {
                    display: flex; align-items: center; gap: 8px;
                    padding: 12px 16px; background: #ddd;
                    border-bottom: 1px solid #bbb; flex-shrink: 0;
                }
                #fptb-overlay.fptb-dark #fptb-header {
                    background: #1e1e1e; border-color: #555;
                }
                #fptb-title { font-size: 17px; font-weight: 700; flex: 1; }
                #fptb-toolbar {
                    display: flex; align-items: center; gap: 8px;
                    padding: 10px 16px; border-bottom: 1px solid #ccc;
                    flex-wrap: wrap; flex-shrink: 0;
                }
                #fptb-overlay.fptb-dark #fptb-toolbar { border-color: #444; }
                #fptb-search {
                    flex: 1; min-width: 140px; max-width: 260px;
                    padding: 6px 9px; font: 13px Arial, sans-serif;
                    border: 1px solid #999; border-radius: 3px;
                    background: #fff; color: #111;
                }
                #fptb-overlay.fptb-dark #fptb-search {
                    background: #111; color: #eee; border-color: #555;
                }
                .fptb-select {
                    padding: 6px 8px; font: 13px Arial, sans-serif;
                    border: 1px solid #999; border-radius: 3px;
                    background: #fff; color: #111; cursor: pointer;
                }
                #fptb-overlay.fptb-dark .fptb-select {
                    background: #111; color: #eee; border-color: #555;
                }
                .fptb-btn {
                    padding: 6px 12px; font: 13px Arial, sans-serif;
                    border: 1px solid #777; border-radius: 3px;
                    background: #fff; color: #111; cursor: pointer;
                    white-space: nowrap;
                }
                #fptb-overlay.fptb-dark .fptb-btn {
                    background: #333; color: #eee; border-color: #777;
                }
                #fptb-btn-custom { margin-left: auto; }
                #fptb-body { flex: 1; overflow-y: auto; padding: 10px 16px; }
                .fptb-group-label {
                    font-size: 11px; font-weight: 700;
                    text-transform: uppercase; letter-spacing: 0.07em;
                    opacity: 0.5; margin: 14px 0 5px;
                    padding-bottom: 3px; border-bottom: 1px solid #ccc;
                }
                .fptb-group-label:first-child { margin-top: 2px; }
                #fptb-overlay.fptb-dark .fptb-group-label { border-color: #444; }
                .fptb-item {
                    display: flex; align-items: center; gap: 10px;
                    padding: 6px 8px; border-radius: 3px; margin-bottom: 2px;
                }
                .fptb-item:hover { background: rgba(0,0,0,0.06); }
                #fptb-overlay.fptb-dark .fptb-item:hover { background: rgba(255,255,255,0.07); }
                .fptb-swatch {
                    width: 13px; height: 13px; border-radius: 2px;
                    border: 1px solid rgba(0,0,0,0.25); flex-shrink: 0;
                }
                .fptb-name { font-weight: 600; font-size: 13px; min-width: 148px; }
                .fptb-badge {
                    font-size: 10px; padding: 2px 6px; border-radius: 10px;
                    background: rgba(0,0,0,0.1); opacity: 0.7; white-space: nowrap;
                }
                #fptb-overlay.fptb-dark .fptb-badge { background: rgba(255,255,255,0.13); }
                .fptb-badge-custom { background: rgba(80,80,220,0.18) !important; opacity: 1 !important; }
                .fptb-desc {
                    font-size: 12px; opacity: 0.6; flex: 1;
                    overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
                }
                .fptb-toggle {
                    padding: 4px 11px; font: 12px Arial, sans-serif;
                    border-radius: 3px; cursor: pointer;
                    white-space: nowrap; flex-shrink: 0; border: 1px solid;
                }
                .fptb-toggle-on  { background: #2d6a2d; border-color: #1a4a1a; color: #fff; }
                .fptb-toggle-off { background: #fff; border-color: #999; color: #555; }
                #fptb-overlay.fptb-dark .fptb-toggle-off {
                    background: #333; border-color: #666; color: #aaa;
                }
                #fptb-footer {
                    padding: 8px 16px; border-top: 1px solid #ccc;
                    font-size: 12px; opacity: 0.6; flex-shrink: 0;
                }
                #fptb-overlay.fptb-dark #fptb-footer { border-color: #444; }
                #fptb-empty { text-align: center; padding: 36px; opacity: 0.5; font-size: 14px; }
            `;
            document.head.appendChild(style);
            toolboxOverlay = document.createElement("div");
            toolboxOverlay.id = "fptb-overlay";
            toolboxOverlay.tabIndex = -1;
            document.body.appendChild(toolboxOverlay);
            const blockEvent = function(e) {
                e.stopPropagation();
                if (e.type === "wheel" || e.type === "contextmenu") e.preventDefault();
                if (e.type === "keydown" && e.key === "Escape") { e.preventDefault(); closeToolbox(); }
            };
            ["mousedown","mouseup","mousemove","click","dblclick","contextmenu","wheel",
             "touchstart","touchmove","touchend","keydown","keyup","keypress"]
                .forEach(ev => toolboxOverlay.addEventListener(ev, blockEvent));
        };

        const renderToolbox = function() {
            ensureToolboxOverlay();

            const dark = boolSettings["Dark Mode"];
            toolboxOverlay.className = dark ? "fptb-dark" : "";

            const searchVal  = toolboxOverlay._search  || "";
            const filterType = toolboxOverlay._type    || "all";
            const filterShow = toolboxOverlay._show    || "all";

            let items = getAllToolboxItems();
            const total = items.length;

            if (filterType !== "all") items = items.filter(it => it.category === filterType);
            if (filterShow === "on")  items = items.filter(it =>  puzzleActiveConstraints.has(it.name));
            if (filterShow === "off") items = items.filter(it => !puzzleActiveConstraints.has(it.name));
            if (searchVal) {
                const q = searchVal.toLowerCase();
                items = items.filter(it =>
                    it.name.toLowerCase().includes(q) ||
                    it.description.toLowerCase().includes(q) ||
                    (categoryLabel[it.category] || "").toLowerCase().includes(q)
                );
            }

            // Sort: in-puzzle first, then by MRU (most recently used), then alphabetical
            items.sort((a, b) => {
                const aOn = puzzleActiveConstraints.has(a.name);
                const bOn = puzzleActiveConstraints.has(b.name);
                if (aOn !== bOn) return aOn ? -1 : 1;
                if (a.category !== b.category) {
                    return categoryOrder.indexOf(a.category) - categoryOrder.indexOf(b.category);
                }
                const aLast = (constraintUsage[a.name] || {}).lastUsed || 0;
                const bLast = (constraintUsage[b.name] || {}).lastUsed || 0;
                if (aLast !== bLast) return bLast - aLast; // more recent first
                return a.name.localeCompare(b.name);
            });

            // Build grouped sections: "In This Puzzle" at top, then by category
            const inPuzzle = items.filter(it => puzzleActiveConstraints.has(it.name));
            const notInPuzzle = items.filter(it => !puzzleActiveConstraints.has(it.name));

            const renderItem = function(it) {
                const on = puzzleActiveConstraints.has(it.name);
                const sw = it.color ? `background:${escapeHtml(it.color)}` : "background:#888";
                const customBadge = it.isCustom
                    ? ` <span class="fptb-badge fptb-badge-custom">custom</span>` : "";
                const usageInfo = constraintUsage[it.name];
                const usageBadge = !on && usageInfo && usageInfo.count > 0
                    ? ` <span class="fptb-badge" title="Used ${usageInfo.count}x">★${usageInfo.count}</span>` : "";
                return `
                    <div class="fptb-item">
                        <div class="fptb-swatch" style="${sw}"></div>
                        <div class="fptb-name">${escapeHtml(it.name)}${customBadge}</div>
                        <span class="fptb-badge">${escapeHtml(categoryLabel[it.category] || it.category)}</span>
                        <div class="fptb-desc">${escapeHtml(it.description)}${usageBadge}</div>
                        <button class="fptb-toggle ${on ? "fptb-toggle-on" : "fptb-toggle-off"}"
                                data-fptb="${escapeHtml(it.name)}">${on ? "In Puzzle ✓" : "+ Add to Puzzle"}</button>
                    </div>`;
            };

            let bodyHtml = "";
            if (inPuzzle.length > 0) {
                bodyHtml += `<div class="fptb-group-label">In This Puzzle</div>`;
                bodyHtml += inPuzzle.map(renderItem).join("");
            }
            if (notInPuzzle.length > 0) {
                // Group remaining by category
                const grouped = {};
                for (const it of notInPuzzle) {
                    (grouped[it.category] || (grouped[it.category] = [])).push(it);
                }
                for (const cat of categoryOrder) {
                    const group = grouped[cat];
                    if (!group || !group.length) continue;
                    bodyHtml += `<div class="fptb-group-label">${escapeHtml(categoryLabel[cat] || cat)}</div>`;
                    bodyHtml += group.map(renderItem).join("");
                }
            }
            if (!bodyHtml) bodyHtml = `<div id="fptb-empty">No constraints match your search.</div>`;

            const activeCount = puzzleActiveConstraints.size;
            toolboxOverlay.innerHTML = `
                <div id="fptb-panel">
                    <div id="fptb-header">
                        <div id="fptb-title">Constraint Toolbox</div>
                        <button class="fptb-btn" id="fptb-close">Close</button>
                    </div>
                    <div id="fptb-toolbar">
                        <input id="fptb-search" type="text" placeholder="Search constraints…"
                               value="${escapeHtml(searchVal)}">
                        <select id="fptb-type" class="fptb-select">
                            <option value="all"     ${filterType==="all"     ? "selected":""}>All Types</option>
                            <option value="line"    ${filterType==="line"    ? "selected":""}>Line</option>
                            <option value="cage"    ${filterType==="cage"    ? "selected":""}>Region / Cage</option>
                            <option value="cell"    ${filterType==="cell"    ? "selected":""}>Cell Marker</option>
                            <option value="outside" ${filterType==="outside" ? "selected":""}>Outside Clue</option>
                            <option value="other"   ${filterType==="other"   ? "selected":""}>Other</option>
                        </select>
                        <select id="fptb-show" class="fptb-select">
                            <option value="all" ${filterShow==="all" ? "selected":""}>All Constraints</option>
                            <option value="on"  ${filterShow==="on"  ? "selected":""}>In This Puzzle</option>
                            <option value="off" ${filterShow==="off" ? "selected":""}>Not in Puzzle</option>
                        </select>
                        <button class="fptb-btn" id="fptb-btn-custom">+ Custom Constraint…</button>
                    </div>
                    <div id="fptb-body">${bodyHtml}</div>
                    <div id="fptb-footer">${activeCount} of ${total} constraint${total !== 1 ? "s" : ""} active in this puzzle</div>
                </div>`;

            toolboxOverlay.querySelector("#fptb-close").addEventListener("click", closeToolbox);
            toolboxOverlay.querySelector("#fptb-btn-custom").addEventListener("click", () => {
                closeToolbox();
                openCustomConstraintManager();
            });
            const searchEl = toolboxOverlay.querySelector("#fptb-search");
            searchEl.addEventListener("input", () => { toolboxOverlay._search = searchEl.value; renderToolbox(); });
            toolboxOverlay.querySelector("#fptb-type").addEventListener("change", e => { toolboxOverlay._type = e.target.value; renderToolbox(); });
            toolboxOverlay.querySelector("#fptb-show").addEventListener("change", e => { toolboxOverlay._show = e.target.value; renderToolbox(); });
            toolboxOverlay.querySelectorAll(".fptb-toggle").forEach(btn => {
                btn.addEventListener("click", () => {
                    const name = btn.getAttribute("data-fptb");
                    if (puzzleActiveConstraints.has(name)) {
                        puzzleActiveConstraints.delete(name);
                    } else {
                        puzzleActiveConstraints.add(name);
                        recordConstraintUsed(name);
                    }
                    refreshCustomConstraintRegistration();
                    renderToolbox();
                });
            });
            searchEl.focus();
        };

        const openToolbox = function() {
            ensureToolboxOverlay();
            renderToolbox();
            if (toolboxPrevDisableInputs === null) toolboxPrevDisableInputs = disableInputs;
            disableInputs = true;
            toolboxOverlay.style.display = "flex";
            toolboxOverlay.focus();
        };

        const openCustomConstraintManager = function() {
            renderCustomConstraintManager();
            if (customManagerPreviousDisableInputs === null) {
                customManagerPreviousDisableInputs = disableInputs;
            }
            disableInputs = true;
            customConstraintManagerOverlay.style.display = "flex";
            customConstraintManagerOverlay.focus();
        };

        // Puzzle title
        // Unfortuantely, there's no way to shim this so it's duplicated in full.
        getPuzzleTitle = function () {
            var title = "";

            ctx.font = titleLSize + "px Arial";

            if (customTitle.length) {
                title = customTitle;
            } else {
                if (size !== 9) title += size + "x" + size + " ";
                if (getCells().some((a) => a.region !== Math.floor(a.i / regionH) * regionH + Math.floor(a.j / regionW))) title += "Irregular ";
                if (constraints[cID("Extra Region")].length) title += "Extra-Region ";
                if (constraints[cID("Odd")].length && !constraints[cID("Even")].length) title += "Odd ";
                if (!constraints[cID("Odd")].length && constraints[cID("Even")].length) title += "Even ";
                if (constraints[cID("Odd")].length && constraints[cID("Even")].length) title += "Odd-Even ";
                if (constraints[cID("Diagonal +")] !== constraints[cID("Diagonal -")]) title += "Single-Diagonal ";
                if (
                    constraints[cID("Nonconsecutive")] &&
                    !(constraints[cID("Difference")].length && constraints[cID("Difference")].some((a) => ["", "1"].includes(a.value))) &&
                    !constraints[cID("Ratio")].negative
                )
                    title += "Nonconsecutive ";
                if (
                    constraints[cID("Nonconsecutive")] &&
                    constraints[cID("Difference")].length &&
                    constraints[cID("Difference")].some((a) => ["", "1"].includes(a.value)) &&
                    !constraints[cID("Ratio")].negative
                )
                    title += "Consecutive ";
                if (
                    !constraints[cID("Nonconsecutive")] &&
                    constraints[cID("Difference")].length &&
                    constraints[cID("Difference")].every((a) => ["", "1"].includes(a.value))
                )
                    title += "Consecutive-Pairs ";
                if (constraints[cID("Antiknight")]) title += "Antiknight ";
                if (constraints[cID("Antiking")]) title += "Antiking ";
                if (constraints[cID("Disjoint Groups")]) title += "Disjoint-Group ";
                if (constraints[cID("XV")].length || constraints[cID("XV")].negative)
                    title += "XV " + (constraints[cID("XV")].negative ? "(-) " : "");
                if (constraints[cID("Little Killer Sum")].length) title += "Little Killer ";
                if (constraints[cID("Sandwich Sum")].length) title += "Sandwich ";
                if (constraints[cID("Thermometer")].length) title += "Thermo ";
                if (constraints[cID("Palindrome")].length) title += "Palindrome ";
                if (
                    constraints[cID("Difference")].length &&
                    constraints[cID("Difference")].some((a) => !["", "1"].includes(a.value)) &&
                    !(constraints[cID("Nonconsecutive")] && constraints[cID("Ratio")].negative)
                )
                    title += "Difference ";
                if (
                    (constraints[cID("Ratio")].length || constraints[cID("Ratio")].negative) &&
                    !(constraints[cID("Nonconsecutive")] && constraints[cID("Ratio")].negative)
                )
                    title += "Ratio " + (constraints[cID("Ratio")].negative ? "(-) " : "");
                if (constraints[cID("Nonconsecutive")] && constraints[cID("Ratio")].negative) title += "Kropki ";
                if (constraints[cID("Killer Cage")].length) title += "Killer ";
                if (constraints[cID("Clone")].length) title += "Clone ";
                if (constraints[cID("Arrow")].length) title += "Arrow ";
                if (constraints[cID("Between Line")].length) title += "Between ";
                if (constraints[cID("Quadruple")].length) title += "Quadruples ";
                if (constraints[cID("Minimum")].length || constraints[cID("Maximum")].length) title += "Extremes ";

                for (let info of newConstraintInfo) {
                    if (constraints[cID(info.name)] && constraints[cID(info.name)].length > 0) {
                        title += `${info.name} `;
                    }
                }

                title += "Sudoku";

                if (constraints[cID("Diagonal +")] && constraints[cID("Diagonal -")]) title += " X";

                if (title === "Sudoku") title = "Classic Sudoku";

                if (ctx.measureText(title).width > canvas.width - 711) title = "Extreme Variant Sudoku";
            }

            buttons[buttons.findIndex((a) => a.id === "EditInfo")].x = canvas.width / 2 + ctx.measureText(title).width / 2 + 40;

            return title;
        };

        // Multi-column constraint sidebar
        let toolboxPopupButton = null;
        const addToolboxPopupButton = function() {
            const constraintsSidebar = sidebars.find(sb => sb.title === "Constraints");
            if (!constraintsSidebar) return;
            if (constraintsSidebar.buttons.some(b => b.id === "ToolboxButton")) return;
            if (!toolboxPopupButton) {
                toolboxPopupButton = new button(
                    0, 0, buttonW, buttonSH,
                    ["Constraint Tools"],
                    "ToolboxButton",
                    "Toolbox"
                );
                toolboxPopupButton.click = function() {
                    if (!this.hovering()) return;
                    closePopups();
                    openToolbox();
                    return true;
                };
            }
            constraintsSidebar.buttons.push(toolboxPopupButton);
        };

        let constraintSidebarWidth = 0;
        const prevCreateSidebarConstraints = createSidebarConstraints;
        createSidebarConstraints = function () {
            prevCreateSidebarConstraints();
            addToolboxPopupButton();

            {
                const x = gridX - (sidebarDist + sidebarW / 2);
                const baseX = x + sidebarW;
                const baseY = gridY + buttonGap;
                const columnWidth = buttonMargin + buttonW + buttonGap + buttonSH + buttonGap + buttonSH + buttonMargin;
                let constraintButtons = sidebars[0].buttons;
                let currentX = baseX;
                let currentY = gridY + buttonGap;
                for (let i = 0; i < constraintButtons.length; i++) {
                    let button = constraintButtons[i];
                    if (button.modes.indexOf("Constraint Tools") > -1 && button.title !== "-") {
                    button.x = currentX;
                    button.y = currentY;
                    button.w = buttonW;
                    button.h = buttonSH;

                        if (i + 1 < constraintButtons.length) {
                            let bm = constraintButtons[i + 1];
                            if (bm.title === "-") {
                                bm.x = currentX + buttonW / 2 + buttonGap + buttonSH / 2;
                                bm.y = currentY;
                    }
                        }

                        if (i < constraintButtons.length - 1) {
                     currentY += buttonSH + buttonGap;
                     if (currentY + buttonSH + buttonGap > gridY + gridSL) {
                         currentY = baseY;
                         currentX += columnWidth;
                     }
                }
                    }
                }

                constraintSidebarWidth = currentX + columnWidth - baseX;
            }
        };

        const origDrawPopups = window.drawPopups;
        window.drawPopups = function (overlapSidebars) {
            if (!overlapSidebars && popup === "Constraint Tools") {
                ctx.lineWidth = lineWW;
                ctx.fillStyle = boolSettings["Dark Mode"] ? "#404040" : "#D0D0D0";
                ctx.strokeStyle = boolSettings["Dark Mode"] ? "#202020" : "#808080";
                ctx.fillRect(gridX - sidebarDist, gridY + gridSL, constraintSidebarWidth, -gridSL);
                ctx.strokeRect(gridX - sidebarDist, gridY + gridSL, constraintSidebarWidth, -gridSL);
            } else {
                origDrawPopups(overlapSidebars);
            }
        };

        const prevonmousemove = document.onmousemove;
        document.onmousemove = function (e) {
            if (!testPaused() && !disableInputs && !holding && sidebars.length && popup === "Constraint Tools") {
                updateCursorPosition(e);

                const hoveredButton =
                    sidebars[sidebars.findIndex((a) => a.title === "Constraints")].buttons[
                        sidebars[sidebars.findIndex((a) => a.title === "Constraints")].buttons.findIndex((a) => a.id === "ConstraintTools")
                    ];
                     if (
                    (mouseX < hoveredButton.x - hoveredButton.w / 2 - buttonMargin ||
                        mouseX > hoveredButton.x + hoveredButton.w / 2 + buttonMargin ||
                        mouseY < hoveredButton.y - buttonMargin ||
                        mouseY > hoveredButton.y + buttonSH + buttonMargin) &&
                    (mouseX < gridX - sidebarDist ||
                        mouseX > gridX - sidebarDist + constraintSidebarWidth ||
                        mouseY < gridY ||
                        mouseY > gridY + gridSL)
                    ) {
                        closePopups();
                    }
            } else {
                prevonmousemove(e);
            }
        };

        if (window.boolConstraints) {
            let prevButtons = buttons.splice(0, buttons.length);
            window.onload();
            buttons.splice(0, buttons.length);
            for (let i = 0; i < prevButtons.length; i++) {
                buttons.push(prevButtons[i]);
            }
        }
    } // end of doShim

    let intervalId = setInterval(() => {
        if (typeof grid === 'undefined' ||
            typeof exportPuzzle === 'undefined' ||
            typeof importPuzzle === 'undefined' ||
            typeof drawConstraints === 'undefined' ||
            typeof candidatePossibleInCell === 'undefined' ||
            typeof categorizeTools === 'undefined' ||
            typeof drawPopups === 'undefined') {
            return;
        }

        clearInterval(intervalId);
        doShim();
    }, 16);
})();
