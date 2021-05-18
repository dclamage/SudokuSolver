// ==UserScript==
// @name         Fpuzzles-SudokuSolver
// @namespace    http://tampermonkey.net/
// @version      0.2.1-alpha
// @description  Connect f-puzzles to SudokuSolver
// @author       Rangsk
// @match        https://*.f-puzzles.com/*
// @match        https://f-puzzles.com/*
// @icon         data:image/gif;base64,R0lGODlhAQABAAAAACH5BAEKAAEALAAAAAABAAEAAAICTAEAOw==
// @grant        none
// ==/UserScript==

(function() {
    'use strict';

    let connectButton = new button(canvas.width - 200, 40, 200, 40, ['Setting', 'Solving'], 'Connect', 'Connect');

    let nonce = 0;
    let lastCommand = '';

    let preventResentCommands = [];
    preventResentCommands['truecandidates'] = true;

    let allowCommandWhenUndo = [];
    allowCommandWhenUndo['check'] = true;
    allowCommandWhenUndo['count'] = true;

    window.sendPuzzle = function(command) {
        if (!window.solverSocket) {
            return;
        }

        if (!window.lastSentPuzzle) {
            window.lastSentPuzzle = [];
        }

        var puzzle = exportPuzzle();
        if (!preventResentCommands[command] || window.lastSentPuzzle[command] !== puzzle) {
            nonce = nonce + 1;
            lastCommand = command;
            window.solverSocket.send('fpuzzles:' + nonce + ':' + command + ':' + puzzle);
            window.lastSentPuzzle[command] = puzzle;
            connectButton.title = "Calculating...";
        }
    }

    let buttonsMap = null;
    let populateButtonsMapFromArray = function(buttonArray) {
        buttonArray.forEach(function(button, _index) {
            buttonsMap[button.title] = button;
        });
    }
    let populateButtonsMap = function() {
        if (buttonsMap !== null) {
            return;
        }
        buttonsMap = {};
        populateButtonsMapFromArray(buttons);
        sidebars.forEach(function(sidebar, _index) {
            populateButtonsMapFromArray(sidebar.buttons);
        });
    }

    let prevConsoleOutputTop = 0;
    let prevConsoleOutputHeight = 0;
    let hookSolverButtons = function() {
        const consoleSidebar = sidebars.filter(sb => sb.title === 'Console')[0];
        if (consoleSidebar) {
            consoleSidebar.sections[0].y += (buttonLH + buttonGap) * 2;

            const solutionPathButton = consoleSidebar.buttons.filter(b => b.title === 'Solution Path')[0];
            const stepButton = buttonsMap['Step'];
            const checkButton = buttonsMap['Check'];
            const countButton = buttonsMap['Solution Count'];
            const solveButton = buttonsMap["Solve"];
            if (!window.trueCandidatesButton) {
                boolSettings.push('TrueCandidates');
                boolSettings['TrueCandidates'] = false;
                window.trueCandidatesButton = new button(solutionPathButton.x - buttonLH / 2 - buttonGap / 2, solutionPathButton.y + (buttonLH + buttonGap) * consoleSidebar.buttons.length, buttonW - buttonLH - buttonGap, buttonLH, ['Solving', 'Setting'], 'TrueCandidates', 'True Candid.')
                window.trueCandidatesButton.origClick = window.trueCandidatesButton.click;
                window.trueCandidatesButton.click = function() {
                    if (!this.hovering()) {
                        return;
                    }

                    this.origClick();

                    if (boolSettings['TrueCandidates']) {
                        window.sendPuzzle('truecandidates');
                    } else {
                        window.lastSentPuzzle['truecandidates'] = null;
                    }
                }
            }
            consoleSidebar.buttons.push(window.trueCandidatesButton);
            if (!window.simpleStepsButton) {
                boolSettings.push('SimpleStep');
                boolSettings['SimpleStep'] = false;
                window.simpleStepsButton = new button(solutionPathButton.x - buttonLH / 2 - buttonGap / 2, solutionPathButton.y + (buttonLH + buttonGap) * consoleSidebar.buttons.length, buttonW - buttonLH - buttonGap, buttonLH, ['Solving', 'Setting'], 'SimpleStep', 'Simple Logic')
                window.simpleStepsButton.origClick = window.simpleStepsButton.click;
            }
            consoleSidebar.buttons.push(window.simpleStepsButton);

            solutionPathButton.click = function() {
                if (!this.hovering()) {
                    return;
                }
                boolSettings['TrueCandidates'] = false;

                forgetFutureChanges();
                if (boolSettings['SimpleStep']) {
                    sendPuzzle('simplepath');
                } else {
                    sendPuzzle('solvepath');
                }
            }
            stepButton.click = function() {
                if (!this.hovering()) {
                    return;
                }
                boolSettings['TrueCandidates'] = false;

                forgetFutureChanges();
                if (boolSettings['SimpleStep']) {
                    sendPuzzle('simplestep');
                } else {
                    sendPuzzle('step');
                }
            }
            checkButton.click = function() {
                if (!this.hovering()) {
                    return;
                }
                boolSettings['TrueCandidates'] = false;

                sendPuzzle('check');
            }
            countButton.click = function() {
                if (!this.hovering()) {
                    return;
                }
                boolSettings['TrueCandidates'] = false;

                sendPuzzle('count');
            }
            solveButton.click = function() {
                if (!this.hovering()) {
                    return;
                }
                boolSettings['TrueCandidates'] = false;

                sendPuzzle('solve');
            }
        }

        if (prevConsoleOutputTop == 0) {
            prevConsoleOutputTop = consoleOutput.style.top;
            prevConsoleOutputHeight = consoleOutput.style.height;
        }
        consoleOutput.style.top = "51.6%";
        consoleOutput.style.height = "42%";
    }

    let unhookSolverButtons = function() {
        const consoleSidebar = sidebars.filter(sb => sb.title === 'Console')[0];
        if (consoleSidebar) {
            consoleSidebar.sections[0].y -= (buttonLH + buttonGap) * 2;

            let index = consoleSidebar.buttons.indexOf(window.trueCandidatesButton);
            if (index > -1) {
                consoleSidebar.buttons.splice(index, 1);
            }
            index = consoleSidebar.buttons.indexOf(window.simpleStepsButton);
            if (index > -1) {
                consoleSidebar.buttons.splice(index, 1);
            }
        }

        boolSettings['TrueCandidates'] = false;
        consoleOutput.style.top = prevConsoleOutputTop;
        consoleOutput.style.height = prevConsoleOutputHeight;
    }

    let solverHooked = false;
    window.hookSolver = function() {
        if (solverHooked) {
            return;
        }
        populateButtonsMap();

        window.prevButtonActions = {};
        for (let name in buttonsMap) {
            let button = buttonsMap[name];
            if (button !== connectButton && button !== window.trueCandidatesButton && button !== window.simpleStepsButton) {
                window.prevButtonActions[name] = button.click;
            }
        }

        hookSolverButtons();
        solverHooked = true;
    }

    window.unhookSolver = function() {
        if (!window.prevButtonActions || !solverHooked) {
            return;
        }

        for (let name in buttonsMap) {
            let button = buttonsMap[name];
            if (button !== connectButton && button !== window.trueCandidatesButton && button !== window.simpleStepsButton) {
                button.click = window.prevButtonActions[name];
            }
        }

        unhookSolverButtons();
        solverHooked = false;
    }

    let origCreateSidebarConsole = createSidebarConsole;
    createSidebarConsole = function() {
        origCreateSidebarConsole();
        if (solverHooked) {
            hookSolverButtons();
        }
    }

    window.importCandidates = function(str, distinguished) {
        if (size <= 9) {
            const numCells = str.length / size;
            for (let i = 0; i < numCells; i++) {
                const cell = grid[Math.floor(i / size)][i % size];
                if (!cell || cell.given) {
                    continue;
                }
                const candidates = [];
                for (let candidateIndex = 0; candidateIndex < size; candidateIndex++) {
                    const candidate = parseInt(str[i * size + candidateIndex]);
                    if (!isNaN(candidate) && candidate != 0) {
                        candidates.push(candidate);
                    }
                }

                let isSet = false;
                if (candidates.length == size && candidates[0] == candidates[1]) {
                    candidates.splice(1);
                    isSet = true;
                }

                cell.value = 0;
                cell.centerPencilMarks = [];
                cell.candidates = candidates;
                if (candidates.length == 1 && (isSet || !distinguished)) {
                    cell.value = candidates[0];
                } else {
                    cell.centerPencilMarks = candidates;
                }
            }
        } else {
            const numCells = str.length / (size * 2);
            for (let i = 0; i < numCells; i++) {
                const cell = grid[Math.floor(i / size)][i % size];
                if (!cell || cell.given) {
                    continue;
                }
                const candidates = [];
                for (let candidateIndex = 0; candidateIndex < size * 2; candidateIndex += 2) {
                    const startIndex = i * size * 2 + candidateIndex;
                    const digit0 = parseInt(str[startIndex]);
                    const digit1 = parseInt(str[startIndex + 1]);
                    if (!isNaN(digit0) && !isNaN(digit1) && (digit0 == 1 || digit1 != 0)) {
                        const candidate = parseInt(digit0) * 10 + parseInt(digit1);
                        candidates.push(candidate);
                    }
                }

                let isSet = false;
                if (candidates.length == size && candidates[0] == candidates[1]) {
                    candidates.splice(1);
                    isSet = true;
                }

                cell.value = 0;
                cell.centerPencilMarks = [];
                cell.candidates = candidates;
                if (candidates.length == 1 && (isSet || !distinguished)) {
                    cell.value = candidates[0];
                } else {
                    cell.centerPencilMarks = candidates;
                }
            }
        }
        onInputEnd();
    }

    window.importGivens = function(str) {
        if (size <= 9) {
            const numCells = str.length;
            for (let i = 0; i < numCells; i++) {
                const cell = grid[Math.floor(i / size)][i % size];
                if (!cell || cell.given) {
                    continue;
                }
                cell.value = parseInt(str[i]);
                cell.centerPencilMarks = [];
                cell.candidates = [cell.value];
            }
        } else {
            const numCells = str.length / 2;
            for (let i = 0; i < numCells; i++) {
                const cell = grid[Math.floor(i / size)][i % size];
                if (!cell || cell.given) {
                    continue;
                }
                cell.value = parseInt(str[i * 2]) * 10 + parseInt(str[i * 2 + 1]);
                cell.centerPencilMarks = [];
                cell.candidates = [cell.value];
            }
        }
        onInputEnd();
    }

    let lastMessageWasStep = false;
    let thisMessageWasStep = false;
    let handleTrueCandidates = function(puzzle) {
        if (puzzle === 'Invalid') {
            clearGrid(false, true);
            connectButton.title = 'INVALID';
        } else {
            window.importCandidates(puzzle);
        }
    }
    let handleSolve = function(puzzle) {
        if (puzzle === 'Invalid') {
            clearGrid(false, true);
        } else {
            window.importGivens(puzzle);
        }
    }
    let handleCheck = function(puzzle) {
        if (puzzle.startsWith('final:')) {
            let count = puzzle.substring('final:'.length);
            clearConsole();
            if (count == 0) {
                log('There are no solutions.');
            } else if (count == 1) {
                log('There is a unique solution.');
            } else {
                log('There are multiple solutions.');
            }
        }
    }
    let handleCount = function(puzzle) {
        if (puzzle.startsWith('progress:')) {
            let count = puzzle.substring('progress:'.length);
            clearConsole();
            log('Found ' + count + ' solutions so far...');
        } else if (puzzle.startsWith('final:')) {
            let count = puzzle.substring('final:'.length);
            clearConsole();
            if (count == 0) {
                log('There are no solutions.');
            } else if (count == 1) {
                log('There is a unique solution.');
            } else {
                log('There are exactly ' + count + ' solutions.');
            }
        }
    }
    let handlePath = function(puzzle) {
        let colonIndex = puzzle.indexOf(':');
        if (colonIndex >= 0) {
            let candidateString = puzzle.substring(0, colonIndex);
            let description = puzzle.substring(colonIndex + 1);
            window.importCandidates(candidateString);
            clearConsole();
            log(description, { newLine: false });
        }
    }
    let handleStep = function(puzzle) {
        let colonIndex = puzzle.indexOf(':');
        if (colonIndex >= 0) {
            let candidateString = puzzle.substring(0, colonIndex);
            let description = puzzle.substring(colonIndex + 1);
            window.importCandidates(candidateString, true);
            if (!lastMessageWasStep) {
                clearConsole();
            }
            log(description, { newLine: false });
            thisMessageWasStep = true;
        }
    }

    connectButton.click = function() {
        if (!this.hovering()) {
            return;
        }

        if (!window.solverSocket) {
            connectButton.title = 'Connecting...';

            let socket = new WebSocket("ws://localhost:4545");
            socket.onopen = function() {
                console.log("Connection succeeded");
                window.hookSolver();
                connectButton.title = 'Disconnect';
            };

            socket.onmessage = function(msg) {
                let expectedNonce = nonce + ':';
                if (msg.data.startsWith(expectedNonce)) {
                    connectButton.title = 'Disconnect';
                    if (!allowCommandWhenUndo[lastCommand] && changeIndex < changes.length - 1) {
                        // Undo has been pressed
                        return;
                    }

                    let puzzle = msg.data.substring(expectedNonce.length);
                    thisMessageWasStep = false;
                    if (lastCommand === 'truecandidates') {
                        handleTrueCandidates(puzzle);
                    } else if (lastCommand === 'solve') {
                        handleSolve(puzzle);
                    } else if (lastCommand === 'check') {
                        handleCheck(puzzle);
                    } else if (lastCommand === 'count') {
                        handleCount(puzzle);
                    } else if (lastCommand === 'solvepath' || lastCommand === 'simplepath') {
                        handlePath(puzzle);
                    } else if (lastCommand === 'step' || lastCommand === 'simplestep') {
                        handleStep(puzzle);
                    }
                    lastMessageWasStep = thisMessageWasStep;
                }
            };

            socket.onclose = function() {
                connectButton.title = 'Connect';
                console.log("Connection closed");
                window.solverSocket = null;
                window.lastSentPuzzle = null;
                window.unhookSolver();
            };
            window.solverSocket = socket;
        } else {
            window.solverSocket.close();
            window.solverSocket = null;
        }
    }
    buttons.push(connectButton);

    let installChangeProxy = function() {
        // a proxy for the changes array
        var changesProxy = new Proxy(changes, {
            apply: function(target, thisArg, argumentsList) {
                return thisArg[target].apply(this, argumentList);
            },
            deleteProperty: function(target, property) {
                if (boolSettings['TrueCandidates']) {
                    window.sendPuzzle('truecandidates');
                }
                return true;
            },
            set: function(target, property, value, receiver) {
                target[property] = value;
                if (boolSettings['TrueCandidates']) {
                    window.sendPuzzle('truecandidates');
                }
                return true;
            }
        });
        changes = changesProxy;
    }

    // These are f-puzzles functions which need to be hooked to restore the changes proxy
    clearChangeHistory = function() {
        changes = [];
        changeIndex = 0;
        installChangeProxy();

        changes.push({ state: exportPuzzle(true), solving: mode === 'Solving' });
    }

    forgetFutureChanges = function() {
        changes = changes.slice(0, changeIndex + 1);
        installChangeProxy();
    }

    installChangeProxy();
})();