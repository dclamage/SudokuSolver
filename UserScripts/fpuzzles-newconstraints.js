// ==UserScript==
// @name         Fpuzzles-NewConstraints
// @namespace    http://tampermonkey.net/
// @version      1.12
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
            lineWidth: 0.4,
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
            name: "Whispers",
            type: "line",
            color: "#67F067",
            colorDark: "#357D35",
            lineWidth: 0.3,
            tooltip: [
                "Adjacent numbers on a whispers line must have a difference of 5 or greater.",
                "[For non-9x9 grid sizes, this adjust to be (size / 2) rounded up.]",
                "",
                "Click and drag to draw a whispers line.",
                "Click on a whispers line to remove it.",
                "Shift click and drag to draw overlapping whispers lines.",
            ],
        },
        {
            name: "Entropic Line",
            type: "line",
            color: "#FFCCAA",
            colorDark: "#FFCCAA",
            lineWidth: 0.25,
            tooltip: [
                "Any set of three sequential cells along an entropic line must contain",
                "a low digit (1-3), a middle digit (4-6), and a high higit (7-9).",
                "Digits my repeat on a line, if allowed by other rules.",
                "An entropic line of length two may not contain two digits from the same rank.",
                "",
                "Click and drag to draw an entropic line.",
                "Click on an entropic line to remove it.",
                "Shift click and drag to draw overlapping entropic lines.",
            ],
        },
        {
            name: "Region Sum Line",
            type: "line",
            color: "#2ECBFF",
            colorDark: "#1E86A8",
            lineWidth: 0.25,
            tooltip: [
                "Digits have an equal sum within each box the line passes through.",
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
            lineWidth: 0.4,
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

    const doShim = function() {
        ("use strict");

        // Additional import/export data
        const origExportPuzzle = exportPuzzle;
        exportPuzzle = function (includeCandidates) {
            const compressed = origExportPuzzle(includeCandidates);
            const puzzle = JSON.parse(
                compressor.decompressFromBase64(compressed)
            );

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
                                outlineC: constraintInfo.color,
                                width: constraintInfo.lineWidth,
                                fromConstraint: constraintInfo.name,
                            });
                        }
                        if (constraintInfo.endpoints === "circle") {
                            if (!puzzle.circle) {
                                puzzle.circle = [];
                            }

                            for (let instance of puzzleEntry) {
                                puzzle.circle.push({
                                    cells: [instance.lines[0][0]],
                                    baseC: "#EAEAEA",
                                    outlineC: constraintInfo.color,
                                    width: 0.85,
                                    height: 0.85,
                                    fromConstraint: constraintInfo.name,
                                });
                                
                                const lastIndex = instance.lines[0].length - 1;
                                puzzle.circle.push({
                                    cells: [instance.lines[0][lastIndex]],
                                    baseC: "#EAEAEA",
                                    outlineC: constraintInfo.color,
                                    width: 0.85,
                                    height: 0.85,
                                    fromConstraint: constraintInfo.name,
                                });
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
            return compressor.compressToBase64(JSON.stringify(puzzle));
        };

        const origImportPuzzle = importPuzzle;
        importPuzzle = function (string, clearHistory) {
            // Remove any generated cosmetics
            const puzzle = JSON.parse(compressor.decompressFromBase64(string));
            let constraintNames = newConstraintInfo.map((c) => c.name);
            if (puzzle.line) {
                let filteredLines = [];
                for (let line of puzzle.line) {
                    // Upgrade from old boolean
                    if (line.isNewConstraint) {
                        line.fromConstraint =
                            line.outlineC === "#C060C0"
                                ? "Renban"
                                : line.outlineC === "#67F067"
                                ? "Whispers"
                                : "Entropic";
                        delete line.isNewConstraint;
                    }

                    if (
                        !line.fromConstraint ||
                        !constraintNames.includes(line.fromConstraint)
                    ) {
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
                    if (
                        !cage.fromConstraint ||
                        !constraintNames.includes(cage.fromConstraint)
                    ) {
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
                    if (
                        !text.fromConstraint ||
                        !constraintNames.includes(text.fromConstraint)
                    ) {
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
                    if (
                        !circle.fromConstraint ||
                        !constraintNames.includes(circle.fromConstraint)
                    ) {
                        filteredCircle.push(circle);
                    }
                }

                if (filteredCircle.length > 0) {
                    puzzle.circle = filteredCircle;
                } else {
                    delete puzzle.circle;
                }
            }

            string = compressor.compressToBase64(JSON.stringify(puzzle));
            origImportPuzzle(string, clearHistory);
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
                            let cells = constraint
                                .flatMap((inst) => inst.cells)
                                .filter((cell) => cell);
                            drawSolidCage(cells, info.color, info.colorDark);
                        }
                    }
                }
            }
            origDrawConstraints(layer);
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
                                    minValue =
                                        minValue === -1 ||
                                        minValue > lineCell.value
                                            ? lineCell.value
                                            : minValue;
                                    maxValue =
                                        maxValue === -1 ||
                                        maxValue < lineCell.value
                                            ? lineCell.value
                                            : maxValue;
                                    if (lineCell.value === n) {
                                        numMatchingValue++;
                                        if (numMatchingValue > 1) {
                                            return false;
                                        }
                                    }
                                }
                            }
                            if (minValue !== -1 && maxValue !== -1) {
                                if (
                                    n - minValue > line.length - 1 ||
                                    maxValue - n > line.length - 1
                                ) {
                                    return false;
                                }
                            }
                        }
                    }
                }
            }

            // Whispers
            const constraintsWhispers = constraints[cID("Whispers")];
            if (constraintsWhispers && constraintsWhispers.length > 0) {
                const whispersDiff = Math.ceil(size / 2);
                for (let whispers of constraintsWhispers) {
                    for (let line of whispers.lines) {
                        const index = line.indexOf(cell);
                        if (index > -1) {
                            if (
                                n - whispersDiff <= 0 &&
                                n + whispersDiff > size
                            ) {
                                return false;
                            }

                            if (index > 0) {
                                const prevCell = line[index - 1];
                                if (
                                    prevCell.value &&
                                    Math.abs(prevCell.value - n) < whispersDiff
                                ) {
                                    return false;
                                }
                            }
                            if (index < line.length - 1) {
                                const nextCell = line[index + 1];
                                if (
                                    nextCell.value &&
                                    Math.abs(nextCell.value - n) < whispersDiff
                                ) {
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
                const entropicLineGroups = [
                    [1, 2, 3],
                    [4, 5, 6],
                    [7, 8, 9],
                ];
                for (let entropicLine of constraintsEntropicLine) {
                    for (let line of entropicLine.lines) {
                        const index = line.indexOf(cell);
                        if (index > -1) {
                            let lineGroupIndices = [-1, -1, -1];

                            let nGroup = -1;

                            for (
                                let i = 0;
                                i < entropicLineGroups.length;
                                i++
                            ) {
                                if (entropicLineGroups[i].includes(n)) {
                                    nGroup = i;
                                }
                            }

                            for (let i = 0; i < line.length; i++) {
                                if (line[i].value) {
                                    for (
                                        let j = 0;
                                        j < entropicLineGroups.length;
                                        j++
                                    ) {
                                        if (
                                            entropicLineGroups[j].includes(
                                                line[i].value
                                            )
                                        ) {
                                            if (lineGroupIndices[j] === -1) {
                                                lineGroupIndices[j] = i % 3;
                                            } else if (
                                                lineGroupIndices[j] !==
                                                i % 3
                                            ) {
                                                if (
                                                    index % 3 === i % 3 ||
                                                    lineGroupIndices[j] ===
                                                        index % 3
                                                ) {
                                                    return false;
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            if (lineGroupIndices[nGroup] !== -1) {
                                if (lineGroupIndices[nGroup] !== index % 3) {
                                    return false;
                                } else if (
                                    lineGroupIndices.indexOf(index % 3) !==
                                        -1 &&
                                    lineGroupIndices.indexOf(index % 3) !==
                                        nGroup
                                ) {
                                    return false;
                                }
                            } else if (
                                lineGroupIndices[(nGroup + 1) % 3] !== -1 &&
                                lineGroupIndices[(nGroup + 2) % 3] !== -1
                            ) {
                                if (
                                    lineGroupIndices[(nGroup + 1) % 3] ===
                                        index % 3 ||
                                    lineGroupIndices[(nGroup + 2) % 3] ==
                                        index % 3
                                ) {
                                    return false;
                                }
                            }
                        }
                    }
                }
            }

            // Region Sum Lines
            const constraintsRegionSumLines = constraints[cID('Region Sum Line')];
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

                            if (completedSums.length > 1 && !completedSums.every(sum => sum === completedSums[0])) {
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
                            for (
                                let lineIndex = 1;
                                lineIndex < line.length - 1;
                                lineIndex++
                            ) {
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

            // Row Indexer
            const constraintsRowIndexer = constraints[cID("Row Indexer")];
            if (
                constraintsRowIndexer &&
                constraintsRowIndexer.some(
                    (rowIndexer) => rowIndexer.cells.indexOf(cell) > -1
                )
            ) {
                let targetCell = grid[n - 1][cell.j];
                if (targetCell !== cell && targetCell.value) {
                    if (
                        targetCell.value > 0 &&
                        targetCell.value !== cell.i + 1
                    ) {
                        return false;
                    }
                }
            }

            // Column Indexer
            const constraintsColumnIndexer = constraints[cID("Column Indexer")];
            if (
                constraintsColumnIndexer &&
                constraintsColumnIndexer.some(
                    (columnIndexer) => columnIndexer.cells.indexOf(cell) > -1
                )
            ) {
                let targetCell = grid[cell.i][n - 1];
                if (targetCell !== cell && targetCell.value) {
                    if (
                        targetCell.value > 0 &&
                        targetCell.value !== cell.j + 1
                    ) {
                        return false;
                    }
                }
            }

            // Box Indexer
            const constraintsBoxIndexer = constraints[cID("Box Indexer")];
            if (
                constraintsBoxIndexer &&
                constraintsBoxIndexer.some(
                    (boxIndexer) => boxIndexer.cells.indexOf(cell) > -1
                )
            ) {
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
                            if (
                                targetCell.value > 0 &&
                                targetCell.value !== cellRegionIndex + 1
                            ) {
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
                            const numCells =
                                index === 0 ? n : xSum.set[0].value;
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
                                    if (
                                        xSumValue > maxVal ||
                                        xSumValue < minVal
                                    ) {
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

                                if (
                                    (sumValid && sum !== xSumValue) ||
                                    (!sumValid && sum > xSumValue)
                                ) {
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
                    if (
                        skyscraper.value.length &&
                        !isNaN(parseInt(skyscraper.value))
                    ) {
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
                                let value =
                                    ci === index ? n : skyscraper.set[ci].value;
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

                            if (
                                (haveAllVals &&
                                    seenCells !== skyscraperValue) ||
                                seenCells > skyscraperValue
                            ) {
                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        };

        // Constraint classes

        // Renban
        window.renban = function (cell) {
            this.lines = [[cell]];

            this.show = function () {
                const renbanInfo = newConstraintInfo.filter(
                    (c) => c.name === "Renban"
                )[0];
                for (var a = 0; a < this.lines.length; a++) {
                    drawLine(
                        this.lines[a],
                        renbanInfo.color,
                        renbanInfo.colorDark,
                        renbanInfo.lineWidth
                    );
                }
            };

            this.addCellToLine = function (cell) {
                if (this.lines[this.lines.length - 1].length < size) {
                    this.lines[this.lines.length - 1].push(cell);
                }
            };
        };

        // Whispers
        window.whispers = function (cell) {
            this.lines = [[cell]];

            this.show = function () {
                const whispersInfo = newConstraintInfo.filter(
                    (c) => c.name === "Whispers"
                )[0];
                for (var a = 0; a < this.lines.length; a++) {
                    drawLine(
                        this.lines[a],
                        whispersInfo.color,
                        whispersInfo.colorDark,
                        whispersInfo.lineWidth
                    );
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
                const entropicLineInfo = newConstraintInfo.filter(
                    (c) => c.name === "Entropic Line"
                )[0];
                for (var a = 0; a < this.lines.length; a++) {
                    drawLine(
                        this.lines[a],
                        entropicLineInfo.color,
                        entropicLineInfo.colorDark,
                        entropicLineInfo.lineWidth
                    );
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
                const regionSumLineInfo = newConstraintInfo.filter(
                    (c) => c.name === "Region Sum Line"
                )[0];
                for (var a = 0; a < this.lines.length; a++) {
                    drawLine(
                        this.lines[a],
                        regionSumLineInfo.color,
                        regionSumLineInfo.colorDark,
                        regionSumLineInfo.lineWidth
                    );
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
                const nabnerLineInfo = newConstraintInfo.filter(
                    (c) => c.name === "Nabner"
                )[0];
                for (var a = 0; a < this.lines.length; a++) {
                    drawLine(
                        this.lines[a],
                        nabnerLineInfo.color,
                        nabnerLineInfo.colorDark,
                        nabnerLineInfo.lineWidth
                    );
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
                const nabnerLineInfo = newConstraintInfo.filter(
                    (c) => c.name === "Double Arrow"
                )[0];
                const nabnerColor = boolSettings["Dark Mode"]
                    ? nabnerLineInfo.colorDark
                    : nabnerLineInfo.color;
                for (let i = 0; i < this.lines.length; i++) {
                    ctx.lineWidth = cellSL * nabnerLineInfo.lineWidth * 0.5;

                    ctx.strokeStyle = nabnerColor;
                    ctx.beginPath();
                    ctx.moveTo(
                        this.lines[i][0].x + cellSL / 2,
                        this.lines[i][0].y + cellSL / 2
                    );
                    for (let j = 1; j < this.lines[i].length; j++)
                        ctx.lineTo(
                            this.lines[i][j].x + cellSL / 2,
                            this.lines[i][j].y + cellSL / 2
                        );
                    ctx.stroke();

                    ctx.fillStyle = boolSettings["Dark Mode"]
                        ? "#888888"
                        : "#EAEAEA";
                    for (
                        let j = 0, k = 0;
                        j < this.lines[i].length &&
                        (this.lines[i].length > 1 || !k);
                        j += this.lines[i].length - 1, k++
                    ) {
                        ctx.beginPath();
                        ctx.arc(
                            this.lines[i][j].x + cellSL / 2,
                            this.lines[i][j].y + cellSL / 2,
                            cellSL / 2 - ctx.lineWidth / 2,
                            0,
                            Math.PI * 2
                        );
                        ctx.fill();
                        ctx.stroke();
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
                this.cells.sort(
                    (a, b) => a.i * size + a.j - (b.i * size + b.j)
                );
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
                ctx.fillStyle = boolSettings["Dark Mode"]
                    ? "#F0F0F0"
                    : "#000000";
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
                    textOffset = this.isReverse
                        ? cellSL * -0.11
                        : cellSL * 0.08;
                }

                let textOffsetX = 0;
                if (
                    this.value.length == 2 &&
                    this.value[0] == "1" &&
                    this.value[1] != "1"
                ) {
                    textOffsetX = cellSL * 0.03;
                } else if (
                    this.value.length == 2 &&
                    this.value[0] != "1" &&
                    this.value[1] == "1"
                ) {
                    textOffsetX = cellSL * -0.03;
                }

                const textBaseX = this.cell.x + cellSL / 2;
                const textBaseY = this.cell.y + cellSL * 0.75;
                if (this.isRow) {
                    ctx.fillText(
                        this.value.length ? this.value : "-",
                        textBaseX - textOffset - textOffsetX,
                        textBaseY
                    );
                } else {
                    ctx.fillText(
                        this.value.length ? this.value : "-",
                        textBaseX - textOffsetX,
                        textBaseY - textOffset
                    );
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
                ctx.fillStyle = boolSettings["Dark Mode"]
                    ? "#F0F0F0"
                    : "#000000";
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
                    ctx.fillText(
                        this.value.length ? this.value : "-",
                        textBaseX - textOffset,
                        textBaseY
                    );
                } else {
                    ctx.fillText(
                        this.value.length ? this.value : "-",
                        textBaseX,
                        textBaseY - textOffset
                    );
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

        const origCategorizeTools = categorizeTools;
        categorizeTools = function () {
            origCategorizeTools();

            let toolLineIndex = toolConstraints.indexOf("Palindrome");
            let toolPerCellIndex = toolConstraints.indexOf("Maximum");
            let toolOutsideIndex = toolConstraints.indexOf("Sandwich Sum");
            for (let info of newConstraintInfo) {
                if (info.type === "line") {
                    if (toolLineIndex < toolPerCellIndex) toolPerCellIndex++;
                    if (toolLineIndex < toolOutsideIndex) toolOutsideIndex++;
                    toolConstraints.splice(++toolLineIndex, 0, info.name);
                    lineConstraints.push(info.name);
                } else if (info.type === "cage") {
                    if (toolPerCellIndex < toolLineIndex) toolLineIndex++;
                    if (toolPerCellIndex < toolOutsideIndex) toolOutsideIndex++;
                    toolConstraints.splice(++toolPerCellIndex, 0, info.name);
                    regionConstraints.push(info.name);
                } else if (info.type === "outside") {
                    if (toolOutsideIndex < toolLineIndex) toolLineIndex++;
                    if (toolOutsideIndex < toolPerCellIndex) toolPerCellIndex++;
                    toolConstraints.splice(++toolOutsideIndex, 0, info.name);
                    outsideConstraints.push(info.name);
                    typableConstraints.push(info.name);
                }
            }

            draggableConstraints = [
                ...new Set([...lineConstraints, ...regionConstraints]),
            ];
            multicellConstraints = [
                ...new Set([
                    ...lineConstraints,
                    ...regionConstraints,
                    ...borderConstraints,
                    ...cornerConstraints,
                ]),
            ];
            betweenCellConstraints = [
                ...borderConstraints,
                ...cornerConstraints,
            ];
            allConstraints = [...boolConstraints, ...toolConstraints];

            tools = [...toolConstraints, ...toolCosmetics];
            selectableTools = [
                ...selectableConstraints,
                ...selectableCosmetics,
            ];
            lineTools = [...lineConstraints, ...lineCosmetics];
            regionTools = [...regionConstraints, ...regionCosmetics];
            diagonalRegionTools = [
                ...diagonalRegionConstraints,
                ...diagonalRegionCosmetics,
            ];
            outsideTools = [...outsideConstraints, ...outsideCosmetics];
            outsideCornerTools = [
                ...outsideCornerConstraints,
                ...outsideCornerCosmetics,
            ];
            oneCellAtATimeTools = [
                ...perCellConstraints,
                ...draggableConstraints,
                ...draggableCosmetics,
            ];
            draggableTools = [...draggableConstraints, ...draggableCosmetics];
            multicellTools = [...multicellConstraints, ...multicellCosmetics];
        };

        // Tooltips
        for (let info of newConstraintInfo) {
            descriptions[info.name] = info.tooltip;
        }

        // Puzzle title
        // Unfortuantely, there's no way to shim this so it's duplicated in full.
        getPuzzleTitle = function () {
            var title = "";

            ctx.font = titleLSize + "px Arial";

            if (customTitle.length) {
                title = customTitle;
            } else {
                if (size !== 9) title += size + "x" + size + " ";
                if (
                    getCells().some(
                        (a) =>
                            a.region !==
                            Math.floor(a.i / regionH) * regionH +
                                Math.floor(a.j / regionW)
                    )
                )
                    title += "Irregular ";
                if (constraints[cID("Extra Region")].length)
                    title += "Extra-Region ";
                if (
                    constraints[cID("Odd")].length &&
                    !constraints[cID("Even")].length
                )
                    title += "Odd ";
                if (
                    !constraints[cID("Odd")].length &&
                    constraints[cID("Even")].length
                )
                    title += "Even ";
                if (
                    constraints[cID("Odd")].length &&
                    constraints[cID("Even")].length
                )
                    title += "Odd-Even ";
                if (
                    constraints[cID("Diagonal +")] !==
                    constraints[cID("Diagonal -")]
                )
                    title += "Single-Diagonal ";
                if (
                    constraints[cID("Nonconsecutive")] &&
                    !(
                        constraints[cID("Difference")].length &&
                        constraints[cID("Difference")].some((a) =>
                            ["", "1"].includes(a.value)
                        )
                    ) &&
                    !constraints[cID("Ratio")].negative
                )
                    title += "Nonconsecutive ";
                if (
                    constraints[cID("Nonconsecutive")] &&
                    constraints[cID("Difference")].length &&
                    constraints[cID("Difference")].some((a) =>
                        ["", "1"].includes(a.value)
                    ) &&
                    !constraints[cID("Ratio")].negative
                )
                    title += "Consecutive ";
                if (
                    !constraints[cID("Nonconsecutive")] &&
                    constraints[cID("Difference")].length &&
                    constraints[cID("Difference")].every((a) =>
                        ["", "1"].includes(a.value)
                    )
                )
                    title += "Consecutive-Pairs ";
                if (constraints[cID("Antiknight")]) title += "Antiknight ";
                if (constraints[cID("Antiking")]) title += "Antiking ";
                if (constraints[cID("Disjoint Groups")])
                    title += "Disjoint-Group ";
                if (
                    constraints[cID("XV")].length ||
                    constraints[cID("XV")].negative
                )
                    title +=
                        "XV " + (constraints[cID("XV")].negative ? "(-) " : "");
                if (constraints[cID("Little Killer Sum")].length)
                    title += "Little Killer ";
                if (constraints[cID("Sandwich Sum")].length)
                    title += "Sandwich ";
                if (constraints[cID("Thermometer")].length) title += "Thermo ";
                if (constraints[cID("Palindrome")].length)
                    title += "Palindrome ";
                if (
                    constraints[cID("Difference")].length &&
                    constraints[cID("Difference")].some(
                        (a) => !["", "1"].includes(a.value)
                    ) &&
                    !(
                        constraints[cID("Nonconsecutive")] &&
                        constraints[cID("Ratio")].negative
                    )
                )
                    title += "Difference ";
                if (
                    (constraints[cID("Ratio")].length ||
                        constraints[cID("Ratio")].negative) &&
                    !(
                        constraints[cID("Nonconsecutive")] &&
                        constraints[cID("Ratio")].negative
                    )
                )
                    title +=
                        "Ratio " +
                        (constraints[cID("Ratio")].negative ? "(-) " : "");
                if (
                    constraints[cID("Nonconsecutive")] &&
                    constraints[cID("Ratio")].negative
                )
                    title += "Kropki ";
                if (constraints[cID("Killer Cage")].length) title += "Killer ";
                if (constraints[cID("Clone")].length) title += "Clone ";
                if (constraints[cID("Arrow")].length) title += "Arrow ";
                if (constraints[cID("Between Line")].length)
                    title += "Between ";
                if (constraints[cID("Quadruple")].length)
                    title += "Quadruples ";
                if (
                    constraints[cID("Minimum")].length ||
                    constraints[cID("Maximum")].length
                )
                    title += "Extremes ";

                for (let info of newConstraintInfo) {
                    if (
                        constraints[cID(info.name)] &&
                        constraints[cID(info.name)].length > 0
                    ) {
                        title += `${info.name} `;
                    }
                }

                title += "Sudoku";

                if (
                    constraints[cID("Diagonal +")] &&
                    constraints[cID("Diagonal -")]
                )
                    title += " X";

                if (title === "Sudoku") title = "Classic Sudoku";

                if (ctx.measureText(title).width > canvas.width - 711)
                    title = "Extreme Variant Sudoku";
            }

            buttons[buttons.findIndex((a) => a.id === "EditInfo")].x =
                canvas.width / 2 + ctx.measureText(title).width / 2 + 40;

            return title;
        };

        // Multi-column constraint sidebar
        let constraintSidebarWidth = 0;
        const prevCreateSidebarConstraints = createSidebarConstraints;
        createSidebarConstraints = function () {
            prevCreateSidebarConstraints();

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
                    (mouseX < gridX - sidebarDist || mouseX > gridX - sidebarDist + constraintSidebarWidth || mouseY < gridY || mouseY > gridY + gridSL)
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
    }

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