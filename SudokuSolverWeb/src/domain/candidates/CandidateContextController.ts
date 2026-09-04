import type { SolverClient, SolverJob } from "../../solver/SolverClient";
import {
  SOLVER_PROTOCOL_VERSION,
  type SolverResponse,
  type TrueCandidatesResult,
  type TrueCandidatesSolverRequest,
} from "../../solver/protocol";
import type {
  CandidateContext,
  CandidateContextId,
  PuzzlePackageV1,
  TrueCandidatesContext,
} from "../puzzle/types";
import {
  assertValidCandidateContext,
  isLogicalSolverCandidateContext,
  isManualCandidateContext,
  isTrueCandidatesContext,
} from "../puzzle/types";
import type {
  CandidateBehaviorInput,
  CandidateBehaviorRegistry,
  CandidateBehaviorTransition,
  CandidateContextBehavior,
  CandidateContextRuntime,
  CandidateContextSnapshot,
  CandidatePanelDescriptor,
  CandidateSceneProjection,
} from "./types";
import { manualCandidateBehavior } from "./manualCandidateBehavior";
import {
  projectTrueCandidates,
  trueCandidatesBehavior,
} from "./trueCandidatesBehavior";

const EMPTY_CANDIDATES = Object.freeze({});
const EMPTY_ANNOTATIONS = Object.freeze([]);
const NO_ACTIONS = Object.freeze({ manualCandidateEntry: false });

export interface CandidatePuzzleChange {
  readonly documentRevision: number;
  readonly semanticRevision: number;
  readonly semanticHash?: string | null;
  readonly semantic: boolean;
  readonly document?: PuzzlePackageV1;
}

export type CandidateWorkScheduler = (
  run: () => void,
) => () => void;

export interface CandidateContextControllerOptions {
  readonly definitions: readonly CandidateContext[];
  readonly activeContextId: CandidateContextId;
  readonly document: PuzzlePackageV1;
  readonly semanticHash: string | null;
  readonly solver: SolverClient;
  readonly createRequestId: () => string;
  readonly schedule?: CandidateWorkScheduler;
  readonly behaviors?: Partial<CandidateBehaviorRegistry>;
  readonly onActiveContextChanged?: (
    contextId: CandidateContextId,
  ) => void;
}

type StoreListener = () => void;

interface ScheduledWork {
  readonly contextId: CandidateContextId;
  cancel: () => void;
}

interface ActiveWork {
  readonly contextId: CandidateContextId;
  readonly generation: number;
  readonly documentId: string;
  readonly configurationKey: string;
  readonly request: TrueCandidatesSolverRequest;
  readonly job: SolverJob;
  readonly unsubscribeProgress: () => void;
}

interface DefinitionReconciliation {
  readonly changedContextIds: ReadonlySet<CandidateContextId>;
  readonly activeReplaced: boolean;
}

function unchanged(input: CandidateBehaviorInput): CandidateBehaviorTransition {
  return { runtime: input.runtime, requestWork: false };
}

function runtimeProjection(
  input: CandidateBehaviorInput,
): CandidateSceneProjection {
  return Object.freeze({
    contextId: input.definition.id,
    candidates: input.runtime.candidates,
    annotations: EMPTY_ANNOTATIONS,
  });
}

function inertProjection(
  input: CandidateBehaviorInput,
): CandidateSceneProjection {
  return Object.freeze({
    contextId: input.definition.id,
    candidates: EMPTY_CANDIDATES,
    annotations: EMPTY_ANNOTATIONS,
  });
}

function withStatus(
  runtime: CandidateContextRuntime,
  status: CandidateContextRuntime["status"],
  semanticRevision: number | null,
  semanticHash: string | null,
): CandidateContextRuntime {
  return Object.freeze({
    ...runtime,
    baseSemanticRevision: semanticRevision,
    baseSemanticHash: semanticHash,
    status,
    error: null,
  });
}

const logicalSolverBehavior: CandidateContextBehavior = Object.freeze({
  panelKind: "logicalSolver",
  actions: NO_ACTIONS,
  activate: unchanged,
  invalidate(input: CandidateBehaviorInput) {
    return {
      runtime: withStatus(input.runtime, "stale", null, null),
      requestWork: false,
    };
  },
  getSceneProjection: runtimeProjection,
});

