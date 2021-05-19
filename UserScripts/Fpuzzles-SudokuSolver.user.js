// ==UserScript==
// @name         Fpuzzles-SudokuSolver
// @namespace    http://tampermonkey.net/
// @version      0.2.2-alpha
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

    let solverSocket = null;
    let lastSentPuzzle = {};
    let commandIsComplete = false;
    let sendPuzzle = function(command) {
        if (!solverSocket) {
            return;
        }

        if (command === "cancel") {
            solverSocket.send('fpuzzles:' + nonce + ':cancel:cancel');
            return;
        }

        var puzzle = exportPuzzle();
        if (!preventResentCommands[command] || lastSentPuzzle[command] !== puzzle) {
            nonce = nonce + 1;
            lastCommand = command;
            solverSocket.send('fpuzzles:' + nonce + ':' + command + ':' + puzzle);
            lastSentPuzzle[command] = puzzle;
            connectButton.title = "Calculating...";
        }
    }

    let prevConsoleOutputTop = 0;
    let prevConsoleOutputHeight = 0;
    let trueCandidatesButton = null;
    let simpleStepsButton = null;
    let cancelButton = null;
    let doCancelableCommand = function(force) {
        if (!force && !this.hovering()) {
            return;
        }
        if (!solverSocket) {
            this.origClick();
            return;
        }
        boolSettings['TrueCandidates'] = false;

        if (cancelButton === this) {
            if (this.title !== 'Cancelling...') {
                sendPuzzle('cancel');
                this.title = 'Cancelling...';
            }
        } else if (this.title === this.origTitle) {
            sendPuzzle(this.solverCommand);
            this.title = "Cancel";
            cancelButton = this;
        }
    }

    let initCancelableButton = function(button, command) {
        if (!button.origClick) {
            button.origClick = button.click;
            button.click = doCancelableCommand;
        }
        if (!button.origTitle) {
            button.origTitle = button.title;
        }
        if (!button.solverCommand) {
            button.solverCommand = command;
        }
        if (cancelButton && cancelButton !== button && cancelButton.solverCommand === command) {
            button.title = cancelButton.title;
            cancelButton = button;
        }
    }

    let hookSolverButtons = function() {
        const consoleSidebar = sidebars.filter(sb => sb.title === 'Console')[0];
        const mainSidebar = sidebars.filter(sb => sb.title === 'Main')[0];
        if (consoleSidebar) {
            const solutionPathButton = consoleSidebar.buttons.filter(b => b.title === 'Solution Path')[0];
            const stepButton = consoleSidebar.buttons.filter(b => b.title === 'Step')[0];
            const checkButton = consoleSidebar.buttons.filter(b => b.title === 'Check')[0];
            const countButton = consoleSidebar.buttons.filter(b => b.title === 'Solution Count')[0];
            const solveButton = mainSidebar.buttons.filter(b => b.title === 'Solve')[0];

            if (!trueCandidatesButton) {
                boolSettings.push('TrueCandidates');
                boolSettings['TrueCandidates'] = false;
                trueCandidatesButton = new button(solutionPathButton.x - buttonLH / 2 - buttonGap / 2, solutionPathButton.y + (buttonLH + buttonGap) * 4, buttonW - buttonLH - buttonGap, buttonLH, ['Solving', 'Setting'], 'TrueCandidates', 'True Candid.')
                trueCandidatesButton.origClick = trueCandidatesButton.click;
                trueCandidatesButton.click = function() {
                    if (!this.hovering()) {
                        return;
                    }

                    this.origClick();

                    if (boolSettings['TrueCandidates']) {
                        sendPuzzle('truecandidates');
                    } else {
                        lastSentPuzzle['truecandidates'] = null;
                    }
                }
            }
            if (!simpleStepsButton) {
                boolSettings.push('SimpleStep');
                boolSettings['SimpleStep'] = false;
                simpleStepsButton = new button(solutionPathButton.x - buttonLH / 2 - buttonGap / 2, solutionPathButton.y + (buttonLH + buttonGap) * 5, buttonW - buttonLH - buttonGap, buttonLH, ['Solving', 'Setting'], 'SimpleStep', 'Simple Logic')
            }

            if (!solutionPathButton.origClick) {
                solutionPathButton.origClick = solutionPathButton.click;
                solutionPathButton.click = function() {
                    if (!this.hovering()) {
                        return;
                    }
                    if (!solverSocket) {
                        this.origClick();
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
            }
            if (!stepButton.origClick) {
                stepButton.origClick = stepButton.click;
                stepButton.click = function() {
                    if (!this.hovering()) {
                        return;
                    }
                    if (!solverSocket) {
                        this.origClick();
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
            }
            initCancelableButton(checkButton, 'check');
            initCancelableButton(countButton, 'count');
            initCancelableButton(solveButton, 'solve');
        }
    }

    let buttonsShown = false;
    let showSolverButtons = function() {
        if (buttonsShown) {
            return;
        }
        buttonsShown = true;

        const consoleSidebar = sidebars.filter(sb => sb.title === 'Console')[0];
        if (consoleSidebar) {
            consoleSidebar.sections[0].y += (buttonLH + buttonGap) * 2;
            consoleSidebar.buttons.push(trueCandidatesButton);
            consoleSidebar.buttons.push(simpleStepsButton);
        }

        if (prevConsoleOutputTop === 0) {
            prevConsoleOutputTop = consoleOutput.style.top;
            prevConsoleOutputHeight = consoleOutput.style.height;
        }
        consoleOutput.style.top = "51.6%";
        consoleOutput.style.height = "42%";
    }

    let hideSolverButtons = function() {
        if (!buttonsShown) {
            return;
        }
        buttonsShown = false;

        const consoleSidebar = sidebars.filter(sb => sb.title === 'Console')[0];
        if (consoleSidebar) {
            consoleSidebar.sections[0].y -= (buttonLH + buttonGap) * 2;

            let index = consoleSidebar.buttons.indexOf(trueCandidatesButton);
            if (index > -1) {
                consoleSidebar.buttons.splice(index, 1);
            }
            index = consoleSidebar.buttons.indexOf(simpleStepsButton);
            if (index > -1) {
                consoleSidebar.buttons.splice(index, 1);
            }
        }

        boolSettings['TrueCandidates'] = false;
        if (prevConsoleOutputTop !== 0) {
            consoleOutput.style.top = prevConsoleOutputTop;
            consoleOutput.style.height = prevConsoleOutputHeight;
        }
    }

    let origCreateSidebarConsole = createSidebarConsole;
    createSidebarConsole = function() {
        origCreateSidebarConsole();
        buttonsShown = false;
        hookSolverButtons();
        if (solverSocket) {
            showSolverButtons();
        }
    }

    let origCreateSidebarMain = createSidebarMain;
    createSidebarMain = function() {
        origCreateSidebarMain();
        hookSolverButtons();
    }

    let importCandidates = function(str, distinguished) {
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

    let importGivens = function(str) {
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
            importCandidates(puzzle);
        }
    }
    let handleSolve = function(puzzle) {
        if (puzzle === 'Invalid') {
            clearGrid(false, true);
        } else {
            importGivens(puzzle);
        }
        if (cancelButton) {
            cancelButton.title = cancelButton.origTitle;
            cancelButton = null;
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
            if (cancelButton) {
                cancelButton.title = cancelButton.origTitle;
                cancelButton = null;
            }
        }
    }
    let handleCount = function(puzzle) {
        if (puzzle.startsWith('progress:')) {
            let count = puzzle.substring('progress:'.length);
            clearConsole();
            log('Found ' + count + ' solutions so far...');
            commandIsComplete = false;
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
            if (cancelButton) {
                cancelButton.title = cancelButton.origTitle;
                cancelButton = null;
            }
        }
    }
    let handlePath = function(puzzle) {
        let colonIndex = puzzle.indexOf(':');
        if (colonIndex >= 0) {
            let candidateString = puzzle.substring(0, colonIndex);
            let description = puzzle.substring(colonIndex + 1);
            importCandidates(candidateString);
            clearConsole();
            log(description, { newLine: false });
        }
    }
    let handleStep = function(puzzle) {
        let colonIndex = puzzle.indexOf(':');
        if (colonIndex >= 0) {
            let candidateString = puzzle.substring(0, colonIndex);
            let description = puzzle.substring(colonIndex + 1);
            importCandidates(candidateString, true);
            if (!lastMessageWasStep) {
                clearConsole();
            }
            log(description, { newLine: false });
            thisMessageWasStep = true;
        }
    }

    let processingMessage = false;
    connectButton.click = function() {
        if (!this.hovering()) {
            return;
        }

        if (!solverSocket) {
            connectButton.title = 'Connecting...';

            let socket = new WebSocket("ws://localhost:4545");
            socket.onopen = function() {
                console.log("Connection succeeded");
                hookSolverButtons();
                showSolverButtons();
                connectButton.title = 'Disconnect';
            };

            socket.onmessage = function(msg) {
                let expectedNonce = nonce + ':';
                if (msg.data.startsWith(expectedNonce)) {
                    if (!allowCommandWhenUndo[lastCommand] && changeIndex < changes.length - 1) {
                        // Undo has been pressed
                        return;
                    }

                    let payload = msg.data.substring(expectedNonce.length);
                    if (payload === 'canceled') {
                        log('Operation canceled.');
                        if (cancelButton) {
                            cancelButton.title = cancelButton.origTitle;
                            cancelButton = null;
                        }
                        connectButton.title = 'Disconnect';
                        commandIsComplete = true;
                        return;
                    }

                    processingMessage = true;
                    thisMessageWasStep = false;
                    commandIsComplete = true;
                    if (lastCommand === 'truecandidates') {
                        handleTrueCandidates(payload);
                    } else if (lastCommand === 'solve') {
                        handleSolve(payload);
                    } else if (lastCommand === 'check') {
                        handleCheck(payload);
                    } else if (lastCommand === 'count') {
                        handleCount(payload);
                    } else if (lastCommand === 'solvepath' || lastCommand === 'simplepath') {
                        handlePath(payload);
                    } else if (lastCommand === 'step' || lastCommand === 'simplestep') {
                        handleStep(payload);
                    }
                    lastMessageWasStep = thisMessageWasStep;

                    if (commandIsComplete) {
                        connectButton.title = 'Disconnect';
                        commandIsComplete = false;
                    }
                    processingMessage = false;
                }
            };

            socket.onclose = function() {
                connectButton.title = 'Connect';
                console.log("Connection closed");
                solverSocket = null;
                lastSentPuzzle = {};
                hideSolverButtons();
            };
            solverSocket = socket;
        } else {
            solverSocket.close();
            solverSocket = null;
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
                if (!processingMessage) {
                    if (boolSettings['TrueCandidates']) {
                        sendPuzzle('truecandidates');
                    } else if (cancelButton) {
                        cancelButton.click(true);
                    }
                }
                return true;
            },
            set: function(target, property, value, receiver) {
                target[property] = value;

                if (!processingMessage) {
                    if (boolSettings['TrueCandidates']) {
                        sendPuzzle('truecandidates');
                    } else if (cancelButton) {
                        cancelButton.click(true);
                    }
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

    const openCTCButton = new button(canvas.width / 2 - 175, canvas.height / 2 + 6 + (buttonLH + buttonGap) * 4, 400, buttonLH, ['Export'], 'OpenCTC', 'Open in CTC');
    const openSudokuLabButton = new button(canvas.width / 2 - 175, canvas.height / 2 + 6 + (buttonLH + buttonGap) * 5, 400, buttonLH, ['Export'], 'SudokuLab', 'Open in Sudoku Lab');

    let origCreateOtherButtons = createOtherButtons;
    createOtherButtons = function() {
        let index = boolSettings.indexOf('TrueCandidates');
        if (index > -1) {
            boolSettings.splice(index, 1);
        }
        index = boolSettings.indexOf('SimpleStep');
        if (index > -1) {
            boolSettings.splice(index, 1);
        }
        origCreateOtherButtons();
        boolSettings.push('TrueCandidates');
        boolSettings.push('SimpleStep');

        buttons.filter(b => b.modes.includes('Export') && b !== openCTCButton && b !== openSudokuLabButton && b.x < canvas.width / 2).forEach(b => b.y -= 90);
    }

    // Export buttons
    popups.export.h += 200;
    buttons.push(openCTCButton);
    buttons.push(openSudokuLabButton);

    openCTCButton.click = function() {
        if (!this.hovering()) {
            return;
        }
        window.open('https://app.crackingthecryptic.com/sudoku/?puzzleid=fpuzzles' + encodeURIComponent(exportPuzzle()));
    }

    openSudokuLabButton.click = function() {
        if (!this.hovering()) {
            return;
        }
        window.open('https://www.sudokulab.net/?fpuzzle=' + exportPuzzle());
    }

    buttons.filter(b => b.modes.includes('Export')).forEach(b => b.y -= 90);
    document.getElementById('previewTypeBox').style.top = '29%';
})();