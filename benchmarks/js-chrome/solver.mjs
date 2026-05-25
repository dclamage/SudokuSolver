const SIZE = 9;
const BOX_SIZE = 3;
const NUM_CELLS = SIZE * SIZE;
const ALL_VALUES = (1 << SIZE) - 1;
const MAX_FRAMES = NUM_CELLS * 2 + 4;
const QUEUE_CAPACITY = 32768;

const VALUE_MASKS = new Uint16Array(SIZE + 1);
const POPCOUNT = new Uint8Array(1 << SIZE);
const LOW_VALUE = new Uint8Array(1 << SIZE);
const SUM_BY_MASK = new Uint8Array(1 << SIZE);

for (let value = 1; value <= SIZE; value++) {
  VALUE_MASKS[value] = 1 << (value - 1);
}

for (let mask = 1; mask <= ALL_VALUES; mask++) {
  const lowBit = mask & -mask;
  POPCOUNT[mask] = POPCOUNT[mask & (mask - 1)] + 1;
  LOW_VALUE[mask] = 32 - Math.clz32(lowBit);
  SUM_BY_MASK[mask] = SUM_BY_MASK[mask & (mask - 1)] + LOW_VALUE[lowBit];
}

const UNITS = buildUnits();
const PEER_MATRIX = buildPeerMatrix();
const PEERS = buildPeers();

const DEFAULT_OPTIONS = Object.freeze({
  maxSolutions: 0,
  traceLimit: 0,
  useHouseBilocals: false,
  useValueConflictScores: false,
});

export function parseIssArrowText(text) {
  const arrows = [];
  const source = String(text ?? '').trim();
  const matches = source.matchAll(/\.Arrow~([^.]*)/g);

  for (const match of matches) {
    const ids = match[1].split('~').filter(Boolean);
    if (ids.length < 2) {
      throw new Error(`Arrow requires a circle and at least one line cell: ${match[0]}`);
    }

    const cells = new Uint8Array(ids.length);
    for (let i = 0; i < ids.length; i++) {
      cells[i] = parseCellId(ids[i]);
    }
    arrows.push(cells);
  }

  if (arrows.length === 0) {
    throw new Error('No .Arrow constraints found in ISS text.');
  }

  return { size: SIZE, arrows };
}

export function runArrowSolve(issText, options = {}) {
  const opts = { ...DEFAULT_OPTIONS, ...options };
  const setupStart = nowMs();
  const puzzle = parseIssArrowText(issText);
  const solver = new ArrowSudokuSolver(puzzle, opts);
  const setupMs = nowMs() - setupStart;

  const runtimeStart = nowMs();
  const solutions = solver.countSolutions(opts.maxSolutions);
  const runtimeMs = nowMs() - runtimeStart;

  return {
    engine: 'js-chrome-arrow-reference',
    language: 'javascript',
    runtime: 'chrome',
    puzzle: {
      size: SIZE,
      arrows: puzzle.arrows.length,
      givens: 0,
    },
    setupMs,
    runtimeMs,
    totalMs: setupMs + runtimeMs,
    solutions,
    ...solver.resultStats(),
  };
}

export function runArrowSamples(issText, options = {}) {
  const warmup = Math.max(0, options.warmup ?? 5);
  const samples = Math.max(1, options.samples ?? 20);
  const solveOptions = {
    maxSolutions: options.maxSolutions ?? 0,
    traceLimit: options.traceLimit ?? 0,
  };

  for (let i = 0; i < warmup; i++) {
    runArrowSolve(issText, solveOptions);
  }

  const results = [];
  for (let i = 0; i < samples; i++) {
    results.push(runArrowSolve(issText, solveOptions));
  }

  return {
    kind: 'arrow-js-chrome-samples',
    warmup,
    samples: results,
    summary: summarizeSamples(results),
  };
}

