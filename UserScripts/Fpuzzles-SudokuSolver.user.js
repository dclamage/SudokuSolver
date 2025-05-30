// ==UserScript==
// @name         Fpuzzles-SudokuSolver
// @namespace    http://tampermonkey.net/
// @version      1.2.0
// @description  Connect f-puzzles to SudokuSolver
// @author       Rangsk
// @match        https://*.f-puzzles.com/*
// @match        https://f-puzzles.com/*
// @icon         data:image/gif;base64,R0lGODlhAQABAAAAACH5BAEKAAEALAAAAAABAAEAAAICTAEAOw==
// @grant        none
// @run-at       document-end
// ==/UserScript==

(function () {
  const doShim = function () {
    "use strict";

    let textScale = 1.5;
    const settingsIcon = "\u2699\uFE0F";

    const connectButtonOffset = 208;
    const connectButton = new button(
      canvas.width - connectButtonOffset,
      40,
      215,
      40,
      ["Setting", "Solving"],
      "Connect",
      "Connect"
    );
    const settingsButton = new button(
      canvas.width - 65,
      40,
      40,
      40,
      ["Setting", "Solving"],
      settingsIcon,
      settingsIcon
    );

    let nonce = 0;
    let lastCommand = "";
    let lastClearCommand = "";

    const allowCommandWhenUndo = [];
    allowCommandWhenUndo["check"] = true;
    allowCommandWhenUndo["count"] = true;
    // "estimate" is ongoing, so undo might not be directly relevant to aborting server-side, but good to allow response processing.
    allowCommandWhenUndo["estimate"] = true;

    let extraSettingsNames = [];
    extraSettingsNames.push("TrueCandidates");

    let solverSocket = null;
    let commandIsComplete = false;

    // UI Elements for custom buttons
    let trueCandidatesButton = null;
    let estimateCountButton = null;

    // For layout adjustments
    let initialConsoleSection0Y = 0;
    let prevConsoleOutputTop = 0;
    let prevConsoleOutputHeight = 0;

    const handleSocketSendError = function (operationName = "send", error) {
      console.error(
        `Error during WebSocket ${operationName}:`,
        error || "Unknown error"
      );
      if (solverSocket) {
        const currentOnClose = solverSocket.onclose;
        if (currentOnClose) {
          solverSocket.onclose = null;
          currentOnClose();
        } else {
          connectButton.title = "Connect";
          solverSocket = null;
          hideSolverButtons();
          if (cancelButton) {
            cancelButton.title = cancelButton.origTitle;
            cancelButton = null;
          }
        }
      }
      connectButton.title = "Connect";
      if (cancelButton) {
        cancelButton.title = cancelButton.origTitle;
        cancelButton = null;
      }
    };

    const exportPuzzleForSolving = function (includeCandidates) {
      const compressed = exportPuzzle(includeCandidates);
      const puzzle = JSON.parse(compressor.decompressFromBase64(compressed));
      puzzle.disabledlogic = [];
      for (let settingName of boolSettings) {
        const settingNameLower = settingName.toLowerCase();
        if (
          settingNameLower.startsWith(enableLogicPrefix) &&
          !boolSettings[settingName]
        ) {
          puzzle.disabledlogic.push(
            settingNameLower.substr(enableLogicPrefix.length)
          );
        }
      }
      puzzle.truecandidatesoptions = [];
      if (boolSettings["ColoredCandidates"])
        puzzle.truecandidatesoptions.push("colored");
      if (boolSettings["LogicalCandidates"])
        puzzle.truecandidatesoptions.push("logical");
      return compressor.compressToBase64(JSON.stringify(puzzle));
    };

    const sendPuzzleDelayed = function (message) {
      if (nonce === message.nonce) {
        if (solverSocket) {
          try {
            solverSocket.send(JSON.stringify(message));
          } catch (e) {
            handleSocketSendError("delayed send", e);
          }
        } else {
          console.log(
            "Socket closed before delayed send for nonce:",
            message.nonce
          );
          if (connectButton.title === "Calculating...")
            connectButton.title = "Connect";
          if (
            cancelButton &&
            (cancelButton.title === "Cancel" ||
              cancelButton.title === "Cancelling...")
          ) {
            cancelButton.title = cancelButton.origTitle;
          }
        }
      }
    };

    const sendPuzzle = function (command) {
      if (!solverSocket && command !== "cancel") {
        console.log("Attempted to send command but not connected:", command);
        if (connectButton.title === "Calculating...")
          connectButton.title = "Connect";
        if (
          cancelButton &&
          (cancelButton.title === "Cancel" ||
            cancelButton.title === "Cancelling...")
        ) {
          cancelButton.title = cancelButton.origTitle;
        }
        return;
      }
      nonce++;

      if (command === "cancel") {
        const message = { nonce: nonce, command: "cancel" };
        if (solverSocket) {
          try {
            solverSocket.send(JSON.stringify(message));
          } catch (e) {
            handleSocketSendError("cancel send", e);
          }
        } else {
          console.log("Socket closed, cannot send cancel.");
        }
        return;
      }

      var puzzle = exportPuzzleForSolving();
      const message = {
        nonce: nonce,
        command: command,
        dataType: "fpuzzles",
        data: puzzle,
      };
      setTimeout(() => sendPuzzleDelayed(message), 250);

      connectButton.title = "Calculating...";
      commandIsComplete = false;

      if (
        command === "solve" ||
        command === "check" ||
        command === "count" ||
        command === "solvepath" ||
        command === "step" ||
        command === "estimate"
      ) {
        if (lastClearCommand !== command || command === "estimate") {
          // Estimate clears each time
          clearConsole();
          lastClearCommand = command;
        }
      }
      lastCommand = command;
    };

    const clearPencilmarkColors = function () {
      /* ... same ... */
      for (let i = 0; i < size; i++)
        for (let j = 0; j < size; j++) {
          const cell = grid[i][j];
          if (cell) {
            cell.centerPencilMarkColors = null;
            cell.tcerror = false;
          }
        }
    };
    const clearTCError = function () {
      /* ... same ... */
      for (let i = 0; i < size; i++)
        for (let j = 0; j < size; j++) {
          const cell = grid[i][j];
          if (cell) cell.tcerror = false;
        }
    };

    let cancelButton = null;
    const doCancelableCommand = function (force) {
      if (!force && !this.hovering()) return;
      if (!solverSocket) return this.origClick();
      if (this.solverCommand !== "truecandidates")
        boolSettings["TrueCandidates"] = false;

      if (cancelButton === this) {
        if (this.title !== "Cancelling...") {
          sendPuzzle("cancel");
          this.title = "Cancelling...";
        }
      } else {
        if (
          cancelButton &&
          cancelButton !== this &&
          (cancelButton.title === "Cancel" ||
            cancelButton.title === "Cancelling...")
        ) {
          cancelButton.title = cancelButton.origTitle;
        }
        sendPuzzle(this.solverCommand);
        this.title = "Cancel";
        cancelButton = this;
      }
      return true;
    };

    let initCancelableButton = function (button, command) {
      if (!button.origClick) {
        button.origClick = button.click;
        button.click = doCancelableCommand;
      }
      if (!button.origTitle) button.origTitle = button.title;
      if (button.solverCommand === undefined) button.solverCommand = command;

      if (cancelButton && cancelButton.solverCommand === command) {
        button.title = cancelButton.title;
        if (cancelButton !== button) cancelButton = button;
      } else if (
        button.title === "Cancel" ||
        button.title === "Cancelling..."
      ) {
        button.title = button.origTitle;
      }
    };

    let hookSolverButtons = function () {
      const consoleSidebar = sidebars.filter((sb) => sb.title === "Console")[0];
      const mainSidebar = sidebars.filter((sb) => sb.title === "Main")[0];

      if (consoleSidebar && mainSidebar) {
        const solutionPathButton = consoleSidebar.buttons.filter(
          (b) => b.title === "Solution Path"
        )[0];
        const stepButton = consoleSidebar.buttons.filter(
          (b) => b.title === "Step"
        )[0];
        const checkButton = consoleSidebar.buttons.filter(
          (b) => b.title === "Check"
        )[0];
        const countButton = consoleSidebar.buttons.filter(
          (b) => b.title === "Solution Count"
        )[0];
        const solveButton = mainSidebar.buttons.filter(
          (b) => b.title === "Solve"
        )[0];

        if (
          !solutionPathButton ||
          !stepButton ||
          !checkButton ||
          !countButton ||
          !solveButton
        ) {
          console.warn(
            "Fpuzzles-SudokuSolver: Could not find all expected f-puzzles solver buttons to hook."
          );
          return;
        }

        // Estimate Count Button
        if (!estimateCountButton) {
          estimateCountButton = new button(
            countButton.x,
            countButton.y + buttonLH + buttonGap,
            buttonW,
            buttonLH,
            ["Solving", "Setting"],
            "Estimate Count",
            "Estimate Count"
          );
        }
        // Ensure properties are set even if button object was persisted
        estimateCountButton.x = countButton.x;
        estimateCountButton.y = countButton.y + buttonLH + buttonGap;
        estimateCountButton.w = buttonW;
        estimateCountButton.h = buttonLH;
        estimateCountButton.modes = ["Solving", "Setting"];
        initCancelableButton(estimateCountButton, "estimate");

        // True Candidates Button
        if (!trueCandidatesButton) {
          boolSettings.push("TrueCandidates");
          boolSettings["TrueCandidates"] = false;
          defaultSettings.push(false);
          trueCandidatesButton = new button(
            estimateCountButton.x - buttonLH / 2 - buttonGap / 2, // Original x relative to container
            estimateCountButton.y + buttonLH + buttonGap,
            buttonW - buttonLH - buttonGap,
            buttonLH,
            ["Solving", "Setting"],
            "TrueCandidates",
            "True Candid."
          );
          trueCandidatesButton.origClick = trueCandidatesButton.click; // Default f-puzzles click
          trueCandidatesButton.click = function () {
            // Our override
            if (!this.hovering()) return;
            this.origClick(); // Toggles boolSettings['TrueCandidates']
            if (boolSettings["TrueCandidates"]) {
              clearTCError();
              sendPuzzle("truecandidates");
            } else {
              clearPencilmarkColors();
            }
            return true;
          };
        }
        // Ensure properties are set
        trueCandidatesButton.x =
          estimateCountButton.x - buttonLH / 2 - buttonGap / 2;
        trueCandidatesButton.y = estimateCountButton.y + buttonLH + buttonGap;
        trueCandidatesButton.w = buttonW - buttonLH - buttonGap;
        trueCandidatesButton.h = buttonLH;
        trueCandidatesButton.modes = ["Solving", "Setting"];

        // Hook existing f-puzzles buttons
        if (!solutionPathButton.origClick) {
          solutionPathButton.origClick = solutionPathButton.click;
          solutionPathButton.click = function () {
            if (!this.hovering()) return;
            if (!solverSocket) return this.origClick();
            boolSettings["TrueCandidates"] = false;
            boolSettings["EditGivenMarks"] = false;
            forgetFutureChanges();
            sendPuzzle("solvepath");
            return true;
          };
        }
        if (!stepButton.origClick) {
          stepButton.origClick = stepButton.click;
          stepButton.click = function () {
            if (!this.hovering()) return;
            if (!solverSocket) return this.origClick();
            boolSettings["TrueCandidates"] = false;
            boolSettings["EditGivenMarks"] = false;
            forgetFutureChanges();
            sendPuzzle("step");
            return true;
          };
        }
        initCancelableButton(checkButton, "check");
        initCancelableButton(countButton, "count");
        initCancelableButton(solveButton, "solve");
      }
    };

    let buttonsShown = false;
    let showSolverButtons = function () {
      if (buttonsShown) return;
      buttonsShown = true;

      const consoleSidebar = sidebars.filter((sb) => sb.title === "Console")[0];
      let numCustomButtons = 0;

      if (consoleSidebar) {
        if (
          estimateCountButton &&
          !consoleSidebar.buttons.includes(estimateCountButton)
        ) {
          consoleSidebar.buttons.push(estimateCountButton);
        }
        if (estimateCountButton) numCustomButtons++;

        if (
          trueCandidatesButton &&
          !consoleSidebar.buttons.includes(trueCandidatesButton)
        ) {
          consoleSidebar.buttons.push(trueCandidatesButton);
        }
        if (trueCandidatesButton) numCustomButtons++;

        consoleSidebar.buttons.sort((a, b) => a.y - b.y); // Ensure visual order

        // Store initial layout values if not already stored
        if (
          initialConsoleSection0Y === 0 &&
          consoleSidebar.sections &&
          consoleSidebar.sections.length > 0
        ) {
          initialConsoleSection0Y = consoleSidebar.sections[0].y;
        }
        if (prevConsoleOutputTop === 0 && consoleOutput) {
          prevConsoleOutputTop = consoleOutput.style.top;
          prevConsoleOutputHeight = consoleOutput.style.height;
        }

        // Adjust layout for custom buttons
        const shiftAmount = numCustomButtons * (buttonLH + buttonGap);
        if (
          consoleSidebar.sections &&
          consoleSidebar.sections.length > 0 &&
          initialConsoleSection0Y > 0
        ) {
          consoleSidebar.sections[0].y = initialConsoleSection0Y + shiftAmount;
        }
        if (consoleOutput && prevConsoleOutputTop !== 0) {
          const currentCanvasHeight = canvas.height || 900;
          const percentDiff = (shiftAmount / currentCanvasHeight) * 100;
          consoleOutput.style.top =
            parseFloat(prevConsoleOutputTop) + percentDiff + "%";
          consoleOutput.style.height =
            parseFloat(prevConsoleOutputHeight) - percentDiff + "%";
        }
      }
      window.solverConnected = true;
    };

    let hideSolverButtons = function () {
      if (!buttonsShown) return;
      buttonsShown = false;

      const consoleSidebar = sidebars.filter((sb) => sb.title === "Console")[0];
      if (consoleSidebar) {
        if (estimateCountButton) {
          let indexEst = consoleSidebar.buttons.indexOf(estimateCountButton);
          if (indexEst > -1) consoleSidebar.buttons.splice(indexEst, 1);
        }
        if (trueCandidatesButton) {
          let indexTC = consoleSidebar.buttons.indexOf(trueCandidatesButton);
          if (indexTC > -1) consoleSidebar.buttons.splice(indexTC, 1);
        }

        // Revert layout to initial state
        if (
          consoleSidebar.sections &&
          consoleSidebar.sections.length > 0 &&
          initialConsoleSection0Y > 0
        ) {
          consoleSidebar.sections[0].y = initialConsoleSection0Y;
        }
        if (consoleOutput && prevConsoleOutputTop !== 0) {
          consoleOutput.style.top = prevConsoleOutputTop;
          consoleOutput.style.height = prevConsoleOutputHeight;
        }
      }

      boolSettings["TrueCandidates"] = false;
      clearPencilmarkColors();
      window.solverConnected = false;
    };

    let origCreateSidebarConsole = createSidebarConsole;
    createSidebarConsole = function () {
      origCreateSidebarConsole();
      initialConsoleSection0Y = 0; // Reset initial Y in case sidebar is rebuilt
      buttonsShown = false;
      hookSolverButtons();
      if (solverSocket) showSolverButtons();
    };

    let origCreateSidebarMain = createSidebarMain;
    createSidebarMain = function () {
      origCreateSidebarMain();
      hookSolverButtons();
    };

    const lerpColor = function (a, b, amount) {
      /* ... same ... */
      const ar = a >> 16,
        ag = (a >> 8) & 0xff,
        ab = a & 0xff,
        br = b >> 16,
        bg = (b >> 8) & 0xff,
        bb = b & 0xff;
      const rr = ar + amount * (br - ar),
        rg = ag + amount * (bg - ag),
        rb = ab + amount * (bb - ab);
      const colStr =
        "000000" + ((rr << 16) + (rg << 8) + (rb | 0)).toString(16);
      return "#" + colStr.substr(colStr.length - 6);
    };
    const baseSolutionColor = "#000000",
      logicalSolutionColor = "#CC0000",
      oneSolutionColor = "#299b20";
    const twoSolutionColor = 0xafafff,
      eightSolutionColor = 0x0000ff;
    const setCenterMarkColor = function (cell, numSols, candIdx) {
      /* ... same ... */
      if (!cell.centerPencilMarkColors) cell.centerPencilMarkColors = [];
      let curCol = baseSolutionColor;
      if (numSols < 0) curCol = logicalSolutionColor;
      else if (numSols === 1) curCol = oneSolutionColor;
      else if (numSols > 1)
        curCol = lerpColor(
          twoSolutionColor,
          eightSolutionColor,
          Math.min(6, numSols - 2) / 6
        );
      cell.centerPencilMarkColors[candIdx + 1] = curCol;
    };
    const importCandidates = function (response) {
      /* ... same ... */
      clearPencilmarkColors();
      if (response.type === "truecandidates") {
        const sols = response.solutionsPerCandidate;
        const col = boolSettings["ColoredCandidates"];
        for (let i = 0; i < size; i++)
          for (let j = 0; j < size; j++) {
            const cel = grid[i][j];
            if (!cel || cel.given) continue;
            const ci = i * size + j;
            const cands = [];
            for (let cidx = 0; cidx < size; cidx++) {
              let ns = sols[ci * size + cidx];
              if (ns !== 0) {
                cands.push(cidx + 1);
                if (!col && ns > 0) ns = 0;
                setCenterMarkColor(cel, ns, cidx);
              }
            }
            if (
              cel.centerPencilMarkColors &&
              cel.centerPencilMarkColors.every((c) => c === baseSolutionColor)
            )
              cel.centerPencilMarkColors = null;
            cel.value = 0;
            cel.centerPencilMarks = [];
            cel.candidates = cands;
            if (cands.length == 1) cel.value = cands[0];
            else cel.centerPencilMarks = cands;
            cel.tcerror = false;
          }
      } else if (response.type === "logical") {
        const rcs = response.cells;
        for (let i = 0; i < size; i++)
          for (let j = 0; j < size; j++) {
            const cel = grid[i][j];
            if (!cel || cel.given) continue;
            const ci = i * size + j;
            const rcel = rcs[ci];
            if (rcel.value != 0) {
              cel.value = rcel.value;
              cel.centerPencilMarks = [];
              cel.candidates = [rcel.value];
            } else {
              cel.value = 0;
              cel.centerPencilMarks = rcel.candidates;
              cel.candidates = rcel.candidates;
            }
            cel.centerPencilMarkColors = null;
            cel.tcerror = false;
          }
      }
      onInputEnd();
    };
    const importGivens = function (response) {
      /* ... same ... */
      if (response.type === "solved") {
        const sol = response.solution;
        for (let i = 0; i < size; i++)
          for (let j = 0; j < size; j++) {
            const cel = grid[i][j];
            if (!cel || cel.given) continue;
            const ci = i * size + j;
            cel.value = sol[ci];
            cel.centerPencilMarks = [];
            cel.candidates = [cel.value];
            cel.centerPencilMarkColors = null;
            cel.tcerror = false;
          }
      }
      onInputEnd();
    };
    const clearCandidates = function () {
      /* ... same ... */
      for (let i = 0; i < size; i++)
        for (let j = 0; j < size; j++) {
          const cel = grid[i][j];
          if (!cel || cel.given) continue;
          cel.value = 0;
          cel.centerPencilMarks = [];
          cel.centerPencilMarkColors = null;
          cel.tcerror = true;
        }
    };
    const handleInvalid = function (response) {
      /* ... same ... */
      if (response.type === "invalid") {
        if (response.message && response.message.length > 0)
          log(response.message);
        else log("Invalid board (no solutions).");
        return true;
      }
      return false;
    };
    const handleTrueCandidates = function (response) {
      /* ... same ... */
      if (handleInvalid(response)) clearCandidates();
      else importCandidates(response);
    };
    const handleSolve = function (response) {
      /* ... same ... */
      if (handleInvalid(response)) clearCandidates();
      else importGivens(response);
      if (cancelButton && cancelButton.solverCommand === "solve") {
        cancelButton.title = cancelButton.origTitle;
        cancelButton = null;
      }
    };
    const handleCheck = function (response) {
      /* ... same ... */
      let compl = false;
      if (response.type === "count") {
        if (!response.inProgress) {
          const ct = response.count;
          if (ct == 0) log("There are no solutions.");
          else if (ct == 1) log("There is a unique solution.");
          else log("There are multiple solutions.");
          compl = true;
        }
      } else if (handleInvalid(response)) compl = true;
      if (cancelButton && compl && cancelButton.solverCommand === "check") {
        cancelButton.title = cancelButton.origTitle;
        cancelButton = null;
      }
    };
    const handleCount = function (response) {
      /* ... same ... */
      let compl = false;
      if (response.type === "count") {
        const ct = response.count;
        if (response.inProgress) {
          clearConsole();
          log("Found " + ct + " solutions so far...");
          commandIsComplete = false;
        } else {
          clearConsole();
          if (ct == 0) log("There are no solutions.");
          else if (ct == 1) log("There is a unique solution.");
          else log("There are exactly " + ct + " solutions.");
          compl = true;
          commandIsComplete = true;
        }
      } else if (handleInvalid(response)) {
        compl = true;
        commandIsComplete = true;
      }
      if (cancelButton && compl && cancelButton.solverCommand === "count") {
        cancelButton.title = cancelButton.origTitle;
        cancelButton = null;
      }
    };
    const handlePath = function (response) {
      /* ... same ... */
      if (!handleInvalid(response)) {
        importCandidates(response);
        log(response.message, { newLine: false });
      }
    };
    const handleStep = function (response) {
      /* ... same ... */
      if (!handleInvalid(response)) {
        importCandidates(response);
        log(response.message, { newLine: false });
      }
    };

    const handleEstimate = function (response) {
      if (handleInvalid(response)) {
        // Should not happen for estimate type, but good practice
        commandIsComplete = true; // Stop if invalid
        if (cancelButton && cancelButton.solverCommand === "estimate") {
          cancelButton.title = cancelButton.origTitle;
          cancelButton = null;
        }
        return;
      }
      clearConsole(); // Clear previous estimate message
      log(
        `Solution Estimate (after ${response.iterations.toLocaleString()} iterations):`
      );
      log(
        `  ~ ${response.estimate.toExponential(
          3
        )} (Rel.Err: ${response.relErrPercent.toFixed(2)}%)`
      );
      log(`  Stderr: ${response.stderr.toExponential(3)}`);
      log(
        `  95% CI: [${response.ci95_lower.toExponential(
          3
        )}, ${response.ci95_upper.toExponential(3)}]`
      );
      commandIsComplete = false; // Indicate that more updates are expected
    };

    let processingMessage = false;
    connectButton.click = function () {
      if (!this.hovering()) return;
      if (!solverSocket) {
        connectButton.title = "Connecting...";
        try {
          let socket = new WebSocket("ws://localhost:4545");
          socket.onopen = function () {
            console.log("Connection succeeded");
            solverSocket = socket;
            hookSolverButtons();
            showSolverButtons();
            connectButton.title = "Disconnect";
          };
          socket.onmessage = function (msg) {
            const response = JSON.parse(msg.data);
            if (response.nonce !== nonce) return;
            if (
              !allowCommandWhenUndo[lastCommand] &&
              changeIndex < changes.length - 1
            ) {
              console.log(
                "Undo occurred, discarding response for command:",
                lastCommand
              );
              if (cancelButton && cancelButton.solverCommand === lastCommand)
                cancelButton.title = cancelButton.origTitle;
              if (
                connectButton.title === "Calculating..." ||
                connectButton.title === "Cancelling..."
              )
                connectButton.title = "Disconnect";
              return;
            }
            try {
              processingMessage = true;
              let currentOpDefinitelyCompleted = true; // Assume true, specific handlers can set to false

              if (response.type === "canceled") {
                log("Operation canceled.");
                if (cancelButton) {
                  cancelButton.title = cancelButton.origTitle;
                  cancelButton = null;
                }
                commandIsComplete = true;
              } else if (lastCommand === "truecandidates") {
                handleTrueCandidates(response);
                commandIsComplete = true;
              } else if (lastCommand === "solve") {
                handleSolve(response);
                commandIsComplete = true;
              } else if (lastCommand === "check") {
                handleCheck(response);
                commandIsComplete = true;
              } else if (lastCommand === "count") {
                handleCount(response);
                currentOpDefinitelyCompleted = commandIsComplete;
              } else if (lastCommand === "solvepath") {
                handlePath(response);
                commandIsComplete = true;
              } else if (lastCommand === "step") {
                handleStep(response);
                commandIsComplete = true;
              } else if (lastCommand === "estimate") {
                handleEstimate(response);
                currentOpDefinitelyCompleted = commandIsComplete; // handleEstimate sets global commandIsComplete to false
              } else {
                console.warn(
                  "Unhandled lastCommand type in onmessage:",
                  lastCommand
                );
                commandIsComplete = true;
              }

              if (commandIsComplete && currentOpDefinitelyCompleted) {
                if (solverSocket) connectButton.title = "Disconnect";
                else connectButton.title = "Connect";
              } else if (!commandIsComplete) {
                // connectButton.title remains "Calculating..." or "Cancel" (for the specific button)
              }
            } catch (e) {
              console.error("Error processing message from solver:", e);
              if (solverSocket) connectButton.title = "Disconnect";
              else connectButton.title = "Connect";
              if (cancelButton) {
                cancelButton.title = cancelButton.origTitle;
                cancelButton = null;
              }
              commandIsComplete = true;
            } finally {
              processingMessage = false;
            }
          };
          socket.onclose = function () {
            connectButton.title = "Connect";
            console.log("Connection closed");
            solverSocket = null;
            hideSolverButtons();
            if (cancelButton) {
              cancelButton.title = cancelButton.origTitle;
              cancelButton = null;
            }
            commandIsComplete = true;
            window.solverConnected = false;
          };
          socket.onerror = function (error) {
            console.error("WebSocket Error:", error);
            connectButton.title = "Error";
          };
        } catch (e) {
          console.error("Failed to create WebSocket:", e);
          connectButton.title = "Connect";
        }
      } else {
        if (solverSocket) solverSocket.close();
      }
      return true;
    };
    buttons.push(connectButton);

    const origDrawPopups = drawPopups;
    const trueCandidatesOptionChanged = function () {
      /* ... same ... */
      if (!this.hovering()) return;
      this.origClickSS();
      if (boolSettings["TrueCandidates"]) {
        clearPencilmarkColors();
        sendPuzzle("truecandidates");
      }
      return true;
    };
    const settingsButtons = [
      /* ... same ... */ { heading: "Logical Solve Settings" },
      {
        id: "EnableLogicTuples",
        label: "Tuples",
        default: true,
        click: trueCandidatesOptionChanged,
      },
      {
        id: "EnableLogicPointing",
        label: "Pointing",
        default: true,
        click: trueCandidatesOptionChanged,
      },
      {
        id: "EnableLogicFishes",
        label: "Fishes",
        default: true,
        click: trueCandidatesOptionChanged,
      },
      {
        id: "EnableLogicWings",
        label: "Wings",
        default: true,
        click: trueCandidatesOptionChanged,
      },
      {
        id: "EnableLogicAIC",
        label: "AIC",
        default: true,
        click: trueCandidatesOptionChanged,
      },
      {
        id: "EnableLogicContradictions",
        label: "Contradictions",
        default: true,
        click: trueCandidatesOptionChanged,
      },
      { heading: "True Candidates Settings" },
      {
        id: "ColoredCandidates",
        label: "Solution Count",
        default: false,
        click: trueCandidatesOptionChanged,
      },
      {
        id: "LogicalCandidates",
        label: "Include Logical Candidates",
        default: false,
        click: trueCandidatesOptionChanged,
      },
      { heading: "Input Settings" },
      { id: "EditGivenMarks", label: "Edit Given Pencilmarks", default: false },
    ];
    drawPopups = function (overlapSidebars) {
      /* ... same ... */
      origDrawPopups(overlapSidebars);
      if (overlapSidebars && popup === "solversettings") {
        const box = popups[cID(popup)];
        ctx.lineWidth = lineWW;
        ctx.fillStyle = boolSettings["Dark Mode"] ? "#404040" : "#E0E0E0";
        ctx.strokeStyle = "#000000";
        ctx.fillRect(
          canvas.width / 2 - box.w / 2,
          canvas.height / 2 - box.h / 2,
          box.w,
          90
        );
        ctx.strokeRect(
          canvas.width / 2 - box.w / 2,
          canvas.height / 2 - box.h / 2,
          box.w,
          90
        );
        ctx.fillStyle = boolSettings["Dark Mode"] ? "#F0F0F0" : "#000000";
        ctx.font = "60px Arial";
        ctx.fillText(
          "Solver Settings",
          canvas.width / 2,
          canvas.height / 2 - box.h / 2 + 66
        );
        ctx.fillStyle = boolSettings["Dark Mode"] ? "#F0F0F0" : "#000000";
        ctx.font = `bold ${Math.floor(buttonSH)}px Arial`;
        const offsetX = canvas.width / 2 - (buttonSH + buttonGap) / 2;
        const offsetY =
          canvas.height / 2 - popups[cID("solversettings")].h / 2 + 135;
        let numSettingsButtons = 0;
        for (let buttonData of settingsButtons) {
          if (buttonData.heading)
            ctx.fillText(
              buttonData.heading,
              offsetX,
              offsetY + (buttonSH + buttonGap) * numSettingsButtons
            );
          numSettingsButtons++;
        }
      }
    };
    settingsButton.click = function () {
      /* ... same ... */
      if (!this.hovering()) return;
      togglePopup("solversettings");
      return true;
    };
    buttons.push(settingsButton);
    popups.solversettings = {
      w: 600,
      h: 125 + (buttonSH + buttonGap) * settingsButtons.length,
    };
    const closeSettingsButton = new button(
      canvas.width / 2 + popups[cID("solversettings")].w / 2,
      canvas.height / 2 - popups[cID("solversettings")].h / 2 - 20,
      40,
      40,
      ["solversettings"],
      "X",
      "X"
    );
    buttons.push(closeSettingsButton);
    let numSettingsButtons = 0;
    for (let buttonData of settingsButtons) {
      /* ... same ... */
      if (!buttonData.heading) {
        const newButton = new button(
          canvas.width / 2 - (buttonSH + buttonGap) / 2,
          canvas.height / 2 -
            popups[cID("solversettings")].h / 2 +
            110 +
            (buttonSH + buttonGap) * numSettingsButtons,
          450,
          buttonSH,
          ["solversettings"],
          buttonData.id,
          buttonData.label
        );
        if (
          !boolSettings.hasOwnProperty(buttonData.id) &&
          !boolSettings.find(
            (bs) => typeof bs === "string" && bs === buttonData.id
          )
        ) {
          boolSettings.push(buttonData.id);
          defaultSettings.push(buttonData.default ? true : false);
          extraSettingsNames.push(buttonData.id);
        }
        boolSettings[buttonData.id] = buttonData.default ? true : false;
        buttons.push(newButton);
        if (buttonData.click) {
          newButton.origClickSS = newButton.click;
          newButton.click = buttonData.click;
        }
      }
      numSettingsButtons++;
    }

    let installChangeProxy = function () {
      var changesProxy = new Proxy(changes, {
        apply: function (target, thisArg, argsList) {
          return thisArg[target].apply(this, argsList);
        },
        deleteProperty: function (target, property) {
          if (!processingMessage) {
            if (boolSettings["TrueCandidates"]) sendPuzzle("truecandidates");
            else if (cancelButton) cancelButton.click(true);
          }
          return delete target[property];
        },
        set: function (target, property, value, receiver) {
          target[property] = value;
          if (!processingMessage) {
            if (boolSettings["TrueCandidates"]) sendPuzzle("truecandidates");
            else if (cancelButton) cancelButton.click(true);
          }
          return true;
        },
      });
      changes = changesProxy;
    };
    const origClearChangeHistory = clearChangeHistory;
    clearChangeHistory = function () {
      origClearChangeHistory();
      installChangeProxy();
    };
    const origForgetFutureChanges = forgetFutureChanges;
    forgetFutureChanges = function () {
      origForgetFutureChanges();
      installChangeProxy();
    };

    const openCTCButton = new button(
      canvas.width / 2 - 175,
      canvas.height / 2 + 6 + (buttonLH + buttonGap) * 4,
      400,
      buttonLH,
      ["Export"],
      "OpenCTC",
      "Open in SudokuPad"
    );
    const openSudokuLabButton = new button(
      canvas.width / 2 - 175,
      canvas.height / 2 + 6 + (buttonLH + buttonGap) * 5,
      400,
      buttonLH,
      ["Export"],
      "SudokuLab",
      "Open in Sudoku Lab"
    );
    let origCreateOtherButtons = createOtherButtons;
    createOtherButtons = function () {
      /* ... same, careful with settings mutation ... */
      const tempRem = {};
      for (let name of extraSettingsNames) {
        let idx = boolSettings.indexOf(name);
        if (idx > -1) {
          tempRem[name] = boolSettings[name];
          boolSettings.splice(idx, 1);
          let dIdx = defaultSettings.findIndex(
            (ds) => typeof ds === "object" && ds.id === name
          );
          if (dIdx === -1)
            dIdx = defaultSettings.length - (boolSettings.length - idx);
          if (dIdx > -1 && dIdx < defaultSettings.length)
            defaultSettings.splice(dIdx, 1);
        }
      }
      origCreateOtherButtons();
      for (let name of extraSettingsNames) {
        if (!boolSettings.includes(name)) {
          boolSettings.push(name);
          const sDef = settingsButtons.find((sb) => sb.id === name);
          const dVal = sDef ? (sDef.default ? true : false) : false;
          defaultSettings.push(dVal);
          boolSettings[name] = tempRem.hasOwnProperty(name)
            ? tempRem[name]
            : dVal;
        } else {
          boolSettings[name] = tempRem.hasOwnProperty(name)
            ? tempRem[name]
            : boolSettings[name];
        }
      }
      buttons
        .filter(
          (b) =>
            b.modes.includes("Export") &&
            b !== openCTCButton &&
            b !== openSudokuLabButton &&
            b.x < canvas.width / 2
        )
        .forEach((b) => (b.y -= 90));
    };
    popups.export.h += 200;
    buttons.push(openCTCButton);
    buttons.push(openSudokuLabButton);
    openCTCButton.click = function () {
      if (!this.hovering()) return;
      window.open(
        "https://sudokupad.app/?puzzleid=fpuzzles" +
          encodeURIComponent(exportPuzzle())
      );
      return true;
    };
    openSudokuLabButton.click = function () {
      if (!this.hovering()) return;
      window.open("https://sudokulab.net/?fpuzzle=" + exportPuzzle());
      return true;
    };
    buttons
      .filter((b) => b.modes.includes("Export"))
      .forEach((b) => (b.y -= 90));
    if (document.getElementById("previewTypeBox"))
      document.getElementById("previewTypeBox").style.top = "29%";

    const origCell = cell;
    cell = function (i, j, outside) {
      /* ... same cell rendering ... */
      const c = new origCell(i, j, outside);
      c.origEnterSS = c.enter;
      c.enter = function (val, forced, isLast) {
        if (
          forced ||
          this.given ||
          !boolSettings["EditGivenMarks"] ||
          tempEnterMode !== "Center"
        ) {
          this.origEnterSS(val, forced, isLast);
          return;
        }
        val = parseInt(val);
        if (val <= size) {
          if (!val) this.givenPencilMarks = [];
          else {
            if (!this.givenPencilMarks) this.givenPencilMarks = [];
            if (this.givenPencilMarks.includes(val))
              this.givenPencilMarks.splice(
                this.givenPencilMarks.indexOf(val),
                1
              );
            else this.givenPencilMarks.push(val);
            this.givenPencilMarks.sort((a, b) => a - b);
          }
        }
      };
      c.origShowTopSS = c.showTop;
      c.showTop = function () {
        const dFS = boolSettings["Dark Mode"] ? "#FFFFFF" : "#000000";
        const gMFS = boolSettings["Dark Mode"] ? "#FF8080" : "#800000";
        const hGM =
          currentTool !== "Regions" &&
          !this.given &&
          this.givenPencilMarks &&
          this.givenPencilMarks.length > 0;
        if ((currentTool === "Regions" && !previewMode) || this.value)
          this.origShowTopSS();
        else if (!previewMode) {
          const TLInt =
            constraints[cID("Killer Cage")].some(
              (a) => a.value.length && a.cells[0] === this
            ) ||
            cosmetics[cID("Cage")].some(
              (a) => a.value.length && a.cells[0] === this
            ) ||
            constraints[cID("Quadruple")].some((a) => a.cells[3] === this);
          const TRInt = constraints[cID("Quadruple")].some(
            (a) => a.cells[2] === this
          );
          const BLInt = constraints[cID("Quadruple")].some(
            (a) => a.cells[1] === this
          );
          const BRInt = constraints[cID("Quadruple")].some(
            (a) => a.cells[0] === this
          );
          ctx.fillStyle = boolSettings["Dark Mode"] ? "#F0F0F0" : "#000000";
          ctx.font = cellSL * 0.19 * textScale + "px Arial";
          for (var a = 0; a < Math.min(4, this.cornerPencilMarks.length); a++) {
            var x = this.x + cellSL * 0.14 + cellSL * 0.7 * (a % 2);
            if ((a === 0 && TLInt) || (a === 2 && BLInt)) x += cellSL * 0.285;
            if ((a === 1 && TRInt) || (a === 3 && BRInt)) x -= cellSL * 0.285;
            var y = this.y + cellSL * 0.25 + cellSL * 0.64 * (a > 1);
            ctx.fillText(this.cornerPencilMarks[a], x, y);
          }
          const cMO = hGM ? 0.4 : 0.6;
          const cPMA = Array(Math.ceil(this.centerPencilMarks.length / 5))
            .fill()
            .map((_, idx) => idx * 5)
            .map((s) => this.centerPencilMarks.slice(s, s + 5));
          ctx.font = `${cellSL * 0.19 * textScale}px Arial`;
          if (!this.centerPencilMarkColors) {
            ctx.fillStyle = dFS;
            for (let a = 0; a < cPMA.length; a++)
              ctx.fillText(
                cPMA[a].join(""),
                this.x + cellSL / 2,
                this.y +
                  cellSL * cMO -
                  ((cPMA.length - 1) / 2 - a) * cellSL * 0.1666 * textScale
              );
          } else {
            const dX = cellSL * 0.19 * textScale * (5.0 / 9.0);
            for (let a = 0; a < cPMA.length; a++) {
              const cM = cPMA[a];
              const x = this.x + cellSL / 2;
              const y =
                this.y +
                cellSL * cMO -
                ((cPMA.length - 1) / 2 - a) * cellSL * 0.1666 * textScale;
              for (let m = 0; m < cM.length; m++) {
                let v = cM[m];
                const col = this.centerPencilMarkColors[v];
                ctx.fillStyle = col ? col : dFS;
                ctx.fillText(v, x + (m - (cM.length - 1) / 2) * dX, y);
              }
            }
          }
        }
        if (hGM) {
          ctx.font = `bold ${cellSL * 0.19 * textScale}px Arial`;
          const gPMA = Array(Math.ceil(this.givenPencilMarks.length / 5))
            .fill()
            .map((_, idx) => idx * 5)
            .map((s) => this.givenPencilMarks.slice(s, s + 5));
          ctx.fillStyle = gMFS;
          for (let a = 0; a < gPMA.length; a++)
            ctx.fillText(
              gPMA[a].join(""),
              this.x + cellSL / 2,
              this.y +
                cellSL * 0.85 -
                ((gPMA.length - 1) / 2 - a) * cellSL * 0.1666 * textScale
            );
        }
        if (
          boolSettings["TrueCandidates"] &&
          this.tcerror &&
          this.value === 0 &&
          this.centerPencilMarks.length === 0 &&
          !this.given
        ) {
          ctx.font = `bold ${cellSL * 0.8}px Arial`;
          ctx.fillStyle = "#FF000080";
          ctx.fillText("X", this.x + cellSL / 2, this.y + cellSL * 0.8);
        }
      };
      return c;
    };

    const enableLogicPrefix = "enablelogic";
    const origExportPuzzle = exportPuzzle;
    exportPuzzle = function (includeCands) {
      /* ... same ... */
      const comp = origExportPuzzle(includeCands);
      const puz = JSON.parse(compressor.decompressFromBase64(comp));
      for (let i = 0; i < size; i++)
        for (let j = 0; j < size; j++) {
          const cel = window.grid[i][j];
          if (cel.givenPencilMarks && cel.givenPencilMarks.length > 0)
            puz.grid[i][j].givenPencilMarks = cel.givenPencilMarks;
          else delete puz.grid[i][j].givenPencilMarks;
        }
      return compressor.compressToBase64(JSON.stringify(puz));
    };
    const origImportPuzzle = importPuzzle;
    importPuzzle = function (str, clearHist) {
      /* ... same ... */
      origImportPuzzle(str, clearHist);
      const puz = JSON.parse(compressor.decompressFromBase64(str));
      for (let i = 0; i < size; i++)
        for (let j = 0; j < size; j++) {
          if (
            puz.grid[i][j].givenPencilMarks &&
            puz.grid[i][j].givenPencilMarks.length > 0
          )
            grid[i][j].givenPencilMarks = puz.grid[i][j].givenPencilMarks;
        }
      if (clearHist) {
        generateCandidates();
        resetKnownPuzzleInformation();
        clearChangeHistory();
      }
    };

    installChangeProxy();

    if (window.boolConstraints) {
      // Dev reload helper
      let prevBtns = buttons.splice(0, buttons.length);
      window.onload();
      buttons.splice(0, buttons.length);
      for (let i = 0; i < prevBtns.length; i++) buttons.push(prevBtns[i]);
      hookSolverButtons();
      if (solverSocket) showSolverButtons();
      installChangeProxy();
    }
  }; // end of doShim

  let intervalId = setInterval(() => {
    if (
      typeof grid === "undefined" ||
      typeof exportPuzzle === "undefined" ||
      typeof importPuzzle === "undefined" ||
      typeof cell === "undefined"
    ) {
      return;
    }

    clearInterval(intervalId);
    doShim();
  }, 16);
})();
