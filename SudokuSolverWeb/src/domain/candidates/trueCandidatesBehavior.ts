import type { TrueCandidatesResult } from "../../solver/protocol";
import { isTrueCandidatesContext } from "../puzzle/types";
import type {
  CandidateBehaviorInput,
  CandidateContextBehavior,
  CandidateSceneProjection,
  TrueCandidateLegendEntry,
  TrueCandidatePresentation,
  TrueCandidatePresentationMap,
} from "./types";

const EMPTY_ANNOTATIONS = Object.freeze([]);
const EMPTY_CANDIDATES = Object.freeze({});
const NO_ACTIONS = Object.freeze({ manualCandidateEntry: false });

const POSSIBILITY_LEGEND = Object.freeze([
  Object.freeze({
    tone: "possible" as const,
    label: "Possible in at least one solution",
  }),
]);
const FREQUENCY_LEGEND = Object.freeze([
  Object.freeze({ tone: "frequencyLow" as const, label: "Low solution frequency" }),
  Object.freeze({ tone: "frequencyMedium" as const, label: "Medium solution frequency" }),
  Object.freeze({ tone: "frequencyHigh" as const, label: "High solution frequency" }),
]);
const COMPARISON_LEGEND = Object.freeze([
  Object.freeze({ tone: "both" as const, label: "Possible and logical" }),
  Object.freeze({ tone: "bruteForceOnly" as const, label: "Brute-force possible only" }),
  Object.freeze({ tone: "logicalOnly" as const, label: "Logical candidate only" }),
]);

export function trueCandidateLegend(
  display: "possibility" | "solutionFrequency" | "logicComparison",
): readonly TrueCandidateLegendEntry[] {
  switch (display) {
    case "possibility":
      return POSSIBILITY_LEGEND;
    case "solutionFrequency":
      return FREQUENCY_LEGEND;
    case "logicComparison":
      return COMPARISON_LEGEND;
  }
}

function frequencyPresentation(
  count: number,
  cap: number,
): TrueCandidatePresentation {
  if (count * 3 <= cap) {
    return { tone: "frequencyLow", label: "Low solution frequency" };
  }
  if (count * 3 <= cap * 2) {
    return { tone: "frequencyMedium", label: "Medium solution frequency" };
  }
  return { tone: "frequencyHigh", label: "High solution frequency" };
}

function assertResultShape(
  result: TrueCandidatesResult,
  display: "possibility" | "solutionFrequency" | "logicComparison",
): void {
  const expectedCounts = result.cellIds.length * result.valueIdsBySolverValue.length;
  if (
    result.solutionCountCap < 1 ||
    !Number.isSafeInteger(result.solutionCountCap) ||
    result.solutionCounts.length !== expectedCounts ||
    result.solutionCounts.some(
      (count) =>
        !Number.isSafeInteger(count) || count < 0 || count > result.solutionCountCap,
    )
  ) {
    throw new Error("True Candidates returned an invalid solution-count shape");
  }
  if (
    display === "logicComparison" &&
    result.logicalCandidateMasks?.length !== result.cellIds.length
  ) {
    throw new Error("True Candidates comparison returned invalid logical masks");
  }
  if (display !== "logicComparison" && result.logicalCandidateMasks !== undefined) {
    throw new Error("True Candidates returned logical masks for a non-comparison display");
  }
}

export interface TrueCandidateProjection {
  readonly candidates: Readonly<Record<string, readonly string[]>>;
  readonly presentation: TrueCandidatePresentationMap;
  readonly legend: readonly TrueCandidateLegendEntry[];
}

export function projectTrueCandidates(
  result: TrueCandidatesResult,
  display: "possibility" | "solutionFrequency" | "logicComparison",
): TrueCandidateProjection {
  assertResultShape(result, display);
  const candidates: Record<string, readonly string[]> = {};
  const presentation: Record<string, Readonly<Record<string, TrueCandidatePresentation>>> = {};
  const valueCount = result.valueIdsBySolverValue.length;
  for (let cellIndex = 0; cellIndex < result.cellIds.length; cellIndex += 1) {
    const cellId = result.cellIds[cellIndex];
    const logicalMask = result.logicalCandidateMasks?.[cellIndex] ?? 0;
    const cellCandidates: string[] = [];
    const cellPresentation: Record<string, TrueCandidatePresentation> = {};
    for (let valueIndex = 0; valueIndex < valueCount; valueIndex += 1) {
      const count = result.solutionCounts[cellIndex * valueCount + valueIndex];
      const bruteForcePossible = count > 0;
      const logicalPossible = (logicalMask & (1 << valueIndex)) !== 0;
      if (!bruteForcePossible && (display !== "logicComparison" || !logicalPossible)) {
        continue;
      }
      const valueId = result.valueIdsBySolverValue[valueIndex];
      cellCandidates.push(valueId);
      if (display === "possibility") {
        cellPresentation[valueId] = {
          tone: "possible",
          label: "Possible in at least one solution",
        };
      } else if (display === "solutionFrequency") {
        cellPresentation[valueId] = frequencyPresentation(count, result.solutionCountCap);
      } else if (bruteForcePossible && logicalPossible) {
        cellPresentation[valueId] = { tone: "both", label: "Possible and logical" };
      } else if (bruteForcePossible) {
        cellPresentation[valueId] = {
          tone: "bruteForceOnly",
          label: "Brute-force possible only",
        };
      } else {
        cellPresentation[valueId] = {
          tone: "logicalOnly",
          label: "Logical candidate only",
        };
      }
    }
    if (cellCandidates.length > 0) {
      candidates[cellId] = Object.freeze(cellCandidates);
      presentation[cellId] = Object.freeze(cellPresentation);
    }
  }
  return Object.freeze({
    candidates: Object.freeze(candidates),
    presentation: Object.freeze(presentation),
    legend: trueCandidateLegend(display),
  });
}

export const trueCandidatesBehavior: CandidateContextBehavior = Object.freeze({
  panelKind: "trueCandidates",
  actions: NO_ACTIONS,
  activate(input: CandidateBehaviorInput) {
    return {
      runtime: input.runtime,
      requestWork:
        isTrueCandidatesContext(input.definition) &&
        input.definition.refresh === "automatic" &&
        input.runtime.status === "stale" &&
        input.semanticHash !== null,
    };
  },
  invalidate(input: CandidateBehaviorInput) {
    return {
      runtime: Object.freeze({
        ...input.runtime,
        baseSemanticRevision: null,
        baseSemanticHash: null,
        status: "stale" as const,
        candidates: EMPTY_CANDIDATES,
        candidatePresentation: undefined,
        trueCandidates: undefined,
        progress: undefined,
        error: null,
      }),
      requestWork:
        input.active &&
        isTrueCandidatesContext(input.definition) &&
        input.definition.refresh === "automatic" &&
        input.semanticHash !== null,
    };
  },
  getSceneProjection(input: CandidateBehaviorInput): CandidateSceneProjection {
    return Object.freeze({
      contextId: input.definition.id,
      candidates: input.runtime.candidates,
      candidatePresentation: input.runtime.candidatePresentation,
      annotations: EMPTY_ANNOTATIONS,
    });
  },
});