export class ArrowSudokuSolver {
  constructor(puzzle, options = {}) {
    this.options = options;
    this.arrows = puzzle.arrows.map(cells => buildArrow(cells));
    this.constraintsByCell = buildConstraintsByCell(this.arrows);
    this.initialGrid = new Uint16Array(NUM_CELLS);
    this.initialGrid.fill(ALL_VALUES);

    this.conflictScores = new Int32Array(NUM_CELLS);
    this.valueConflictScores = new Int32Array(SIZE);
    this.seedConflictScores();

    this.supports = new Uint16Array(SIZE);
    this.constraintQueued = new Uint8Array(UNITS.length + this.arrows.length);
    this.constraintQueue = new Int16Array(QUEUE_CAPACITY);

    this.gridBuffer = new ArrayBuffer(MAX_FRAMES * NUM_CELLS * Uint16Array.BYTES_PER_ELEMENT);
    this.gridPool = new Array(MAX_FRAMES);
    for (let i = 0; i < MAX_FRAMES; i++) {
      this.gridPool[i] = new Uint16Array(this.gridBuffer, i * NUM_CELLS * Uint16Array.BYTES_PER_ELEMENT, NUM_CELLS);
    }
    this.freeStack = new Int16Array(MAX_FRAMES);
    this.frameGridIndex = new Int16Array(MAX_FRAMES);
    this.frameDepth = new Uint8Array(MAX_FRAMES);
    this.framePendingCell = new Int16Array(MAX_FRAMES);
    this.framePendingForced = new Uint8Array(MAX_FRAMES);
    this.frameConflictValue = new Uint16Array(MAX_FRAMES);

    this.freeTop = 0;
    this.frameTop = 0;
    this.queueHead = 0;
    this.queueTail = 0;
    this.propagationNewSingletons = 0;
    this.branchCell = -1;
    this.branchValueMask = 0;
    this.branchCount = 0;
    this.valueScoreMask = 0;
    this.valueScore = 0;

    this.trace = [];
    this.traceHash = 0x811c9dc5;
    this.solutions = 0;
    this.guesses = 0;
    this.valuesTried = 0;
    this.nodesSearched = 0;
    this.backtracks = 0;
    this.domainEliminations = 0;
    this.propagationSteps = 0;
  }

  countSolutions(maxSolutions = 0) {
    this.resetSearchState();

    const rootIndex = this.allocGrid();
    const rootGrid = this.gridPool[rootIndex];
    rootGrid.set(this.initialGrid);
    this.propagationNewSingletons = 0;
    if (!this.propagateAll(rootGrid)) {
      this.backtracks++;
      this.freeGrid(rootIndex);
      return 0;
    }
    this.valuesTried += this.propagationNewSingletons;
    this.pushFrame(rootIndex, 0, -1, 0, 0);
    this.search(maxSolutions);
    return maxSolutions > 0 && this.solutions > maxSolutions
      ? maxSolutions
      : this.solutions;
  }

  resultStats() {
    return {
      guesses: this.guesses,
      valuesTried: this.valuesTried,
      nodesSearched: this.nodesSearched,
      backtracks: this.backtracks,
      domainEliminations: this.domainEliminations,
      propagationSteps: this.propagationSteps,
      traceHash: (this.traceHash >>> 0).toString(16).padStart(8, '0'),
      trace: this.trace,
    };
  }

  seedConflictScores() {
    this.conflictScores.fill(SIZE * 3);
    for (const cells of this.arrows.map(a => a.cells)) {
      const priority = Math.max(SIZE * 2 - cells.length, SIZE);
      for (let i = 0; i < cells.length; i++) {
        this.conflictScores[cells[i]] += priority;
      }
    }
  }

  search(maxSolutions) {
    while (this.frameTop > 0) {
      const frameIndex = --this.frameTop;
      const gridIndex = this.frameGridIndex[frameIndex];
      const grid = this.gridPool[gridIndex];
      const depth = this.frameDepth[frameIndex];
      const pendingCell = this.framePendingCell[frameIndex];
      const conflictValue = this.frameConflictValue[frameIndex];

      if (pendingCell >= 0) {
        this.propagationNewSingletons = this.framePendingForced[frameIndex];
        if (!this.propagateFromCell(grid, pendingCell)) {
          this.backtracks++;
          this.incrementConflict(pendingCell, conflictValue);
          this.freeGrid(gridIndex);
          continue;
        }
        this.valuesTried += this.propagationNewSingletons;
      }

      this.nodesSearched++;

      this.selectBestBranch(grid);
      if (this.branchCell < 0) {
        this.solutions++;
        this.mixTrace(255, depth, this.solutions & 255);
        this.freeGrid(gridIndex);
        if (maxSolutions > 0 && this.solutions >= maxSolutions) {
          break;
        }
        continue;
      }

      const cell = this.branchCell;
      const mask = grid[cell];
      const valueMask = this.branchValueMask;
      this.guesses++;
      this.valuesTried++;
      this.mixTrace(cell, depth, LOW_VALUE[valueMask]);

      if (this.trace.length < (this.options.traceLimit ?? 0)) {
        this.trace.push({
          depth,
          cell: cellToId(cell),
          value: LOW_VALUE[valueMask],
          candidates: maskToDigits(mask),
        });
      }

      const clearIndex = this.allocGrid();
      const clearGrid = this.gridPool[clearIndex];
      clearGrid.set(grid);
      const clearMask = mask & ~valueMask;
      clearGrid[cell] = clearMask;
      this.pushFrame(
        clearIndex,
        depth + 1,
        cell,
        (clearMask & (clearMask - 1)) === 0 ? 1 : 0,
        valueMask,
      );

      grid[cell] = valueMask;
      this.pushFrame(gridIndex, depth + 1, cell, 0, valueMask);
    }

    while (this.frameTop > 0) {
      this.freeGrid(this.frameGridIndex[--this.frameTop]);
    }
  }

