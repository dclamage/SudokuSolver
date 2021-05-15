// ==UserScript==
// @name         Fpuzzles-SudokuSolver
// @namespace    http://tampermonkey.net/
// @version      0.1.1-alpha
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
    window.sendPuzzle = function() {
        if (window.solverSocket) {
            var puzzle = exportPuzzle();
            if (window.lastSentPuzzle !== puzzle) {
                nonce = nonce + 1;
                window.solverSocket.send('fpuzzles:' + nonce + ':' + puzzle);
                window.lastSentPuzzle = puzzle;
            }
            connectButton.title = "Calculating...";
        }
    }

    window.importCandidates = function(str) {
        if (changeIndex < changes.length - 1) {
            // Undo has been pressed
            return;
        }

        const numCells = str.length / size;
        if (size <= 9) {
            for (let i = 0; i < numCells; i++) {
                const cell = grid[Math.floor(i / size)][i % size];
                if (cell.given) {
                    continue;
                }
                const candidates = [];
                for (let candidateIndex = 0; candidateIndex < size; candidateIndex++) {
                    const candidate = parseInt(str[i * size + candidateIndex]);
                    if (!isNaN(candidate) && candidate != 0) {
                        candidates.push(candidate);
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
            }
        } else {
            for (let i = 0; i < numCells; i++) {
                const cell = grid[Math.floor(i / size)][i % size];
                if (cell.given) {
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

                cell.value = 0;
                cell.centerPencilMarks = [];
                if (candidates.length == 1) {
                    cell.value = candidates[0];
                } else {
                    cell.centerPencilMarks = candidates;
                }
            }
        }
        onInputEnd();
    }

    connectButton.click = function() {
        if (!this.hovering()) return;

        if (!window.solverSocket) {
            connectButton.title = 'Connecting...';

            let socket = new WebSocket("ws://localhost:4545");
            socket.onopen = function() {
                console.log("Connection succeeded");
                window.sendPuzzle();
            };

            socket.onmessage = function(msg) {
                let expectedNonce = nonce + ':';
                if (msg.data.startsWith(expectedNonce)) {
                    let puzzle = msg.data.substring(expectedNonce.length);
                    if (puzzle === 'Invalid') {
                        clearGrid(false, true);
                        connectButton.title = 'INVALID';
                    } else {
                        connectButton.title = 'Disconnect';
                        window.importCandidates(puzzle);
                    }
                }
            };

            socket.onclose = function() {
                connectButton.title = 'Connect';
                console.log("Connection closed");
                window.solverSocket = null;
                window.lastSentPuzzle = null;
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
                window.sendPuzzle();
                return true;
            },
            set: function(target, property, value, receiver) {
                target[property] = value;
                window.sendPuzzle();
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