const unsupportedBehavior: CandidateContextBehavior = Object.freeze({
  panelKind: "unsupported",
  actions: NO_ACTIONS,
  activate: unchanged,
  invalidate: unchanged,
  getSceneProjection: inertProjection,
});

export const defaultCandidateBehaviorRegistry: CandidateBehaviorRegistry =
  Object.freeze({
    manual: manualCandidateBehavior,
    trueCandidates: trueCandidatesBehavior,
    logicalSolver: logicalSolverBehavior,
  });

function defaultSchedule(run: () => void): () => void {
  const timeout = globalThis.setTimeout(run, 250);
  return () => globalThis.clearTimeout(timeout);
}

function initialRuntime(
  definition: CandidateContext,
  document: PuzzlePackageV1,
  semanticHash: string | null,
): CandidateContextRuntime {
  const manual = isManualCandidateContext(definition);
  const supported =
    manual ||
    isTrueCandidatesContext(definition) ||
    isLogicalSolverCandidateContext(definition);
  return Object.freeze({
    contextId: definition.id,
    baseSemanticRevision: manual ? document.semanticRevision : null,
    baseSemanticHash: manual ? semanticHash : null,
    status: manual ? "live" : supported ? "stale" : "idle",
    candidates: manual
      ? manualCornerMarks(document, definition.id)
      : EMPTY_CANDIDATES,
    error: null,
  });
}