  resetSearchState() {
    this.freeTop = MAX_FRAMES;
    for (let i = 0; i < MAX_FRAMES; i++) {
      this.freeStack[i] = i;
    }
    this.frameTop = 0;
  }

  allocGrid() {
    if (this.freeTop === 0) {
      throw new Error('Search grid pool exhausted');
    }
    return this.freeStack[--this.freeTop];
  }

  freeGrid(gridIndex) {
    this.freeStack[this.freeTop++] = gridIndex;
  }

  pushFrame(gridIndex, depth, pendingCell, pendingForced, conflictValue) {
    if (this.frameTop === MAX_FRAMES) {
      throw new Error('Search frame stack exhausted');
    }

    const frameIndex = this.frameTop++;
    this.frameGridIndex[frameIndex] = gridIndex;
    this.frameDepth[frameIndex] = depth;
    this.framePendingCell[frameIndex] = pendingCell;
    this.framePendingForced[frameIndex] = pendingForced;
    this.frameConflictValue[frameIndex] = conflictValue;
  }

  propagateAll(grid) {
    this.resetQueue();
    for (let constraintIndex = 0; constraintIndex < this.constraintQueued.length; constraintIndex++) {
      this.enqueueConstraint(constraintIndex);
    }
    return this.drainQueue(grid);
  }

  propagateFromCell(grid, cell) {
    this.resetQueue();
    this.enqueueCell(cell);
    return this.drainQueue(grid);
  }

  resetQueue() {
    this.constraintQueued.fill(0);
    this.queueHead = 0;
    this.queueTail = 0;
  }

  enqueueCell(cell) {
    const indexes = this.constraintsByCell[cell];
    for (let i = 0; i < indexes.length; i++) {
      this.enqueueConstraint(indexes[i]);
    }
  }

  enqueueConstraint(constraintIndex) {
    if (this.constraintQueued[constraintIndex]) return;
    this.constraintQueued[constraintIndex] = 1;
    if (this.queueTail === this.constraintQueue.length) {
      throw new Error('Constraint queue exhausted');
    }
    this.constraintQueue[this.queueTail++] = constraintIndex;
  }

  drainQueue(grid) {
    const queue = this.constraintQueue;
    while (this.queueHead < this.queueTail) {
      const constraintIndex = queue[this.queueHead++];
      this.constraintQueued[constraintIndex] = 0;
      this.propagationSteps++;

      const valid = constraintIndex < UNITS.length
        ? this.enforceHouse(grid, constraintIndex)
        : this.enforceArrow(grid, constraintIndex - UNITS.length);

      if (!valid) return false;
    }

    return true;
  }

  enforceHouse(grid, unitIndex) {
    const unit = UNITS[unitIndex];
    let fixedMask = 0;

    for (let i = 0; i < SIZE; i++) {
      const mask = grid[unit[i]];
      if (mask === 0) return false;
      if ((mask & (mask - 1)) === 0) {
        if (fixedMask & mask) return false;
        fixedMask |= mask;
      }
    }

    let once = 0;
    let twice = 0;
    for (let i = 0; i < SIZE; i++) {
      const cell = unit[i];
      const oldMask = grid[cell];
      let nextMask = oldMask;

      if ((oldMask & (oldMask - 1)) !== 0) {
        nextMask = oldMask & ~fixedMask;
        if (nextMask === 0) return false;
        if (nextMask !== oldMask) {
          this.narrowCell(grid, cell, nextMask);
          this.enqueueCell(cell);
        }
      }

      twice |= once & nextMask;
      once |= nextMask;
    }

    if ((once & ALL_VALUES) !== ALL_VALUES) return false;

    let hiddenSingles = once & ~twice;
    while (hiddenSingles) {
      const bit = hiddenSingles & -hiddenSingles;
      hiddenSingles ^= bit;
      let lastCell = -1;

      for (let i = 0; i < SIZE; i++) {
        const cell = unit[i];
        if (grid[cell] & bit) {
          lastCell = cell;
          break;
        }
      }

      if (lastCell < 0) return false;
      if (grid[lastCell] !== bit) {
        this.narrowCell(grid, lastCell, bit);
        this.enqueueCell(lastCell);
      }
    }

    return true;
  }

