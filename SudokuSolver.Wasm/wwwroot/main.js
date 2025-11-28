// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { dotnet } from './_framework/dotnet.js'

const { getAssemblyExports, getConfig, runMain } = await dotnet
    .withApplicationArguments("start")
    .create();

const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);

document.getElementById('solveBtn').addEventListener('click', async e => {
    const inputJson = document.getElementById('inputJson').value;
    const outputDiv = document.getElementById('output');
    
    outputDiv.innerText = "Solving...";
    
    try {
        // Allow UI to update before blocking
        await new Promise(resolve => setTimeout(resolve, 10));
        
        const resultJson = exports.SudokuSolver.Wasm.Program.Solve(inputJson);
        
        // Format JSON for display
        try {
            const parsed = JSON.parse(resultJson);
            outputDiv.innerText = JSON.stringify(parsed, null, 2);
        } catch {
            outputDiv.innerText = resultJson;
        }
    } catch (err) {
        outputDiv.innerText = "Error: " + err.toString();
    }
});

// run the C# Main() method and keep the runtime process running and executing further API calls
await runMain();
