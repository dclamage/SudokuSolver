// ==UserScript==
// @name         Fpuzzles-SudokuSolver
// @namespace    http://tampermonkey.net/
// @version      0.3.1-alpha
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

        const allowCommandWhenUndo = [];
        allowCommandWhenUndo['check'] = true;
        allowCommandWhenUndo['count'] = true;

        let extraSettingsNames = [];
        extraSettingsNames.push('TrueCandidates');

        let solverSocket = null;
        let commandIsComplete = false;

        const sendPuzzleDelayed = function(message) {
            if (nonce === message.nonce) {
                solverSocket.send(JSON.stringify(message));
            }
        }

        const sendPuzzle = function(command) {
            if (!solverSocket) {
                return;
            }
            nonce++;

            if (command === "cancel") {
                const message = {
                    nonce: nonce,
                    command: 'cancel',
                }
                solverSocket.send(JSON.stringify(message));
                return;
            }

            var puzzle = exportPuzzle();
            const message = {
                nonce: nonce,
                command: command,
                dataType: 'fpuzzles',
                data: puzzle
            }
            setTimeout(() => sendPuzzleDelayed(message), 250);

            connectButton.title = "Calculating...";

            if (command === 'solve' ||
                command === 'check' ||
                command === 'count' ||
                command === 'solvepath' ||
                command === 'step' && lastClearCommand !== 'step') {
                clearConsole();
                lastClearCommand = command;
            }
            lastCommand = command;
        }

        const clearPencilmarkColors = function() {
            for (let i = 0; i < size; i++) {
                for (let j = 0; j < size; j++) {
                    const cell = grid[i][j];
                    if (cell) {
                        cell.centerPencilMarkColors = null;
                        cell.tcerror = false;
                    }
                }
            }
        }

        const clearTCError = function() {
            for (let i = 0; i < size; i++) {
                for (let j = 0; j < size; j++) {
                    const cell = grid[i][j];
                    if (cell) {
                        cell.tcerror = false;
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
                    defaultSettings.push(false);
                    trueCandidatesButton = new button(solutionPathButton.x - buttonLH / 2 - buttonGap / 2, solutionPathButton.y + (buttonLH + buttonGap) * 4, buttonW - buttonLH - buttonGap, buttonLH, ['Solving', 'Setting'], 'TrueCandidates', 'True Candid.')
                    trueCandidatesButton.origClick = trueCandidatesButton.click;
                    trueCandidatesButton.click = function() {
                        if (!this.hovering()) {
                            return;
                        }

                        this.origClick();

                        if (boolSettings['TrueCandidates']) {
                            clearTCError();
                            sendPuzzle('truecandidates');
                        } else {
                            clearPencilmarkColors();
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
                        sendPuzzle('solvepath');
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
                        sendPuzzle('step');
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

        const baselineColor = "#000000";
        const zeroSolutionColor = "#CC0000";
        const oneSolutionColor = "#299b20";
        const twoSolutionColor = 0xAFAFFF;
        const eightSolutionColor = 0x0000FF;
        const setCenterMarkColor = function(cell, numSolutions, candidateIndex) {
            if (!cell.centerPencilMarkColors) {
                cell.centerPencilMarkColors = [];
            }
            let curColor = oneSolutionColor;
            if (numSolutions < 0) {
                curColor = baselineColor;
            } else if (numSolutions === 0) {
                curColor = zeroSolutionColor;
            } else if (numSolutions > 1) {
                curColor = lerpColor(twoSolutionColor, eightSolutionColor, Math.min(6, numSolutions - 2) / 6);
            }
            cell.centerPencilMarkColors[candidateIndex + 1] = curColor;
        }

        const importCandidates = function(response) {
            clearPencilmarkColors();

            if (response.type === 'truecandidates') {
                const solutions = response.solutionsPerCandidate;
                const colored = boolSettings['ColoredCandidates'];
                for (let i = 0; i < size; i++) {
                    for (let j = 0; j < size; j++) {
                        const cell = grid[i][j];
                        if (!cell || cell.given) {
                            continue;
                        }

                        const cellIndex = i * size + j;
                        const candidates = [];
                        for (let candidateIndex = 0; candidateIndex < size; candidateIndex++) {
                            const numSolutions = solutions[cellIndex * size + candidateIndex];
                            if (numSolutions !== 0) {
                                candidates.push(candidateIndex + 1);
                                if (colored) {
                                    setCenterMarkColor(cell, numSolutions, candidateIndex);
                                }
                            }
                        }

                        cell.value = 0;
                        cell.centerPencilMarks = [];
                        cell.candidates = candidates;
                        if (candidates.length == 1) {
                            cell.value = candidates[0];
                        } else {
                            cell.centerPencilMarks = candidates;
                        }
                        cell.tcerror = false;
                    }
                }
            } else if (response.type === 'logical') {
                const responseCells = response.cells;
                for (let i = 0; i < size; i++) {
                    for (let j = 0; j < size; j++) {
                        const cell = grid[i][j];
                        if (!cell || cell.given) {
                            continue;
                        }

                        const cellIndex = i * size + j;
                        const responseCell = responseCells[cellIndex];
                        if (responseCell.value != 0) {
                            cell.value = responseCell.value;
                            cell.centerPencilMarks = [];
                            cell.candidates = [responseCell.value];
                        } else {
                            cell.value = 0;
                            cell.centerPencilMarks = responseCell.candidates;
                            cell.candidates = responseCell.candidates;
                        }
                        cell.centerPencilMarkColors = null;
                        cell.tcerror = false;
                    }
                }
            }

            onInputEnd();
        }

        const importGivens = function(response) {
            if (response.type === 'solved') {
                const solution = response.solution;
                for (let i = 0; i < size; i++) {
                    for (let j = 0; j < size; j++) {
                        const cell = grid[i][j];
                        if (!cell || cell.given) {
                            continue;
                        }

                        const cellIndex = i * size + j;
                        cell.value = solution[cellIndex];
                        cell.centerPencilMarks = [];
                        cell.candidates = [cell.value];
                        cell.centerPencilMarkColors = null;
                        cell.tcerror = false;
                    }
                }
            }

            onInputEnd();
        }

        const clearCandidates = function() {
            for (let i = 0; i < size; i++) {
                for (let j = 0; j < size; j++) {
                    const cell = grid[i][j];
                    if (!cell || cell.given) {
                        continue;
                    }
                    cell.value = 0;
                    cell.centerPencilMarks = [];
                    cell.centerPencilMarkColors = null;
                    cell.tcerror = true;
                }
            }
        }

        const handleInvalid = function(response) {
            if (response.type === 'invalid') {
                if (response.message && response.message.length > 0) {
                    log(response.message);
                } else {
                    log('Invalid board (no solutions).');
                }
                return true;
            }
            return false;
        }

        const handleTrueCandidates = function(response) {
            if (handleInvalid(response)) {
                clearCandidates();
            } else {
                importCandidates(response);
            }
        }
        const handleSolve = function(response) {
            if (handleInvalid(response)) {
                clearCandidates();
            } else {
                importGivens(response);
            }
            if (cancelButton) {
                cancelButton.title = cancelButton.origTitle;
                cancelButton = null;
            }
        }
        const handleCheck = function(response) {
            let complete = false;
            if (response.type === 'count') {
                if (!response.inProgress) {
                    const count = response.count;
                    if (count == 0) {
                        log('There are no solutions.');
                    } else if (count == 1) {
                        log('There is a unique solution.');
                    } else {
                        log('There are multiple solutions.');
                    }
                    complete = true;
                }
            } else if (handleInvalid(response)) {
                complete = true;
            }
            if (cancelButton && complete) {
                cancelButton.title = cancelButton.origTitle;
                cancelButton = null;
            }
        }

        const handleCount = function(response) {
            let complete = false;
            if (response.type === 'count') {
                const count = response.count;
                if (response.inProgress) {
                    clearConsole();
                    log('Found ' + count + ' solutions so far...');
                    commandIsComplete = false;
                } else {
                    clearConsole();
                    if (count == 0) {
                        log('There are no solutions.');
                    } else if (count == 1) {
                        log('There is a unique solution.');
                    } else {
                        log('There are exactly ' + count + ' solutions.');
                    }
                    complete = true;
                }
            } else if (handleInvalid(response)) {
                complete = true;
            }
            if (cancelButton && complete) {
                cancelButton.title = cancelButton.origTitle;
                cancelButton = null;
            }
        }

        const handlePath = function(response) {
            if (!handleInvalid(response)) {
                importCandidates(response);
                log(response.message, { newLine: false });
            }
        }

        const handleStep = function(response) {
            if (!handleInvalid(response)) {
                importCandidates(response);
                log(response.message, { newLine: false });
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
                    const response = JSON.parse(msg.data);
                    if (response.nonce === nonce) {
                        if (!allowCommandWhenUndo[lastCommand] && changeIndex < changes.length - 1) {
                            // Undo has been pressed
                            return;
                        }

                        if (response.type === 'canceled') {
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
                        if (lastCommand === 'truecandidates') {
                            handleTrueCandidates(response);
                        } else if (lastCommand === 'solve') {
                            handleSolve(response);
                        } else if (lastCommand === 'check') {
                            handleCheck(response);
                        } else if (lastCommand === 'count') {
                            handleCount(response);
                        } else if (lastCommand === 'solvepath') {
                            handlePath(response);
                        } else if (lastCommand === 'step') {
                            handleStep(response);
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

        const trueCandidatesOptionChanged = function() {
            if (!this.hovering()) {
                return;
            }

            this.origClickSS();

            if (boolSettings['TrueCandidates']) {
                clearPencilmarkColors();
                sendPuzzle('truecandidates');
            }
            return true;
        };

        const settingsButtons = [{
                heading: 'Logical Solve Settings'
            },
            {
                id: 'EnableLogicTuples',
                label: 'Tuples',
                default: true,
                click: trueCandidatesOptionChanged,
            },
            {
                id: 'EnableLogicPointing',
                label: 'Pointing',
                default: true,
                click: trueCandidatesOptionChanged,
            },
            {
                id: 'EnableLogicFishes',
                label: 'Fishes',
                default: true,
                click: trueCandidatesOptionChanged,
            },
            {
                id: 'EnableLogicWings',
                label: 'Wings',
                default: true,
                click: trueCandidatesOptionChanged,
            },
            {
                id: 'EnableLogicContradictions',
                label: 'Contradictions',
                default: true,
                click: trueCandidatesOptionChanged,
            },
            {
                heading: 'True Candidates Settings'
            },
            {
                id: 'ColoredCandidates',
                label: 'Solution Count',
                default: false,
                click: trueCandidatesOptionChanged,
            },
            {
                id: 'LogicalCandidates',
                label: 'Include Logical Candidates',
                default: false,
                click: trueCandidatesOptionChanged,
            },
            {
                heading: 'Input Settings'
            },
            {
                id: 'EditGivenMarks',
                label: 'Edit Given Pencilmarks',
                default: false,
            },
        ];

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

                ctx.fillStyle = boolSettings['Dark Mode'] ? '#F0F0F0' : '#000000';
                ctx.font = `bold ${Math.floor(buttonSH)}px Arial`;
                const offsetX = canvas.width / 2 - (buttonSH + buttonGap) / 2;
                const offsetY = canvas.height / 2 - popups[cID('solversettings')].h / 2 + 135;
                let numSettingsButtons = 0;
                for (let buttonData of settingsButtons) {
                    if (buttonData.heading) {
                        ctx.fillText(buttonData.heading, offsetX, offsetY + (buttonSH + buttonGap) * numSettingsButtons);
                    }
                    numSettingsButtons++;
                }
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

        popups.solversettings = { w: 600, h: 125 + (buttonSH + buttonGap) * settingsButtons.length };
        const closeSettingsButton = new button(canvas.width / 2 + popups[cID('solversettings')].w / 2, canvas.height / 2 - popups[cID('solversettings')].h / 2 - 20, 40, 40, ['solversettings'], 'X', 'X');
        buttons.push(closeSettingsButton);

        let numSettingsButtons = 0;
        for (let buttonData of settingsButtons) {
            if (!buttonData.heading) {
                const newButton = new button(canvas.width / 2 - (buttonSH + buttonGap) / 2, canvas.height / 2 - popups[cID('solversettings')].h / 2 + 110 + (buttonSH + buttonGap) * numSettingsButtons, 450, buttonSH, ['solversettings'], buttonData.id, buttonData.label);
                boolSettings.push(buttonData.id);
                defaultSettings.push(buttonData.default ? true : false);
                boolSettings[buttonData.id] = buttonData.default ? true : false;
                extraSettingsNames.push(buttonData.id);
                buttons.push(newButton);
                if (buttonData.click) {
                    newButton.origClickSS = newButton.click;
                    newButton.click = buttonData.click;
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
                defaultSettings.push(false);
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

                if (boolSettings['TrueCandidates'] && this.tcerror && this.value === 0 && this.centerPencilMarks.length === 0 && !this.given) {
                    ctx.font = `bold ${(cellSL * 0.8)}px  Arial`;
                    ctx.fillStyle = '#FF000080';
                    ctx.fillText('X', this.x + cellSL / 2, this.y + (cellSL * 0.8));
                }
            }

            return c;
        }

        // Additional import/export data
        const enableLogicPrefix = 'enablelogic';
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

            puzzle.disabledlogic = [];
            for (let settingName of boolSettings) {
                const settingNameLower = settingName.toLowerCase();
                if (settingNameLower.startsWith(enableLogicPrefix) && !boolSettings[settingName]) {
                    puzzle.disabledlogic.push(settingNameLower.substr(enableLogicPrefix.length));
                }
            }

            puzzle.truecandidatesoptions = [];
            if (boolSettings['ColoredCandidates']) {
                puzzle.truecandidatesoptions.push('colored');
            }
            if (boolSettings['LogicalCandidates']) {
                puzzle.truecandidatesoptions.push('logical');
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