  enforceArrow(grid, arrowIndex) {
    const arrow = this.arrows[arrowIndex];
    const cells = arrow.cells;
    const tupleMasks = arrow.tupleMasks;
    const width = cells.length;
    const supports = this.supports;

    supports.fill(0, 0, width);

    for (let offset = 0; offset < tupleMasks.length; offset += width) {
      let valid = true;
      for (let i = 0; i < width; i++) {
        if ((grid[cells[i]] & tupleMasks[offset + i]) === 0) {
          valid = false;
          break;
        }
      }

      if (!valid) continue;

      for (let i = 0; i < width; i++) {
        supports[i] |= tupleMasks[offset + i];
      }
    }

    for (let i = 0; i < width; i++) {
      const cell = cells[i];
      const oldMask = grid[cell];
      const nextMask = oldMask & supports[i];
      if (nextMask === 0) return false;
      if (nextMask !== oldMask) {
        this.narrowCell(grid, cell, nextMask);
        this.enqueueCell(cell);
      }
    }

    return true;
  }

  narrowCell(grid, cell, nextMask) {
    const oldMask = grid[cell];
    this.domainEliminations += POPCOUNT[oldMask] - POPCOUNT[nextMask];
    if ((oldMask & (oldMask - 1)) !== 0 && (nextMask & (nextMask - 1)) === 0) {
      this.propagationNewSingletons++;
    }
    grid[cell] = nextMask;
  }

  selectBestBranch(grid) {
    this.setMaxValueScore();
    let bestCell = -1;
    let bestScore = -1.0;
    let bestCount = 1;
    let bestMask = 0;

    for (let cell = 0; cell < NUM_CELLS; cell++) {
      const mask = grid[cell];
      const count = POPCOUNT[mask];
      if (count <= 1) continue;

      let scoreUnnormalized = this.conflictScores[cell];
      if (mask & this.valueScoreMask) {
        scoreUnnormalized += this.valueScore * 0.2;
      }
      const score = scoreUnnormalized / count;
      if (
        bestCell < 0
        || score > bestScore
        || (score === bestScore && count < bestCount)
      ) {
        bestCell = cell;
        bestScore = score;
        bestCount = count;
        bestMask = mask;
      }
    }

    if (bestCell < 0) {
      this.branchCell = -1;
      this.branchValueMask = 0;
      this.branchCount = 0;
      return;
    }

    if (this.options.useHouseBilocals && bestCount > 2 && bestScore > 0) {
      this.findBestHouseBilocal(grid, bestScore);
      if (this.branchCell >= 0) return;
    }

    this.branchCell = bestCell;
    this.branchValueMask = bestMask & -bestMask;
    this.branchCount = bestCount;
  }

  findBestHouseBilocal(grid, currentScore) {
    let bestCell = -1;
    let bestValueMask = 0;
    let bestScore = currentScore;

    for (let unitIndex = 0; unitIndex < UNITS.length; unitIndex++) {
      const unit = UNITS[unitIndex];
      let once = 0;
      let twice = 0;
      let more = 0;

      for (let i = 0; i < SIZE; i++) {
        const mask = grid[unit[i]];
        more |= twice & mask;
        twice |= once & mask;
        once |= mask;
      }

      let exactlyTwice = twice & ~more;
      while (exactlyTwice) {
        const valueMask = exactlyTwice & -exactlyTwice;
        exactlyTwice ^= valueMask;

        let cell0 = -1;
        let cell1 = -1;
        let maxScore = 0;
        for (let i = 0; i < SIZE; i++) {
          const cell = unit[i];
          if ((grid[cell] & valueMask) === 0) continue;
          if ((grid[cell] & (grid[cell] - 1)) === 0) {
            cell0 = -1;
            cell1 = -1;
            break;
          }
          if (cell0 < 0) cell0 = cell;
          else cell1 = cell;
          if (this.conflictScores[cell] > maxScore) {
            maxScore = this.conflictScores[cell];
          }
        }

        if (cell0 < 0 || cell1 < 0) continue;

        const score = maxScore * 0.5;
        if (score <= bestScore) continue;

        bestScore = score;
        bestValueMask = valueMask;
        bestCell = this.conflictScores[cell1] >= this.conflictScores[cell0] ? cell1 : cell0;
      }
    }

    this.branchCell = bestCell;
    this.branchValueMask = bestValueMask;
    this.branchCount = bestCell >= 0 ? 2 : 0;
  }

