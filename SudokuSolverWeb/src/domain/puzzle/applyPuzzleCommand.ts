import type { PuzzleCommand } from "./commands";
import type {
  CandidateContext,
  CellId,
  ManualCellNotes,
  PuzzlePackageV1,
  ValueId,
} from "./types";
import {
  assertValidCandidateContext,
  isManualColorToken,
  isLogicalSolverCandidateContext,
  isManualCandidateContext,
  isTrueCandidatesContext,
} from "./types";

export interface CommandResult {
  document: PuzzlePackageV1;
  inverse: PuzzleCommand;
  semanticChange: boolean;
  changed: boolean;
}

interface PendingCommandResult {
  inverse: PuzzleCommand;
  semanticChange: boolean;
  changed: boolean;
}

function requireCell(document: PuzzlePackageV1, cellId: CellId) {
  const cell = document.cells[cellId];
  if (cell === undefined) {
    throw new Error(`missing cell ${cellId}`);
  }
  return cell;
}

function requireCandidateInput(document: PuzzlePackageV1, cellId: CellId) {
  const cell = requireCell(document, cellId);
  if (!cell.input.acceptsCandidates) {
    throw new Error(`cell ${cellId} does not accept candidate marks`);
  }
  return cell;
}

function findContextIndex(
  document: PuzzlePackageV1,
  contextId: string,
): number {
  const index = document.authoring.candidateContexts.findIndex(
    (context) => context.id === contextId,
  );
  if (index < 0) {
    throw new Error(`missing candidate context ${contextId}`);
  }
  return index;
}

function requireNewContextId(document: PuzzlePackageV1, contextId: string) {
  if (contextId.length === 0) {
    throw new Error("candidate context ID must be a non-empty string");
  }
  if (
    document.authoring.candidateContexts.some(
      (context) => context.id === contextId,
    )
  ) {
    throw new Error(`candidate context ${contextId} already exists`);
  }
}

function requireContextName(name: string) {
  if (name.length === 0) {
    throw new Error("candidate context name must be a non-empty string");
  }
}

function validateContext(context: CandidateContext) {
  assertValidCandidateContext(context);
}

function requireInsertionIndex(index: number | undefined, length: number): number {
  if (index === undefined) {
    return length;
  }
  if (!Number.isSafeInteger(index) || index < 0 || index > length) {
    throw new Error(`candidate context index ${index} is out of range`);
  }
  return index;
}

function requireMoveIndex(index: number, length: number): number {
  if (!Number.isSafeInteger(index) || index < 0 || index >= length) {
    throw new Error(`candidate context index ${index} is out of range`);
  }
  return index;
}

function requireValueIds(
  document: PuzzlePackageV1,
  cellId: CellId,
  valueIds: readonly ValueId[],
) {
  const cell = requireCell(document, cellId);
  const domain = document.domains[cell.domainId];
  if (domain === undefined) {
    throw new Error(`cell ${cellId} references missing domain ${cell.domainId}`);
  }
  const domainValueIds = new Set(domain.values.map((value) => value.id));
  for (const valueId of valueIds) {
    if (!domainValueIds.has(valueId)) {
      throw new Error(
        `value ${valueId} is not in cell ${cellId} domain ${cell.domainId}`,
      );
    }
  }
}

function normalizeValueIds(
  document: PuzzlePackageV1,
  cellId: CellId,
  valueIds: readonly ValueId[],
): ValueId[] {
  requireValueIds(document, cellId, valueIds);
  const cell = document.cells[cellId];
  const selectedValueIds = new Set(valueIds);
  return document.domains[cell.domainId].values
    .map((value) => value.id)
    .filter((valueId) => selectedValueIds.has(valueId));
}

function arraysEqual<T>(left: readonly T[], right: readonly T[]): boolean {
  return (
    left.length === right.length &&
    left.every((value, index) => value === right[index])
  );
}

function emptyManualCellNotes(): ManualCellNotes {
  return { corner: [], centre: [], color: null };
}

function manualCellNotesEqual(
  left: ManualCellNotes | undefined,
  right: ManualCellNotes | undefined,
): boolean {
  if (left === undefined || right === undefined) {
    return left === right;
  }
  return (
    arraysEqual(left.corner, right.corner) &&
    arraysEqual(left.centre, right.centre) &&
    left.color === right.color
  );
}

function isEmptyManualCellNotes(notes: ManualCellNotes): boolean {
  return (
    notes.corner.length === 0 &&
    notes.centre.length === 0 &&
    notes.color === null
  );
}

