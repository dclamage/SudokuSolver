// Page-side driver for the WASM solver testbed.
//
// Owns the worker lifecycle, assigns nonces the way the userscript does, and renders responses.
// No Sudoku UI yet by design — this exists to prove the solver runs in the browser and to measure
// what that costs.

const puzzleInput = document.getElementById('puzzle');
const logEl = document.getElementById('log');
const statusEl = document.getElementById('status');
const runtimeEl = document.getElementById('runtime');
const cancelButton = document.getElementById('cancel');
const rawToggle = document.getElementById('raw');

let worker = null;
let runtimeInfo = null;
let nonce = 0;

function log(text, kind = 'info') {
    const line = document.createElement('div');
    line.className = `line ${kind}`;
    line.textContent = text;
    logEl.appendChild(line);
    logEl.scrollTop = logEl.scrollHeight;
}

function setStatus(text, busy) {
    statusEl.textContent = text;
    statusEl.classList.toggle('busy', !!busy);
    cancelButton.disabled = !busy;
    for (const button of document.querySelectorAll('button[data-command]')) {
        button.disabled = !!busy;
    }
}

function startWorker() {
    worker = new Worker('worker.js', { type: 'module' });
    worker.onmessage = (e) => onWorkerMessage(e.data);
    worker.onerror = (e) => log(`Worker error: ${e.message}`, 'error');
}

function onWorkerMessage(msg) {
    switch (msg.kind) {
        case 'ready':
            runtimeInfo = msg.info;
            runtimeEl.textContent =
                `.NET ${msg.info.runtimeVersion} · ${msg.info.osDescription} · ` +
                `threads ${msg.info.threadsEnabled ? 'on' : 'off'} · ` +
                `${msg.info.processorCount} logical cpu${msg.info.processorCount === 1 ? '' : 's'}`;
            setStatus('idle', false);
            break;

        case 'response':
            renderResponse(JSON.parse(msg.payload), msg.payload);
            break;

        case 'done':
            log(`— finished in ${msg.elapsedMs.toFixed(1)} ms`, 'meta');
            setStatus('idle', false);
            break;

        case 'error':
            log(msg.message, 'error');
            setStatus('idle', false);
            break;
    }
}

function renderResponse(response, rawJson) {
    if (rawToggle.checked) {
        log(rawJson, 'raw');
        return;
    }

    switch (response.type) {
        case 'solved':
            log('Solution:', 'ok');
            log(formatGrid(response.solution), 'grid');
            break;

        case 'count':
            log(
                response.inProgress
                    ? `counting… ${response.count.toLocaleString()}`
                    : `Solution count: ${response.count.toLocaleString()}`,
                response.inProgress ? 'meta' : 'ok'
            );
            break;

        case 'estimate':
            log(
                `Estimate ${response.estimate.toExponential(4)} ` +
                `(95% CI ${response.ci95_lower.toExponential(3)} – ${response.ci95_upper.toExponential(3)}, ` +
                `±${response.relErrPercent.toFixed(2)}%, ${response.iterations.toLocaleString()} iters)`,
                'meta'
            );
            break;

        case 'truecandidates':
            log(summarizeTrueCandidates(response.solutionsPerCandidate), 'ok');
            break;

        case 'logical':
            log(response.message.trimEnd(), response.isValid ? 'ok' : 'error');
            break;

        case 'invalid':
            log(`Invalid: ${response.message}`, 'error');
            break;

        case 'canceled':
            log('Canceled.', 'meta');
            break;

        default:
            log(rawJson, 'raw');
    }
}

function formatGrid(values) {
    const size = Math.round(Math.sqrt(values.length));
    const rows = [];
    for (let r = 0; r < size; r++) {
        rows.push(values.slice(r * size, (r + 1) * size).join(' '));
    }
    return rows.join('\n');
}

function summarizeTrueCandidates(perCandidate) {
    const alive = perCandidate.filter((n) => n > 0).length;
    const logicallyRemovable = perCandidate.filter((n) => n === -1).length;
    return (
        `True candidates: ${alive} candidate${alive === 1 ? '' : 's'} have solutions` +
        (logicallyRemovable ? `, ${logicallyRemovable} eliminated by brute force but not by logic` : '')
    );
}

function send(command) {
    const data = puzzleInput.value.trim();
    if (!data) {
        log('Paste an f-puzzles URL first.', 'error');
        return;
    }

    nonce++;
    log(`> ${command}`, 'cmd');
    setStatus(`${command}…`, true);
    worker.postMessage({
        kind: 'message',
        payload: JSON.stringify({ nonce, command, dataType: 'fpuzzles', data }),
    });
}

function cancel() {
    if (runtimeInfo && runtimeInfo.threadsEnabled) {
        worker.postMessage({ kind: 'cancel' });
        return;
    }

    // Single-threaded builds block the worker inside the solve, so a queued cancel message would
    // never be read. Tearing the worker down is the only way out — it also drops the solver's
    // true-candidates cache and costs a runtime reboot.
    log('Terminating worker (single-threaded build cannot cancel cooperatively)…', 'meta');
    worker.terminate();
    runtimeInfo = null;
    runtimeEl.textContent = 'restarting runtime…';
    setStatus('restarting', true);
    startWorker();
}

for (const button of document.querySelectorAll('button[data-command]')) {
    button.addEventListener('click', () => send(button.dataset.command));
}
cancelButton.addEventListener('click', cancel);
document.getElementById('clear').addEventListener('click', () => (logEl.textContent = ''));

setStatus('booting', true);
startWorker();