  setMaxValueScore() {
    if (!this.options.useValueConflictScores) {
      this.valueScoreMask = 0;
      this.valueScore = 0;
      return;
    }

    let maxScore = 0;
    let minScore = 0x7fffffff;
    let valueMask = 0;

    for (let i = 0; i < SIZE; i++) {
      const score = this.valueConflictScores[i];
      if (score > maxScore) {
        maxScore = score;
        valueMask = 1 << i;
      }
      if (score !== 0 && score < minScore) {
        minScore = score;
      }
    }

    if (maxScore < SIZE || (maxScore << 1) <= minScore * 3) {
      this.valueScoreMask = 0;
      this.valueScore = 0;
      return;
    }

    this.valueScoreMask = valueMask;
    this.valueScore = maxScore;
  }

  incrementConflict(cell, valueMask) {
    this.conflictScores[cell]++;
    if (valueMask) {
      this.valueConflictScores[LOW_VALUE[valueMask] - 1]++;
    }
  }

  mixTrace(cell, depth, value) {
    let hash = this.traceHash >>> 0;
    hash = Math.imul(hash ^ (cell + 1), 16777619) >>> 0;
    hash = Math.imul(hash ^ (depth + 1), 16777619) >>> 0;
    hash = Math.imul(hash ^ value, 16777619) >>> 0;
    this.traceHash = hash;
  }
}

function buildArrow(cells) {
  const tuples = [];
  const values = new Uint8Array(cells.length);

  for (let circleValue = 1; circleValue <= SIZE; circleValue++) {
    values[0] = circleValue;
    buildArrowTuples(cells, values, tuples, 1, circleValue);
  }

  return {
    cells,
    tupleMasks: Uint16Array.from(tuples, value => VALUE_MASKS[value]),
  };
}

function buildConstraintsByCell(arrows) {
  const lists = Array.from({ length: NUM_CELLS }, () => []);

  for (let unitIndex = 0; unitIndex < UNITS.length; unitIndex++) {
    const unit = UNITS[unitIndex];
    for (let i = 0; i < SIZE; i++) {
      lists[unit[i]].push(unitIndex);
    }
  }

  for (let arrowIndex = 0; arrowIndex < arrows.length; arrowIndex++) {
    const constraintIndex = UNITS.length + arrowIndex;
    const cells = arrows[arrowIndex].cells;
    for (let i = 0; i < cells.length; i++) {
      lists[cells[i]].push(constraintIndex);
    }
  }

  return lists.map(list => Uint8Array.from(list));
}

function buildArrowTuples(cells, values, tuples, index, remainingSum) {
  if (index === cells.length) {
    if (remainingSum === 0 && tupleRespectsPeers(cells, values)) {
      for (let i = 0; i < values.length; i++) tuples.push(values[i]);
    }
    return;
  }

  const remainingCells = cells.length - index - 1;
  const minRemaining = remainingCells;
  const maxRemaining = remainingCells * SIZE;
  const maxValue = Math.min(SIZE, remainingSum - minRemaining);

  for (let value = 1; value <= maxValue; value++) {
    const nextRemaining = remainingSum - value;
    if (nextRemaining < minRemaining || nextRemaining > maxRemaining) continue;
    values[index] = value;
    buildArrowTuples(cells, values, tuples, index + 1, nextRemaining);
  }
}

function tupleRespectsPeers(cells, values) {
  for (let i = 0; i < cells.length - 1; i++) {
    for (let j = i + 1; j < cells.length; j++) {
      if (values[i] === values[j] && PEER_MATRIX[cells[i] * NUM_CELLS + cells[j]]) {
        return false;
      }
    }
  }
  return true;
}