function normalizeManualCellNotes(
  document: PuzzlePackageV1,
  cellId: CellId,
  notes: ManualCellNotes,
): ManualCellNotes {
  if (notes.color !== null && !isManualColorToken(notes.color)) {
    throw new Error(`manual color ${String(notes.color)} is invalid`);
  }
  return {
    corner: normalizeValueIds(document, cellId, notes.corner),
    centre: normalizeValueIds(document, cellId, notes.centre),
    color: notes.color,
  };
}

function assignManualCellNotes(
  document: PuzzlePackageV1,
  contextId: string,
  cellId: CellId,
  notes: ManualCellNotes | undefined,
): void {
  const contextMarks = { ...(document.authoring.manualMarks[contextId] ?? {}) };
  if (notes === undefined || isEmptyManualCellNotes(notes)) {
    delete contextMarks[cellId];
  } else {
    contextMarks[cellId] = notes;
  }
  document.authoring.manualMarks[contextId] = contextMarks;
}

function cloneContextWithIdentity(
  source: CandidateContext,
  id: string,
  name: string,
): CandidateContext {
  assertValidCandidateContext(source);
  if (isManualCandidateContext(source)) {
    return { id, name, kind: "manual" };
  }
  if (isTrueCandidatesContext(source)) {
    return {
      id,
      name,
      kind: "trueCandidates",
      refresh: source.refresh,
      display: source.display,
      solutionCountCap: source.solutionCountCap,
    };
  }
  if (isLogicalSolverCandidateContext(source)) {
    return {
      id,
      name,
      kind: "logicalSolver",
      followPuzzleRevision: true,
      enabledTechniqueIds: [...source.enabledTechniqueIds],
    };
  }
  return { ...structuredClone(source), id, name };
}

function finishCommand(
  previous: PuzzlePackageV1,
  next: PuzzlePackageV1,
  pending: PendingCommandResult,
): CommandResult {
  if (!pending.changed) {
    return { document: previous, ...pending };
  }
  if (previous.revision >= Number.MAX_SAFE_INTEGER) {
    throw new Error("document revision cannot be incremented safely");
  }
  if (
    pending.semanticChange &&
    previous.semanticRevision >= Number.MAX_SAFE_INTEGER
  ) {
    throw new Error("semantic revision cannot be incremented safely");
  }
  next.revision = previous.revision + 1;
  next.semanticRevision =
    previous.semanticRevision + (pending.semanticChange ? 1 : 0);
  return { document: next, ...pending };
}

