import type { PuzzleCommand } from "./commands";
import type {
  CandidateContext,
  CellId,
  PuzzlePackageV1,
  ValueId,
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

function requireSolutionCountCap(solutionCountCap: number) {
  if (!Number.isSafeInteger(solutionCountCap) || solutionCountCap < 0) {
    throw new Error("solutionCountCap must be a non-negative safe integer");
  }
}

function validateContext(context: CandidateContext) {
  if (context.id.length === 0) {
    throw new Error("candidate context ID must be a non-empty string");
  }
  requireContextName(context.name);
  if (context.kind === "trueCandidates") {
    requireSolutionCountCap(context.solutionCountCap);
  }
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

function cloneContextWithIdentity(
  source: CandidateContext,
  id: string,
  name: string,
): CandidateContext {
  if (source.kind === "manual") {
    return { id, name, kind: "manual" };
  }
  if (source.kind === "trueCandidates") {
    return {
      id,
      name,
      kind: "trueCandidates",
      refresh: source.refresh,
      display: source.display,
      solutionCountCap: source.solutionCountCap,
    };
  }
  return {
    id,
    name,
    kind: "logicalSolver",
    followPuzzleRevision: true,
    enabledTechniqueIds: [...source.enabledTechniqueIds],
  };
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
      const normalizedMarks = normalizeValueIds(
        document,
        command.cellId,
        command.valueIds,
      );
      const previousMarks = [
        ...(document.authoring.manualMarks[command.contextId]?.[
          command.cellId
        ] ?? []),
      ];
      const contextMarks = {
        ...(next.authoring.manualMarks[command.contextId] ?? {}),
      };
      if (normalizedMarks.length === 0) {
        delete contextMarks[command.cellId];
      } else {
        contextMarks[command.cellId] = normalizedMarks;
      }
      next.authoring.manualMarks[command.contextId] = contextMarks;
      pending = {
        inverse: {
          type: "setManualMarks",
          contextId: command.contextId,
          cellId: command.cellId,
          valueIds: [...previousMarks],
        },
        semanticChange: false,
        changed: !arraysEqual(previousMarks, normalizedMarks),
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
      if (previous.kind !== "trueCandidates") {
        throw new Error(
          `candidate context ${command.contextId} is not a True Candidates context`,
        );
      }
      requireSolutionCountCap(command.solutionCountCap);
      const contexts = [...next.authoring.candidateContexts];
      contexts[contextIndex] = {
        ...previous,
        refresh: command.refresh,
        display: command.display,
        solutionCountCap: command.solutionCountCap,
      };
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
      for (const [cellId, valueIds] of Object.entries(command.manualMarks)) {
        requireValueIds(document, cellId, valueIds);
      }
      const contexts = [...next.authoring.candidateContexts];
      contexts.splice(index, 0, structuredClone(command.context));
      next.authoring.candidateContexts = contexts;
      next.authoring.manualMarks[command.context.id] = structuredClone(
        command.manualMarks,
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