function buildUnits() {
  const units = [];

  for (let row = 0; row < SIZE; row++) {
    const unit = new Uint8Array(SIZE);
    for (let col = 0; col < SIZE; col++) unit[col] = row * SIZE + col;
    units.push(unit);
  }

  for (let col = 0; col < SIZE; col++) {
    const unit = new Uint8Array(SIZE);
    for (let row = 0; row < SIZE; row++) unit[row] = row * SIZE + col;
    units.push(unit);
  }

  for (let boxRow = 0; boxRow < BOX_SIZE; boxRow++) {
    for (let boxCol = 0; boxCol < BOX_SIZE; boxCol++) {
      const unit = new Uint8Array(SIZE);
      let index = 0;
      for (let dr = 0; dr < BOX_SIZE; dr++) {
        for (let dc = 0; dc < BOX_SIZE; dc++) {
          unit[index++] = (boxRow * BOX_SIZE + dr) * SIZE + boxCol * BOX_SIZE + dc;
        }
      }
      units.push(unit);
    }
  }

  return units;
}

function buildPeerMatrix() {
  const matrix = new Uint8Array(NUM_CELLS * NUM_CELLS);

  for (let cell = 0; cell < NUM_CELLS; cell++) {
    const row = (cell / SIZE) | 0;
    const col = cell % SIZE;
    const boxRow = (row / BOX_SIZE) | 0;
    const boxCol = (col / BOX_SIZE) | 0;

    for (let other = 0; other < NUM_CELLS; other++) {
      if (cell === other) continue;
      const otherRow = (other / SIZE) | 0;
      const otherCol = other % SIZE;
      if (
        row === otherRow
        || col === otherCol
        || (boxRow === ((otherRow / BOX_SIZE) | 0) && boxCol === ((otherCol / BOX_SIZE) | 0))
      ) {
        matrix[cell * NUM_CELLS + other] = 1;
      }
    }
  }

  return matrix;
}

function buildPeers() {
  const peers = [];
  for (let cell = 0; cell < NUM_CELLS; cell++) {
    const list = [];
    for (let other = 0; other < NUM_CELLS; other++) {
      if (PEER_MATRIX[cell * NUM_CELLS + other]) list.push(other);
    }
    peers.push(Uint8Array.from(list));
  }
  return peers;
}

function parseCellId(id) {
  const match = /^R([1-9])C([1-9])$/i.exec(id.trim());
  if (!match) {
    throw new Error(`Unsupported ISS cell id: ${id}`);
  }
  const row = Number(match[1]) - 1;
  const col = Number(match[2]) - 1;
  return row * SIZE + col;
}

function cellToId(cell) {
  return `R${((cell / SIZE) | 0) + 1}C${(cell % SIZE) + 1}`;
}

function maskToDigits(mask) {
  let text = '';
  for (let value = 1; value <= SIZE; value++) {
    if (mask & VALUE_MASKS[value]) text += value;
  }
  return text;
}

function nowMs() {
  return globalThis.performance?.now?.() ?? Date.now();
}

function summarizeSamples(samples) {
  const setup = samples.map(s => s.setupMs);
  const runtime = samples.map(s => s.runtimeMs);
  const total = samples.map(s => s.totalMs);
  const first = samples[0];

  return {
    setupMs: summarizeNumbers(setup),
    runtimeMs: summarizeNumbers(runtime),
    totalMs: summarizeNumbers(total),
    solutions: first.solutions,
    guesses: first.guesses,
    valuesTried: first.valuesTried,
    nodesSearched: first.nodesSearched,
    backtracks: first.backtracks,
    domainEliminations: first.domainEliminations,
    propagationSteps: first.propagationSteps,
    traceHash: first.traceHash,
    consistent: samples.every(s =>
      s.solutions === first.solutions
      && s.guesses === first.guesses
      && s.valuesTried === first.valuesTried
      && s.nodesSearched === first.nodesSearched
      && s.backtracks === first.backtracks
      && s.traceHash === first.traceHash),
  };
}

function summarizeNumbers(values) {
  const sorted = values.slice().sort((a, b) => a - b);
  return {
    min: sorted[0],
    p10: percentile(sorted, 0.10),
    median: percentile(sorted, 0.50),
    p90: percentile(sorted, 0.90),
    max: sorted[sorted.length - 1],
  };
}

function percentile(sorted, p) {
  if (sorted.length === 1) return sorted[0];
  const index = (sorted.length - 1) * p;
  const lower = Math.floor(index);
  const upper = Math.ceil(index);
  if (lower === upper) return sorted[lower];
  const weight = index - lower;
  return sorted[lower] * (1 - weight) + sorted[upper] * weight;
}

export const constants = Object.freeze({
  SIZE,
  NUM_CELLS,
  ALL_VALUES,
  POPCOUNT,
  LOW_VALUE,
  SUM_BY_MASK,
  PEERS,
});