export function applyPuzzleCommand(
  document: PuzzlePackageV1,
  command: PuzzleCommand,
): CommandResult {
  const next = structuredClone(document);
  let pending: PendingCommandResult;

  switch (command.type) {
    case "setGiven": {
      requireValueIds(
        document,
        command.cellId,
        command.valueId === null ? [] : [command.valueId],
      );
      const previousValue = document.givens[command.cellId] ?? null;
      if (command.valueId === null) {
        delete next.givens[command.cellId];
      } else {
        next.givens[command.cellId] = command.valueId;
      }
      pending = {
        inverse: {
          type: "setGiven",
          cellId: command.cellId,
          valueId: previousValue,
        },
        semanticChange: true,
        changed: previousValue !== command.valueId,
      };
      break;
    }

    case "moveCell": {
      const cell = requireCell(document, command.cellId);
      if (cell.shape.kind !== "rect") {
        throw new Error(
          `moveCell supports rectangular cells only; ${command.cellId} uses ${cell.shape.kind}`,
        );
      }
      if (!Number.isFinite(command.x) || !Number.isFinite(command.y)) {
        throw new Error("cell coordinates must be finite numbers");
      }
      const nextCell = next.cells[command.cellId];
      if (nextCell.shape.kind !== "rect") {
        throw new Error("cell shape changed while applying moveCell");
      }
      nextCell.shape.x = command.x;
      nextCell.shape.y = command.y;
      pending = {
        inverse: {
          type: "moveCell",
          cellId: command.cellId,
          x: cell.shape.x,
          y: cell.shape.y,
        },
        semanticChange: false,
        changed: cell.shape.x !== command.x || cell.shape.y !== command.y,
      };
      break;
    }

    case "setManualMarks": {
      findContextIndex(document, command.contextId);
      const kind = command.kind ?? "corner";
      if (kind !== "corner" && kind !== "centre") {
        throw new Error(`manual mark kind ${String(kind)} is invalid`);
      }
      requireCandidateInput(document, command.cellId);
      const normalizedMarks = normalizeValueIds(
        document,
        command.cellId,
        command.valueIds,
      );
      const previousNotes =
        document.authoring.manualMarks[command.contextId]?.[command.cellId];
      const previousMarks = [...(previousNotes?.[kind] ?? [])];
      const nextNotes = {
        ...(previousNotes ?? emptyManualCellNotes()),
        [kind]: normalizedMarks,
      };
      assignManualCellNotes(next, command.contextId, command.cellId, nextNotes);
      pending = {
        inverse: {
          type: "setManualMarks",
          contextId: command.contextId,
          cellId: command.cellId,
          kind,
          valueIds: [...previousMarks],
        },
        semanticChange: false,
        changed: !arraysEqual(previousMarks, normalizedMarks),
      };
      break;
    }

    case "setManualColor": {
      findContextIndex(document, command.contextId);
      requireCandidateInput(document, command.cellId);
      if (command.color !== null && !isManualColorToken(command.color)) {
        throw new Error(`manual color ${String(command.color)} is invalid`);
      }
      const previousNotes =
        document.authoring.manualMarks[command.contextId]?.[command.cellId];
      const previousColor = previousNotes?.color ?? null;
      const nextNotes = {
        ...(previousNotes ?? emptyManualCellNotes()),
        color: command.color,
      };
      assignManualCellNotes(next, command.contextId, command.cellId, nextNotes);
      pending = {
        inverse: {
          type: "setManualColor",
          contextId: command.contextId,
          cellId: command.cellId,
          color: previousColor,
        },
        semanticChange: false,
        changed: previousColor !== command.color,
      };
      break;
    }

    case "clearManualCell": {
      findContextIndex(document, command.contextId);
      requireCell(document, command.cellId);
      const previousNotes =
        document.authoring.manualMarks[command.contextId]?.[command.cellId];
      assignManualCellNotes(next, command.contextId, command.cellId, undefined);
      pending = {
        inverse: {
          type: "restoreManualCell",
          contextId: command.contextId,
          cellId: command.cellId,
          notes: previousNotes === undefined ? null : structuredClone(previousNotes),
        },
        semanticChange: false,
        changed: previousNotes !== undefined,
      };
      break;
    }

    case "restoreManualCell": {
      findContextIndex(document, command.contextId);
      requireCell(document, command.cellId);
      const previousNotes =
        document.authoring.manualMarks[command.contextId]?.[command.cellId];
      const normalizedNotes =
        command.notes === null
          ? undefined
          : normalizeManualCellNotes(document, command.cellId, command.notes);
      const canonicalNotes =
        normalizedNotes === undefined || isEmptyManualCellNotes(normalizedNotes)
          ? undefined
          : normalizedNotes;
      assignManualCellNotes(
        next,
        command.contextId,
        command.cellId,
        canonicalNotes,
      );
      pending = {
        inverse: {
          type: "restoreManualCell",
          contextId: command.contextId,
          cellId: command.cellId,
          notes: previousNotes === undefined ? null : structuredClone(previousNotes),
        },
        semanticChange: false,
        changed: !manualCellNotesEqual(previousNotes, canonicalNotes),
      };
      break;
    }

    case "renameCandidateContext": {
      const contextIndex = findContextIndex(document, command.contextId);
      requireContextName(command.name);
      const previous = document.authoring.candidateContexts[contextIndex];
      const contexts = [...next.authoring.candidateContexts];
      contexts[contextIndex] = cloneContextWithIdentity(
        previous,
        previous.id,
        command.name,
      );
      next.authoring.candidateContexts = contexts;
      pending = {
        inverse: {
          type: "renameCandidateContext",
          contextId: command.contextId,
          name: previous.name,
        },
        semanticChange: false,
        changed: previous.name !== command.name,
      };
      break;
    }

    case "configureTrueCandidates": {
      const contextIndex = findContextIndex(document, command.contextId);
      const previous = document.authoring.candidateContexts[contextIndex];
      assertValidCandidateContext(previous);
      if (!isTrueCandidatesContext(previous)) {
        throw new Error(
          `candidate context ${command.contextId} is not a True Candidates context`,
        );
      }
      const configured: CandidateContext = {
        ...previous,
        refresh: command.refresh,
        display: command.display,
        solutionCountCap: command.solutionCountCap,
      };
      assertValidCandidateContext(configured);
      const contexts = [...next.authoring.candidateContexts];
      contexts[contextIndex] = configured;
      next.authoring.candidateContexts = contexts;
      pending = {
        inverse: {
          type: "configureTrueCandidates",
          contextId: command.contextId,
          refresh: previous.refresh,
          display: previous.display,
          solutionCountCap: previous.solutionCountCap,
        },
        semanticChange: false,
        changed:
          previous.refresh !== command.refresh ||
          previous.display !== command.display ||
          previous.solutionCountCap !== command.solutionCountCap,
      };
      break;
    }

    case "addCandidateContext": {
      requireNewContextId(document, command.context.id);
      validateContext(command.context);
      const index = requireInsertionIndex(
        command.index,
        document.authoring.candidateContexts.length,
      );
      const contexts = [...next.authoring.candidateContexts];
      contexts.splice(index, 0, structuredClone(command.context));
      next.authoring.candidateContexts = contexts;
      next.authoring.manualMarks[command.context.id] = {};
      pending = {
        inverse: {
          type: "removeCandidateContext",
          contextId: command.context.id,
        },
        semanticChange: false,
        changed: true,
      };
      break;
    }

    case "duplicateCandidateContext": {
      const sourceIndex = findContextIndex(document, command.sourceContextId);
      requireNewContextId(document, command.contextId);
      requireContextName(command.name);
      const index = requireInsertionIndex(
        command.index,
        document.authoring.candidateContexts.length,
      );
      const duplicate = cloneContextWithIdentity(
        document.authoring.candidateContexts[sourceIndex],
        command.contextId,
        command.name,
      );
      const contexts = [...next.authoring.candidateContexts];
      contexts.splice(index, 0, duplicate);
      next.authoring.candidateContexts = contexts;
      next.authoring.manualMarks[command.contextId] = structuredClone(
        document.authoring.manualMarks[command.sourceContextId] ?? {},
      );
      pending = {
        inverse: {
          type: "removeCandidateContext",
          contextId: command.contextId,
        },
        semanticChange: false,
        changed: true,
      };
      break;
    }

    case "moveCandidateContext": {
      const fromIndex = findContextIndex(document, command.contextId);
      const toIndex = requireMoveIndex(
        command.toIndex,
        document.authoring.candidateContexts.length,
      );
      const contexts = [...next.authoring.candidateContexts];
      const [context] = contexts.splice(fromIndex, 1);
      contexts.splice(toIndex, 0, context);
      next.authoring.candidateContexts = contexts;
      pending = {
        inverse: {
          type: "moveCandidateContext",
          contextId: command.contextId,
          toIndex: fromIndex,
        },
        semanticChange: false,
        changed: fromIndex !== toIndex,
      };
      break;
    }

    case "removeCandidateContext": {
      const contextIndex = findContextIndex(document, command.contextId);
      if (document.authoring.candidateContexts.length === 1) {
        throw new Error("cannot remove the last candidate context");
      }
      const context = document.authoring.candidateContexts[contextIndex];
      assertValidCandidateContext(context);
      const manualMarks =
        document.authoring.manualMarks[command.contextId] ?? {};
      const contexts = [...next.authoring.candidateContexts];
      contexts.splice(contextIndex, 1);
      next.authoring.candidateContexts = contexts;
      delete next.authoring.manualMarks[command.contextId];
      pending = {
        inverse: {
          type: "restoreCandidateContext",
          context: structuredClone(context),
          index: contextIndex,
          manualMarks: structuredClone(manualMarks),
        },
        semanticChange: false,
        changed: true,
      };
      break;
    }

    case "restoreCandidateContext": {
      requireNewContextId(document, command.context.id);
      validateContext(command.context);
      const index = requireInsertionIndex(
        command.index,
        document.authoring.candidateContexts.length,
      );
      const normalizedManualMarks = Object.fromEntries(
        Object.entries(command.manualMarks).flatMap(([cellId, notes]) => {
          const normalized = normalizeManualCellNotes(document, cellId, notes);
          return isEmptyManualCellNotes(normalized)
            ? []
            : [[cellId, normalized]];
        }),
      );
      const contexts = [...next.authoring.candidateContexts];
      contexts.splice(index, 0, structuredClone(command.context));
      next.authoring.candidateContexts = contexts;
      next.authoring.manualMarks[command.context.id] = structuredClone(
        normalizedManualMarks,
      );
      pending = {
        inverse: {
          type: "removeCandidateContext",
          contextId: command.context.id,
        },
        semanticChange: false,
        changed: true,
      };
      break;
    }
  }

  return finishCommand(document, next, pending);
}