function manualCornerMarks(
  document: PuzzlePackageV1,
  contextId: CandidateContextId,
): Readonly<Record<string, readonly string[]>> {
  return Object.fromEntries(
    Object.entries(document.authoring.manualMarks[contextId] ?? {})
      .filter(([, notes]) => notes.corner.length > 0)
      .map(([cellId, notes]) => [cellId, notes.corner]),
  );
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

export class CandidateContextController {
  private readonly listeners = new Set<StoreListener>();
  private readonly solver: SolverClient;
  private readonly createRequestId: () => string;
  private readonly schedule: CandidateWorkScheduler;
  private readonly behaviors: CandidateBehaviorRegistry;
  private readonly onActiveContextChanged?: (
    contextId: CandidateContextId,
  ) => void;

  private document: PuzzlePackageV1;
  private documentRevision: number;
  private semanticRevision: number;
  private semanticHash: string | null;
  private definitions: readonly CandidateContext[];
  private activeContextId: CandidateContextId;
  private contexts: Readonly<
    Record<CandidateContextId, CandidateContextRuntime>
  >;
  private scheduledWork: ScheduledWork | null = null;
  private activeWork: ActiveWork | null = null;
  private workGeneration = 0;
  private snapshot: CandidateContextSnapshot;

  public constructor(options: CandidateContextControllerOptions) {
    for (const definition of options.definitions) {
      assertValidCandidateContext(definition);
    }
    const activeDefinition = options.definitions.find(
      (definition) => definition.id === options.activeContextId,
    );
    if (activeDefinition === undefined) {
      throw new Error(
        `missing active candidate context ${options.activeContextId}`,
      );
    }
    this.document = options.document;
    this.documentRevision = options.document.revision;
    this.semanticRevision = options.document.semanticRevision;
    this.semanticHash = options.semanticHash;
    this.definitions = Object.freeze([...options.definitions]);
    this.activeContextId = options.activeContextId;
    this.solver = options.solver;
    this.createRequestId = options.createRequestId;
    this.schedule = options.schedule ?? defaultSchedule;
    this.behaviors = Object.freeze({
      ...defaultCandidateBehaviorRegistry,
      ...options.behaviors,
    });
    this.onActiveContextChanged = options.onActiveContextChanged;
    this.contexts = Object.freeze(
      Object.fromEntries(
        this.definitions.map((definition) => [
          definition.id,
          initialRuntime(
            definition,
            this.document,
            this.semanticHash,
          ),
        ]),
      ),
    );
    this.snapshot = this.createSnapshot();
  }

  public readonly getSnapshot = (): CandidateContextSnapshot => this.snapshot;

  public readonly subscribe = (listener: StoreListener): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public getSceneProjection(): CandidateSceneProjection {
    return this.snapshot.sceneProjection;
  }

  public getPanelDescriptor(): CandidatePanelDescriptor {
    return this.snapshot.panel;
  }

  public activate(contextId: CandidateContextId): void {
    if (contextId === this.activeContextId) {
      return;
    }
    const definition = this.findDefinition(contextId);
    this.cancelWork(true);
    this.activeContextId = contextId;
    this.onActiveContextChanged?.(contextId);
    const transition = this.behaviorFor(definition).activate(
      this.behaviorInput(definition, true),
    );
    this.contexts = Object.freeze({
      ...this.contexts,
      [contextId]: transition.runtime,
    });
    this.publish();
    if (transition.requestWork) {
      this.scheduleTrueCandidateWork(definition);
    }
  }

  public refresh(): void {
    const definition = this.findDefinition(this.activeContextId);
    if (!isTrueCandidatesContext(definition)) {
      return;
    }
    this.scheduleTrueCandidateWork(definition);
  }

  public cancel(): void {
    const contextId = this.activeContextId;
    if (!isTrueCandidatesContext(this.findDefinition(contextId))) {
      return;
    }
    this.cancelWork(true);
    this.publish();
  }

  public onPuzzleChanged(change: CandidatePuzzleChange): void {
    if (change.document !== undefined) {
      for (const definition of change.document.authoring.candidateContexts) {
        assertValidCandidateContext(definition);
      }
    }
    const nextSemanticHash =
      change.semanticHash === undefined
        ? this.semanticHash
        : change.semanticHash;
    const semanticIdentityChanged =
      change.semanticRevision !== this.semanticRevision ||
      nextSemanticHash !== this.semanticHash;
    const documentIdentityChanged =
      change.document !== undefined && change.document.id !== this.document.id;
    this.documentRevision = change.documentRevision;
    this.semanticRevision = change.semanticRevision;
    this.semanticHash = nextSemanticHash;
    let reconciliation: DefinitionReconciliation = {
      changedContextIds: new Set(),
      activeReplaced: false,
    };
    if (change.document !== undefined) {
      this.document = change.document;
      reconciliation = this.reconcileDefinitions(
        change.document.authoring.candidateContexts,
      );
    }
    if (
      !semanticIdentityChanged &&
      !documentIdentityChanged &&
      reconciliation.changedContextIds.size === 0 &&
      !reconciliation.activeReplaced
    ) {
      this.publish();
      return;
    }

    if (
      semanticIdentityChanged ||
      documentIdentityChanged ||
      reconciliation.activeReplaced ||
      reconciliation.changedContextIds.has(this.activeContextId)
    ) {
      this.cancelWork(true);
    }
    let nextContexts = this.contexts;
    let shouldRequestActiveWork = false;
    for (const definition of this.definitions) {
      if (
        !semanticIdentityChanged &&
        !documentIdentityChanged &&
        !reconciliation.changedContextIds.has(definition.id)
      ) {
        continue;
      }
      const transition = this.behaviorFor(definition).invalidate(
        this.behaviorInput(definition, definition.id === this.activeContextId),
      );
      if (transition.runtime !== nextContexts[definition.id]) {
        nextContexts = Object.freeze({
          ...nextContexts,
          [definition.id]: transition.runtime,
        });
      }
      if (definition.id === this.activeContextId) {
        shouldRequestActiveWork = transition.requestWork;
      }
    }
    if (reconciliation.activeReplaced) {
      const activeDefinition = this.findDefinition(this.activeContextId);
      const transition = this.behaviorFor(activeDefinition).activate({
        ...this.behaviorInput(activeDefinition, true),
        runtime: nextContexts[this.activeContextId],
      });
      nextContexts = Object.freeze({
        ...nextContexts,
        [this.activeContextId]: transition.runtime,
      });
      shouldRequestActiveWork = transition.requestWork;
    }
    this.contexts = nextContexts;
    this.publish();
    if (shouldRequestActiveWork) {
      this.scheduleTrueCandidateWork(this.findDefinition(this.activeContextId));
    }
  }

  public dispose(): void {
    this.cancelWork(false);
    this.listeners.clear();
  }

  private reconcileDefinitions(
    definitions: readonly CandidateContext[],
  ): DefinitionReconciliation {
    if (definitions.length === 0) {
      throw new Error("candidate contexts cannot be empty");
    }
    const previousDefinitions = new Map(
      this.definitions.map((definition) => [definition.id, definition]),
    );
    const nextDefinitions = Object.freeze([...definitions]);
    const nextContexts: Record<CandidateContextId, CandidateContextRuntime> = {};
    const changedContextIds = new Set<CandidateContextId>();
    for (const definition of nextDefinitions) {
      const existing = this.contexts[definition.id];
      const previousDefinition = previousDefinitions.get(definition.id);
      if (
        previousDefinition === undefined ||
        behaviorConfigurationChanged(previousDefinition, definition)
      ) {
        changedContextIds.add(definition.id);
      }
      if (
        existing === undefined ||
        previousDefinition?.kind !== definition.kind
      ) {
        nextContexts[definition.id] = initialRuntime(
          definition,
          this.document,
          this.semanticHash,
        );
      } else if (isManualCandidateContext(definition)) {
        nextContexts[definition.id] = Object.freeze({
          ...existing,
          candidates: manualCornerMarks(this.document, definition.id),
        });
      } else {
        nextContexts[definition.id] = existing;
      }
    }
    this.definitions = nextDefinitions;
    this.contexts = Object.freeze(nextContexts);
    let activeReplaced = false;
    if (!nextContexts[this.activeContextId]) {
      this.activeContextId = nextDefinitions[0].id;
      activeReplaced = true;
      this.onActiveContextChanged?.(this.activeContextId);
    }
    return { changedContextIds, activeReplaced };
  }

  private scheduleTrueCandidateWork(definition: CandidateContext): void {
    if (
      definition.id !== this.activeContextId ||
      !isTrueCandidatesContext(definition) ||
      this.semanticHash === null
    ) {
      return;
    }
    const projection = this.document.solverProjections[0];
    if (projection === undefined) {
      this.setRuntimeError(definition.id, "Puzzle has no solver projection");
      return;
    }
    this.cancelWork(false);
    this.setRuntime(
      definition.id,
      withStatus(this.contexts[definition.id], "calculating", null, null),
    );
    const scheduled: ScheduledWork = {
      contextId: definition.id,
      cancel: () => undefined,
    };
    this.scheduledWork = scheduled;
    const cancel = this.schedule(() => {
      if (this.scheduledWork !== scheduled) {
        return;
      }
      this.scheduledWork = null;
      this.startTrueCandidateWork(definition, projection.id);
    });
    if (this.scheduledWork === scheduled) {
      scheduled.cancel = cancel;
    }
  }

  private startTrueCandidateWork(
    definition: TrueCandidatesContext,
    projectionId: string,
  ): void {
    if (
      definition.id !== this.activeContextId ||
      this.semanticHash === null
    ) {
      return;
    }
    const currentDefinition = this.findDefinition(definition.id);
    if (
      !isTrueCandidatesContext(currentDefinition) ||
      trueCandidateConfigurationKey(currentDefinition) !==
        trueCandidateConfigurationKey(definition)
    ) {
      return;
    }
    const request: TrueCandidatesSolverRequest = {
      protocolVersion: SOLVER_PROTOCOL_VERSION,
      requestId: this.createRequestId(),
      operation: "trueCandidates",
      documentRevision: this.documentRevision,
      semanticRevision: this.semanticRevision,
      semanticHash: this.semanticHash,
      contextId: definition.id,
      puzzle: this.document,
      trueCandidatesOptions: {
        projectionId,
        display: definition.display,
        solutionCountCap:
          definition.display === "solutionFrequency"
            ? definition.solutionCountCap
            : 1,
      },
    };
    let job: SolverJob;
    try {
      job = this.solver.start(request);
    } catch (error) {
      this.setRuntimeError(definition.id, errorMessage(error));
      return;
    }
    const generation = ++this.workGeneration;
    let unsubscribeProgress: () => void = () => undefined;
    const activeWork: ActiveWork = {
      contextId: definition.id,
      generation,
      documentId: this.document.id,
      configurationKey: trueCandidateConfigurationKey(definition),
      request,
      job,
      unsubscribeProgress: () => unsubscribeProgress(),
    };
    unsubscribeProgress = job.subscribe((response) =>
      this.applyProgress(activeWork, response),
    );
    this.activeWork = activeWork;
    void job.result
      .then((response) => this.applyResponse(activeWork, response))
      .catch((error: unknown) => {
        if (!this.isCurrentWork(activeWork)) {
          return;
        }
        activeWork.unsubscribeProgress();
        this.activeWork = null;
        this.setRuntimeError(definition.id, errorMessage(error));
      });
  }

  private applyProgress(work: ActiveWork, response: SolverResponse): void {
    if (
      !this.isCurrentWork(work) ||
      response.kind !== "progress" ||
      !isCorrelatedCandidateResponse(work.request, response) ||
      response.trueCandidates === undefined
    ) {
      return;
    }
    try {
      this.applyTrueCandidateResult(work, response.trueCandidates, "calculating");
    } catch (error) {
      work.job.cancel();
      work.unsubscribeProgress();
      this.activeWork = null;
      this.setRuntimeError(work.contextId, errorMessage(error));
    }
  }

  private applyResponse(work: ActiveWork, response: SolverResponse): void {
    if (!this.isCurrentWork(work)) {
      return;
    }
    work.unsubscribeProgress();
    this.activeWork = null;
    if (!isCorrelatedCandidateResponse(work.request, response)) {
      this.setRuntime(
        work.contextId,
        withStatus(this.contexts[work.contextId], "stale", null, null),
      );
      return;
    }
    if (response.kind === "error") {
      this.setRuntimeError(work.contextId, response.error.message);
      return;
    }
    if (response.kind !== "result" || response.trueCandidates === undefined) {
      this.setRuntime(
        work.contextId,
        withStatus(this.contexts[work.contextId], "stale", null, null),
      );
      return;
    }
    try {
      this.applyTrueCandidateResult(work, response.trueCandidates, "live");
    } catch (error) {
      this.setRuntimeError(work.contextId, errorMessage(error));
    }
  }

  private applyTrueCandidateResult(
    work: ActiveWork,
    result: TrueCandidatesResult,
    status: "calculating" | "live",
  ): void {
    const definition = this.findDefinition(work.contextId);
    if (!isTrueCandidatesContext(definition)) {
      return;
    }
    this.validateTrueCandidateResult(work, result);
    const projection = projectTrueCandidates(result, definition.display);
    this.setRuntime(
      work.contextId,
      Object.freeze({
        ...this.contexts[work.contextId],
        baseSemanticRevision:
          status === "live" ? work.request.semanticRevision : null,
        baseSemanticHash: status === "live" ? work.request.semanticHash : null,
        status,
        candidates: projection.candidates,
        candidatePresentation: projection.presentation,
        trueCandidates: Object.freeze({
          solutionCounts: Object.freeze([...result.solutionCounts]),
          logicalCandidateMasks:
            result.logicalCandidateMasks === undefined
              ? undefined
              : Object.freeze([...result.logicalCandidateMasks]),
          solutionCountCap: result.solutionCountCap,
          legend: projection.legend,
        }),
        progress: Object.freeze({
          discoveredCandidates: result.solutionCounts.filter((count) => count > 0).length,
          candidateSlots: result.solutionCounts.length,
        }),
        error: null,
      }),
    );
  }

  private validateTrueCandidateResult(
    work: ActiveWork,
    result: TrueCandidatesResult,
  ): void {
    const projection = work.request.puzzle.solverProjections.find(
      (candidate) =>
        candidate.id === work.request.trueCandidatesOptions.projectionId,
    );
    if (projection === undefined) {
      throw new Error(
        "True Candidates returned identifiers outside the requested projection",
      );
    }
    const expectedCellIds = projection.cellIdsByRow.flat();
    if (
      expectedCellIds.length !== result.cellIds.length ||
      expectedCellIds.some((cellId, index) => cellId !== result.cellIds[index]) ||
      projection.valueIdsBySolverValue.length !==
        result.valueIdsBySolverValue.length ||
      projection.valueIdsBySolverValue.some(
        (valueId, index) => valueId !== result.valueIdsBySolverValue[index],
      )
    ) {
      throw new Error(
        "True Candidates returned identifiers outside the requested projection",
      );
    }
    if (
      result.solutionCountCap !==
      work.request.trueCandidatesOptions.solutionCountCap
    ) {
      throw new Error(
        "True Candidates returned a count cap outside the current configuration",
      );
    }
  }

  private isCurrentWork(work: ActiveWork): boolean {
    if (
      this.activeWork !== work ||
      work.generation !== this.workGeneration ||
      work.contextId !== this.activeContextId ||
      work.documentId !== this.document.id ||
      work.request.semanticRevision !== this.semanticRevision ||
      work.request.semanticHash !== this.semanticHash
    ) {
      return false;
    }
    const definition = this.definitions.find(
      (candidate) => candidate.id === work.contextId,
    );
    return (
      definition !== undefined &&
      isTrueCandidatesContext(definition) &&
      trueCandidateConfigurationKey(definition) === work.configurationKey
    );
  }

  private cancelWork(markStale: boolean): void {
    const contextId =
      this.activeWork?.contextId ?? this.scheduledWork?.contextId;
    this.scheduledWork?.cancel();
    this.scheduledWork = null;
    this.activeWork?.unsubscribeProgress();
    this.activeWork?.job.cancel();
    this.activeWork = null;
    this.workGeneration += 1;
    if (
      markStale &&
      contextId !== undefined &&
      this.contexts[contextId]?.status === "calculating"
    ) {
      this.contexts = Object.freeze({
        ...this.contexts,
        [contextId]: withStatus(
          this.contexts[contextId],
          "stale",
          null,
          null,
        ),
      });
    }
  }

  private setRuntime(
    contextId: CandidateContextId,
    runtime: CandidateContextRuntime,
  ): void {
    this.contexts = Object.freeze({
      ...this.contexts,
      [contextId]: runtime,
    });
    this.publish();
  }

  private setRuntimeError(contextId: CandidateContextId, message: string): void {
    this.setRuntime(
      contextId,
      Object.freeze({
        ...this.contexts[contextId],
        status: "error",
        error: message,
      }),
    );
  }

  private behaviorInput(
    definition: CandidateContext,
    active: boolean,
  ): CandidateBehaviorInput {
    return {
      definition,
      runtime: this.contexts[definition.id],
      semanticRevision: this.semanticRevision,
      semanticHash: this.semanticHash,
      active,
      manualMarks: isManualCandidateContext(definition)
        ? this.document.authoring.manualMarks[definition.id]
        : undefined,
    };
  }

  private findDefinition(contextId: CandidateContextId): CandidateContext {
    const definition = this.definitions.find(
      (candidate) => candidate.id === contextId,
    );
    if (definition === undefined) {
      throw new Error(`missing candidate context ${contextId}`);
    }
    return definition;
  }

  private behaviorFor(definition: CandidateContext): CandidateContextBehavior {
    return this.behaviors[definition.kind] ?? unsupportedBehavior;
  }

  private createSnapshot(): CandidateContextSnapshot {
    const definition = this.findDefinition(this.activeContextId);
    const behavior = this.behaviorFor(definition);
    const input = this.behaviorInput(definition, true);
    const panel = Object.freeze({
      contextId: definition.id,
      name: definition.name,
      contextKind: definition.kind,
      panelKind: behavior.panelKind,
      status: input.runtime.status,
    });
    return Object.freeze({
      activeContextId: definition.id,
      definitions: this.definitions,
      contexts: this.contexts,
      sceneProjection: behavior.getSceneProjection(input),
      panel,
      actions: behavior.actions,
    });
  }

  private publish(): void {
    this.snapshot = this.createSnapshot();
    for (const listener of [...this.listeners]) {
      listener();
    }
  }
}

function isCorrelatedCandidateResponse(
  request: TrueCandidatesSolverRequest,
  response: SolverResponse,
): boolean {
  return (
    response.protocolVersion === request.protocolVersion &&
    response.requestId === request.requestId &&
    response.operation === request.operation &&
    response.semanticRevision === request.semanticRevision &&
    response.semanticHash === request.semanticHash &&
    response.contextId === request.contextId
  );
}

function trueCandidateConfigurationKey(
  definition: TrueCandidatesContext,
): string {
  return `${definition.refresh}:${definition.display}:${definition.solutionCountCap}`;
}

function behaviorConfigurationChanged(
  previous: CandidateContext,
  next: CandidateContext,
): boolean {
  if (previous.kind !== next.kind) {
    return true;
  }
  if (isTrueCandidatesContext(previous) && isTrueCandidatesContext(next)) {
    return (
      previous.refresh !== next.refresh ||
      previous.display !== next.display ||
      previous.solutionCountCap !== next.solutionCountCap
    );
  }
  if (
    isLogicalSolverCandidateContext(previous) &&
    isLogicalSolverCandidateContext(next)
  ) {
    return (
      previous.followPuzzleRevision !== next.followPuzzleRevision ||
      previous.enabledTechniqueIds.length !== next.enabledTechniqueIds.length ||
      previous.enabledTechniqueIds.some(
        (techniqueId, index) => techniqueId !== next.enabledTechniqueIds[index],
      )
    );
  }
  return false;
}
