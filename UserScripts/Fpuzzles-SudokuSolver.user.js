// ==UserScript==
// @name         Fpuzzles-SudokuSolver
// @namespace    http://tampermonkey.net/
// @version      0.2.6-alpha
// @description  Connect f-puzzles to SudokuSolver
// @author       Rangsk
// @match        https://*.f-puzzles.com/*
// @match        https://f-puzzles.com/*
// @icon         data:image/gif;base64,R0lGODlhAQABAAAAACH5BAEKAAEALAAAAAABAAEAAAICTAEAOw==
// @grant        none
// @run-at       document-start
// ==/UserScript==

(function() {
    'use strict';

    const doShim = function() {
        // Makes center and corner marks larger so they're easier to see.
        let textScale = 1.5;
        const settingsIcon = '⚙️';

        const connectButton = new button(canvas.width - 208, 40, 215, 40, ['Setting', 'Solving'], 'Connect', 'Connect');
        const settingsButton = new button(canvas.width - 65, 40, 40, 40, ['Setting', 'Solving'], settingsIcon, settingsIcon);

        let nonce = 0;
        let lastCommand = '';
        let lastClearCommand = '';

        let preventResentCommands = [];
        preventResentCommands['truecandidates'] = true;
        preventResentCommands['truecandidatescolored'] = true;

        let allowCommandWhenUndo = [];
        allowCommandWhenUndo['check'] = true;
        allowCommandWhenUndo['count'] = true;

        let extraSettingsNames = [];
        extraSettingsNames.push('TrueCandidates');

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

            if (command === 'truecandidates' && boolSettings['ColoredCandidates']) {
                command = "truecandidatescolored";
            }

            var puzzle = exportPuzzle();
            if (!preventResentCommands[command] || lastSentPuzzle[command] !== puzzle) {
                if (command === '')
                    nonce = nonce + 1;
                solverSocket.send('fpuzzles:' + nonce + ':' + command + ':' + puzzle);
                lastSentPuzzle[command] = puzzle;
                connectButton.title = "Calculating...";

                if (command === 'solve' ||
                    command === 'check' ||
                    command === 'count' ||
                    command === 'solvepath' || command === 'simplepath' ||
                    (command === 'step' || command === 'simplestep') && !(lastClearCommand === 'step' || lastClearCommand === 'simplestep')) {
                    clearConsole();
                    lastClearCommand = command;
                }
                lastCommand = command;
            }
        }

        let clearPencilmarkColors = function() {
            for (let i = 0; i < size; i++) {
                for (let j = 0; j < size; j++) {
                    const cell = grid[i][j];
                    if (cell) {
                        cell.centerPencilMarkColors = null;
                    }
                }
            }
        }

        let prevConsoleOutputTop = 0;
        let prevConsoleOutputHeight = 0;
        let trueCandidatesButton = null;
        let cancelButton = null;
        const doCancelableCommand = function(force) {
            if (!force && !this.hovering()) {
                return;
            }
            if (!solverSocket) {
                return this.origClick();
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
            return true;
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
                            clearPencilmarkColors();
                            lastSentPuzzle['truecandidates'] = null;
                            lastSentPuzzle['truecandidatescolored'] = null;
                        }
                        return true;
                    }
                }

                if (!solutionPathButton.origClick) {
                    solutionPathButton.origClick = solutionPathButton.click;
                    solutionPathButton.click = function() {
                        if (!this.hovering()) {
                            return;
                        }
                        if (!solverSocket) {
                            return this.origClick();
                        }

                        boolSettings['TrueCandidates'] = false;
                        boolSettings['EditGivenMarks'] = false;

                        forgetFutureChanges();
                        if (boolSettings['SimpleStep']) {
                            sendPuzzle('simplepath');
                        } else {
                            sendPuzzle('solvepath');
                        }
                        return true;
                    }
                }
                if (!stepButton.origClick) {
                    stepButton.origClick = stepButton.click;
                    stepButton.click = function() {
                        if (!this.hovering()) {
                            return;
                        }
                        if (!solverSocket) {
                            return this.origClick();
                        }
                        boolSettings['TrueCandidates'] = false;
                        boolSettings['EditGivenMarks'] = false;

                        forgetFutureChanges();
                        if (boolSettings['SimpleStep']) {
                            sendPuzzle('simplestep');
                        } else {
                            sendPuzzle('step');
                        }
                        return true;
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
                consoleSidebar.sections[0].y += (buttonLH + buttonGap);
                consoleSidebar.buttons.push(trueCandidatesButton);
            }

            if (prevConsoleOutputTop === 0) {
                prevConsoleOutputTop = consoleOutput.style.top;
                prevConsoleOutputHeight = consoleOutput.style.height;
            }
            consoleOutput.style.top = "45.6%";
            consoleOutput.style.height = "48%";
        }

        let hideSolverButtons = function() {
            if (!buttonsShown) {
                return;
            }
            buttonsShown = false;

            const consoleSidebar = sidebars.filter(sb => sb.title === 'Console')[0];
            if (consoleSidebar) {
                consoleSidebar.sections[0].y -= (buttonLH + buttonGap);

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

            clearPencilmarkColors();
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

        const lerpColor = function(a, b, amount) {
            const ar = a >> 16;
            const ag = a >> 8 & 0xff;
            const ab = a & 0xff;

            const br = b >> 16;
            const bg = b >> 8 & 0xff;
            const bb = b & 0xff;

            const rr = ar + amount * (br - ar);
            const rg = ag + amount * (bg - ag);
            const rb = ab + amount * (bb - ab);

            const colorStr = '000000' + (((rr << 16) + (rg << 8) + (rb | 0))).toString(16);
            return '#' + colorStr.substr(colorStr.length - 6);
        };

        const oneSolutionColor = "#299b20";
        const twoSolutionColor = 0xAFAFFF;
        const eightSolutionColor = 0x0000FF;
        const setCenterMarkColor = function(cell, numSolutionsStr, candidateIndex) {
            if (!numSolutionsStr) {
                return;
            }

            if (!cell.centerPencilMarkColors) {
                cell.centerPencilMarkColors = {};
            }
            const numSolutions = parseInt(numSolutionsStr[cell.i * size * size + cell.j * size + candidateIndex]);
            let curColor = oneSolutionColor;
            if (numSolutions > 1) {
                curColor = lerpColor(twoSolutionColor, eightSolutionColor, (numSolutions - 2) / 6);
            }
            cell.centerPencilMarkColors[candidateIndex + 1] = curColor;
        }

        let importCandidates = function(str, distinguished) {
            clearPencilmarkColors();

            if (size <= 9) {
                let candidateStr = str;
                let numSolutionsStr = null;
                const candidateStrLen = size * size * size;
                if (str.length === candidateStrLen * 2) {
                    candidateStr = str.substring(0, candidateStrLen);
                    numSolutionsStr = str.substring(candidateStrLen);
                }
                const numCells = candidateStr.length / size;
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
                            setCenterMarkColor(cell, numSolutionsStr, candidateIndex);
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
                let candidateStr = str;
                let numSolutionsStr = null;
                const candidateStrLen = size * size * size * 2;
                const numSolutionsStrLen = size * size * size;
                if (str.length === candidateStrLen + numSolutionsStrLen) {
                    candidateStr = str.substring(0, candidateStrLen);
                    numSolutionsStr = str.substring(candidateStrLen);
                }

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
                            setCenterMarkColor(cell, numSolutionsStr, candidateIndex);
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

        const handleInvalid = function(puzzle) {
            if (puzzle.startsWith('Invalid')) {
                const split = puzzle.split(':');
                if (split.length == 2) {
                    log(split[1]);
                } else {
                    log('Invalid board (no solutions).');
                }
                return true;
            }
            return false;
        }

        const handleTrueCandidates = function(puzzle) {
            if (handleInvalid(puzzle)) {
                clearGrid(false, true);
            } else {
                importCandidates(puzzle);
            }
        }
        const handleSolve = function(puzzle) {
            if (handleInvalid(puzzle)) {
                clearGrid(false, true);
            } else {
                importGivens(puzzle);
            }
            if (cancelButton) {
                cancelButton.title = cancelButton.origTitle;
                cancelButton = null;
            }
        }
        const handleCheck = function(puzzle) {
            let complete = false;
            if (puzzle.startsWith('final:')) {
                let count = puzzle.substring('final:'.length);
                if (count == 0) {
                    log('There are no solutions.');
                } else if (count == 1) {
                    log('There is a unique solution.');
                } else {
                    log('There are multiple solutions.');
                }
                complete = true;
            } else if (handleInvalid(puzzle)) {
                complete = true;
            }
            if (cancelButton && complete) {
                cancelButton.title = cancelButton.origTitle;
                cancelButton = null;
            }
        }
        const handleCount = function(puzzle) {
            let complete = false;
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
                complete = true;
            } else if (handleInvalid(puzzle)) {
                complete = true;
            }
            if (cancelButton && complete) {
                cancelButton.title = cancelButton.origTitle;
                cancelButton = null;
            }
        }
        const handlePath = function(puzzle) {
            if (!handleInvalid(puzzle)) {
                let colonIndex = puzzle.indexOf(':');
                if (colonIndex >= 0) {
                    let candidateString = puzzle.substring(0, colonIndex);
                    let description = puzzle.substring(colonIndex + 1);
                    importCandidates(candidateString);
                    log(description, { newLine: false });
                }
            }
        }
        const handleStep = function(puzzle) {
            if (!handleInvalid(puzzle)) {
                let colonIndex = puzzle.indexOf(':');
                if (colonIndex >= 0) {
                    let candidateString = puzzle.substring(0, colonIndex);
                    let description = puzzle.substring(colonIndex + 1);
                    importCandidates(candidateString, true);
                    log(description, { newLine: false });
                }
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
                        commandIsComplete = true;
                        if (lastCommand === 'truecandidates' || lastCommand === 'truecandidatescolored') {
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
            return true;
        }
        buttons.push(connectButton);

        const origDrawPopups = drawPopups;
        drawPopups = function(overlapSidebars) {
            origDrawPopups(overlapSidebars);

            if (overlapSidebars && popup === 'solversettings') {
                const box = popups[cID(popup)];

                ctx.lineWidth = lineWW;
                ctx.fillStyle = boolSettings['Dark Mode'] ? '#404040' : '#E0E0E0';
                ctx.strokeStyle = '#000000';
                ctx.fillRect(canvas.width / 2 - box.w / 2, canvas.height / 2 - box.h / 2, box.w, 90);
                ctx.strokeRect(canvas.width / 2 - box.w / 2, canvas.height / 2 - box.h / 2, box.w, 90);

                ctx.fillStyle = boolSettings['Dark Mode'] ? '#F0F0F0' : '#000000';
                ctx.font = '60px Arial';
                ctx.fillText('Solver Settings', canvas.width / 2, canvas.height / 2 - box.h / 2 + 66);
            }
        }

        settingsButton.click = function() {
            if (!this.hovering()) {
                return;
            }

            togglePopup('solversettings');
            return true;
        }
        buttons.push(settingsButton);

        const settingsButtons = [{
                id: 'SimpleStep',
                label: 'Simple Logic Only'
            },
            {
                id: 'ColoredCandidates',
                label: 'Colored True Candidates',
                click: function() {
                    if (!this.hovering()) {
                        return;
                    }

                    this.origClick();

                    if (boolSettings['TrueCandidates']) {
                        clearPencilmarkColors();
                        lastSentPuzzle['truecandidates'] = null;
                        lastSentPuzzle['truecandidatescolored'] = null;
                        sendPuzzle('truecandidates');
                    }
                    return true;
                }
            },
            {
                id: 'EditGivenMarks',
                label: 'Edit Given Pencilmarks'
            },
        ];
        popups.solversettings = { w: 600, h: 125 + (buttonSH + buttonGap) * settingsButtons.length };
        const closeSettingsButton = new button(canvas.width / 2 + popups[cID('solversettings')].w / 2, canvas.height / 2 - popups[cID('solversettings')].h / 2 - 20, 40, 40, ['solversettings'], 'X', 'X');
        buttons.push(closeSettingsButton);

        var numSettingsButtons = 0;
        for (let buttonData of settingsButtons) {
            const newButton = new button(canvas.width / 2 - (buttonSH + buttonGap) / 2, canvas.height / 2 - popups[cID('solversettings')].h / 2 + 110 + (buttonSH + buttonGap) * numSettingsButtons, 450, buttonSH, ['solversettings'], buttonData.id, buttonData.label);
            boolSettings.push(buttonData.id);
            boolSettings[buttonData.id] = false;
            extraSettingsNames.push(buttonData.id);
            buttons.push(newButton);
            if (buttonData.click) {
                newButton.origClick = newButton.click;
                newButton.click = function() {
                    if (!this.hovering()) {
                        return;
                    }

                    this.origClick();

                    if (boolSettings['TrueCandidates']) {
                        clearPencilmarkColors();
                        lastSentPuzzle['truecandidates'] = null;
                        lastSentPuzzle['truecandidatescolored'] = null;
                        sendPuzzle('truecandidates');
                    }
                    return true;
                }
            }
            numSettingsButtons++;
        }

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
            for (let i = 0; i < extraSettingsNames.length; i++) {
                let name = extraSettingsNames[i];
                let index = boolSettings.indexOf(name);
                if (index > -1) {
                    boolSettings.splice(index, 1);
                }
            }
            origCreateOtherButtons();
            for (let i = 0; i < extraSettingsNames.length; i++) {
                let name = extraSettingsNames[i];
                boolSettings.push(name);
            }

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
            return true;
        }

        openSudokuLabButton.click = function() {
            if (!this.hovering()) {
                return;
            }
            window.open('https://www.sudokulab.net/?fpuzzle=' + exportPuzzle());
            return true;
        }

        buttons.filter(b => b.modes.includes('Export')).forEach(b => b.y -= 90);
        document.getElementById('previewTypeBox').style.top = '29%';

        // Replace cell rendering to support colors
        const origCell = cell;
        cell = function(i, j, outside) {
            const c = new origCell(i, j, outside);

            c.origEnterSS = c.enter;
            c.enter = function(value, forced, isLast) {
                if (forced || this.given || !boolSettings['EditGivenMarks'] || tempEnterMode !== 'Center') {
                    this.origEnterSS(value, forced, isLast);
                    return;
                }

                value = parseInt(value);
                if (value <= size) {
                    if (!value) {
                        this.givenPencilMarks = [];
                    } else {
                        if (!this.givenPencilMarks) {
                            this.givenPencilMarks = [];
                        }
                        if (this.givenPencilMarks.includes(value)) {
                            this.givenPencilMarks.splice(this.givenPencilMarks.indexOf(value), 1);
                        } else {
                            this.givenPencilMarks.push(value);
                        }
                        this.givenPencilMarks.sort((a, b) => a - b);
                    }
                }
            }

            c.origShowTopSS = c.showTop;
            c.showTop = function() {
                const defaultFillStyle = boolSettings['Dark Mode'] ? '#FFFFFF' : '#000000';
                const givenMarkFillStyle = boolSettings['Dark Mode'] ? '#FF8080' : '#800000';

                const haveGivenMarks = (currentTool !== 'Regions' && !this.given && this.givenPencilMarks && this.givenPencilMarks.length > 0);

                if (currentTool === 'Regions' && !previewMode || this.value) {
                    this.origShowTopSS();
                } else if (!previewMode) {
                    const TLInterference = constraints[cID('Killer Cage')].some(a => a.value.length && a.cells[0] === this) ||
                        cosmetics[cID('Cage')].some(a => a.value.length && a.cells[0] === this) ||
                        constraints[cID('Quadruple')].some(a => a.cells[3] === this);
                    const TRInterference = constraints[cID('Quadruple')].some(a => a.cells[2] === this);
                    const BLInterference = constraints[cID('Quadruple')].some(a => a.cells[1] === this);
                    const BRInterference = constraints[cID('Quadruple')].some(a => a.cells[0] === this);

                    ctx.fillStyle = boolSettings['Dark Mode'] ? '#F0F0F0' : '#000000';
                    ctx.font = (cellSL * 0.19 * textScale) + 'px Arial';
                    for (var a = 0; a < Math.min(4, this.cornerPencilMarks.length); a++) {
                        var x = this.x + (cellSL * 0.14) + (cellSL * 0.70 * (a % 2));
                        if ((a === 0 && TLInterference) || (a === 2 && BLInterference))
                            x += cellSL * 0.285;
                        if ((a === 1 && TRInterference) || (a === 3 && BRInterference))
                            x -= cellSL * 0.285;
                        var y = this.y + (cellSL * 0.25) + (cellSL * 0.64 * (a > 1));
                        ctx.fillText(this.cornerPencilMarks[a], x, y);
                    }

                    const centerMarkOffsetY = haveGivenMarks ? 0.4 : 0.6;
                    const centerPencilMarks = Array(Math.ceil(this.centerPencilMarks.length / 5)).fill().map((a, i) => i * 5).map(a => this.centerPencilMarks.slice(a, a + 5));
                    ctx.font = `${(cellSL * 0.19 * textScale)}px Arial`;
                    if (!this.centerPencilMarkColors) {
                        ctx.fillStyle = defaultFillStyle;
                        for (let a = 0; a < centerPencilMarks.length; a++) {
                            ctx.fillText(centerPencilMarks[a].join(''), this.x + cellSL / 2, this.y + (cellSL * centerMarkOffsetY) - ((centerPencilMarks.length - 1) / 2 - a) * cellSL * 0.1666 * textScale);
                        }
                    } else {
                        const deltaX = (cellSL * 0.19 * textScale) * (5.0 / 9.0);
                        for (let a = 0; a < centerPencilMarks.length; a++) {
                            const curMarks = centerPencilMarks[a];
                            const x = this.x + cellSL / 2;
                            const y = this.y + (cellSL * centerMarkOffsetY) - ((centerPencilMarks.length - 1) / 2 - a) * cellSL * 0.1666 * textScale;
                            for (let m = 0; m < curMarks.length; m++) {
                                let v = curMarks[m];
                                const color = this.centerPencilMarkColors[v];
                                ctx.fillStyle = color ? color : defaultFillStyle;
                                ctx.fillText(v, x + (m - (curMarks.length - 1) / 2) * deltaX, y);
                            }
                        }
                    }
                }

                if (haveGivenMarks) {
                    ctx.font = `bold ${(cellSL * 0.19 * textScale)}px  Arial`;
                    const centerPencilMarks = Array(Math.ceil(this.givenPencilMarks.length / 5)).fill().map((a, i) => i * 5).map(a => this.givenPencilMarks.slice(a, a + 5));
                    ctx.fillStyle = givenMarkFillStyle;
                    for (let a = 0; a < centerPencilMarks.length; a++) {
                        ctx.fillText(centerPencilMarks[a].join(''), this.x + cellSL / 2, this.y + (cellSL * 0.85) - ((centerPencilMarks.length - 1) / 2 - a) * cellSL * 0.1666 * textScale);
                    }
                }
            }

            return c;
        }

        // Additional import/export data
        const origExportPuzzle = exportPuzzle;
        exportPuzzle = function(includeCandidates) {
            const compressed = origExportPuzzle(includeCandidates);
            const puzzle = JSON.parse(compressor.decompressFromBase64(compressed));
            for (let i = 0; i < size; i++) {
                for (let j = 0; j < size; j++) {
                    const cell = window.grid[i][j];
                    puzzle.grid[i][j].givenPencilMarks = cell.givenPencilMarks && cell.givenPencilMarks.length > 0 ? cell.givenPencilMarks : null;
                }
            }
            return compressor.compressToBase64(JSON.stringify(puzzle));
        }

        const origImportPuzzle = importPuzzle;
        importPuzzle = function(string, clearHistory) {
            origImportPuzzle(string, clearHistory);

            const puzzle = JSON.parse(compressor.decompressFromBase64(string));
            for (let i = 0; i < size; i++) {
                for (let j = 0; j < size; j++) {
                    if (puzzle.grid[i][j].givenPencilMarks && puzzle.grid[i][j].givenPencilMarks.length > 0) {
                        grid[i][j].givenPencilMarks = puzzle.grid[i][j].givenPencilMarks;
                    }
                }
            }

            if (clearHistory) {
                generateCandidates();
                resetKnownPuzzleInformation();
                clearChangeHistory();
            }
        }
    }

    if (window.grid) {
        doShim();
    } else {
        document.addEventListener('DOMContentLoaded', (event) => {
            doShim();
        });
    }
})();