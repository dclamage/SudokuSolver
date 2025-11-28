// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

(async () => {
    const dotnetUrl = import.meta.resolve('./_framework/dotnet.js');
    const { dotnet } = await import(dotnetUrl);

    const { getAssemblyExports, getConfig, runMain } = await dotnet
        .withApplicationArguments('start')
        .create();

    const config = getConfig();
    const exports = await getAssemblyExports(config.mainAssemblyName);

    const outputDiv = document.getElementById('output');

    // Provide a dedicated JS function for wasm to call when it wants to post messages
    // back to the page. This avoids using window.postMessage which would create
    // confusing message events on the page.
    window.wasmReceiveMessage = (msg) => {
        try {
            const parsed = JSON.parse(msg);
            if (parsed.type === 'canceled') {
                outputDiv.innerText += "Computation was canceled by user.\n\n";
                return;
            }

            outputDiv.innerText = JSON.stringify(parsed, null, 2) + "\n\n";
        } catch {
            outputDiv.innerText = msg + "\n\n";
        }
    };

    document.getElementById('solveBtn').addEventListener('click', async e => {
        const inputJson = document.getElementById('inputJson').value;
        outputDiv.innerText = "Sending message...\n\n";
        // Call into the wasm-exported entrypoint; Program.HandleMessage will enqueue it on
        // the managed message thread inside the runtime.
        try {
            await exports.SudokuSolver.Wasm.Program.HandleMessage(inputJson);
        } catch (err) {
            console.error('Failed to send message to wasm:', err);
        }
    });

    document.getElementById('cancelBtn').addEventListener('click', async e => {
        const inputJson = document.getElementById('inputJson').value;
        try {
            const parsed = JSON.parse(inputJson);
            const cancelMsg = {
                nonce: parsed.nonce,
                command: 'cancel'
            };
            await exports.SudokuSolver.Wasm.Program.HandleMessage(JSON.stringify(cancelMsg));
        } catch (err) {
            console.error('Failed to parse input JSON to get nonce for cancellation', err);
        }
    });

    await runMain();
})();
