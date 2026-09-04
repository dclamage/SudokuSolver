import { computeSemanticHash } from "../domain/puzzle/computeSemanticHash";
import {
  CandidateContextController,
  type CandidateWorkScheduler,
} from "../domain/candidates/CandidateContextController";
import type { PuzzleStore } from "../domain/puzzle/PuzzleStore";
import type {
  CandidateContextId,
  CellId,
  PuzzlePackageV1,
} from "../domain/puzzle/types";
import type { ManualInputMode } from "../domain/candidates/types";
import { PlaytestSession } from "../features/playtest/PlaytestSession";
import type { SolverClient, SolverJob } from "../solver/SolverClient";
import {
  SOLVER_PROTOCOL_VERSION,
  type CapabilityResult,
  type SolverResponse,
  type ValidateSolverRequest,
} from "../solver/protocol";

export type WorkspaceId = "set" | "playtest";
export type EditorTool =
  | "select"
  | "pan"
  | "given"
  | "region"
  | "auxiliary"
  | "more";
export type MobileSheet =
  | "elements"
  | "inspector"
  | "layers"
  | "rules"
  | null;

export interface EditorSnapshot {
  readonly selectedCellIds: readonly CellId[];
  readonly viewport: Readonly<{ x: number; y: number; zoom: number }>;
  readonly activeTool: EditorTool;
  readonly activeContextId: CandidateContextId;
  readonly setterNotesInputMode: ManualInputMode;
  readonly mobileSheet: MobileSheet;
}

type StoreListener = () => void;

export class EditorStore {
  private readonly listeners = new Set<StoreListener>();
  private snapshot: EditorSnapshot = Object.freeze({
    selectedCellIds: Object.freeze([]),
    viewport: Object.freeze({ x: 0, y: 0, zoom: 1 }),
    activeTool: "select",
    activeContextId: "setter-notes",
    setterNotesInputMode: "corner",
    mobileSheet: null,
  });

  public readonly getSnapshot = (): EditorSnapshot => this.snapshot;

  public readonly subscribe = (listener: StoreListener): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public selectOnly(cellId: CellId): void {
    if (
      this.snapshot.selectedCellIds.length === 1 &&
      this.snapshot.selectedCellIds[0] === cellId
    ) {
      return;
    }
    this.update({ selectedCellIds: Object.freeze([cellId]) });
  }

  public setActiveTool(activeTool: EditorTool): void {
    const setterNotesInputMode =
      activeTool === "given"
        ? "digit"
        : this.snapshot.setterNotesInputMode === "digit"
          ? "corner"
          : this.snapshot.setterNotesInputMode;
    if (
      activeTool !== this.snapshot.activeTool ||
      setterNotesInputMode !== this.snapshot.setterNotesInputMode
    ) {
      this.update({ activeTool, setterNotesInputMode });
    }
  }

  public setActiveContext(activeContextId: CandidateContextId): void {
    if (activeContextId !== this.snapshot.activeContextId) {
      this.update({ activeContextId });
    }
  }

  public setSetterNotesInputMode(
    setterNotesInputMode: ManualInputMode,
  ): void {
    const activeTool =
      setterNotesInputMode === "digit"
        ? "given"
        : this.snapshot.activeTool === "given"
          ? "select"
          : this.snapshot.activeTool;
    if (
      setterNotesInputMode !== this.snapshot.setterNotesInputMode ||
      activeTool !== this.snapshot.activeTool
    ) {
      this.update({ setterNotesInputMode, activeTool });
    }
  }

  public setViewport(viewport: EditorSnapshot["viewport"]): void {
    if (
      viewport.x !== this.snapshot.viewport.x ||
      viewport.y !== this.snapshot.viewport.y ||
      viewport.zoom !== this.snapshot.viewport.zoom
    ) {
      this.update({ viewport: Object.freeze({ ...viewport }) });
    }
  }

  public setMobileSheet(mobileSheet: MobileSheet): void {
    if (mobileSheet !== this.snapshot.mobileSheet) {
      this.update({ mobileSheet });
    }
  }

  private update(change: Partial<EditorSnapshot>): void {
    this.snapshot = Object.freeze({ ...this.snapshot, ...change });
    for (const listener of [...this.listeners]) {
      listener();
    }
  }
}

export type DocumentValidationStatus =
  | "validating"
  | "valid"
  | "partial"
  | "visualOnly"
  | "coverageUnknown"
  | "invalid"
  | "error";

export interface DocumentValidationSnapshot {
  readonly status: DocumentValidationStatus;
  readonly documentRevision: number;
  readonly semanticRevision: number;
  readonly semanticHash: string | null;
  readonly capability: CapabilityResult | null;
  readonly error: string | null;
}

export class DocumentValidationStore {
  private readonly listeners = new Set<StoreListener>();
  private generation = 0;
  private activeJob: SolverJob | null = null;
  private snapshot: DocumentValidationSnapshot;

  public constructor(
    private readonly solver: SolverClient,
    private readonly createRequestId: () => string,
    initialDocument: PuzzlePackageV1,
  ) {
    this.snapshot = Object.freeze({
      status: "validating",
      documentRevision: initialDocument.revision,
      semanticRevision: initialDocument.semanticRevision,
      semanticHash: null,
      capability: null,
      error: null,
    });
  }

  public readonly getSnapshot = (): DocumentValidationSnapshot =>
    this.snapshot;

  public readonly subscribe = (listener: StoreListener): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public validate(document: PuzzlePackageV1): void {
    this.generation += 1;
    const generation = this.generation;
    this.activeJob?.cancel();
    this.activeJob = null;
    this.setSnapshot({
      status: "validating",
      documentRevision: document.revision,
      semanticRevision: document.semanticRevision,
      semanticHash: null,
      capability: null,
      error: null,
    });

    void computeSemanticHash(document)
      .then((semanticHash) => {
        if (generation !== this.generation) {
          return;
        }
        const projection = document.solverProjections[0];
        if (projection === undefined) {
          this.setError(generation, "Puzzle has no solver projection");
          return;
        }
        const request: ValidateSolverRequest = {
          protocolVersion: SOLVER_PROTOCOL_VERSION,
          requestId: this.createRequestId(),
          operation: "validate",
          documentRevision: document.revision,
          semanticRevision: document.semanticRevision,
          semanticHash,
          contextId: "document-validation",
          puzzle: document,
          validateOptions: { projectionId: projection.id },
        };
        this.setSnapshot({ ...this.snapshot, semanticHash });
        let job: SolverJob;
        try {
          job = this.solver.start(request);
        } catch (error) {
          this.setError(generation, errorMessage(error));
          return;
        }
        this.activeJob = job;
        void job.result
          .then((response) =>
            this.applyResponse(generation, request, response),
          )
          .catch((error: unknown) => {
            this.setError(generation, errorMessage(error));
          });
      })
      .catch((error: unknown) => {
        this.setError(generation, errorMessage(error));
      });
  }

  public dispose(): void {
    this.generation += 1;
    this.activeJob?.cancel();
    this.activeJob = null;
    this.listeners.clear();
  }

  private applyResponse(
    generation: number,
    request: ValidateSolverRequest,
    response: SolverResponse,
  ): void {
    if (generation !== this.generation) {
      return;
    }
    this.activeJob = null;
    if (!isCorrelatedValidation(request, response)) {
      this.setError(generation, "Solver validation response was stale");
      return;
    }
    if (response.kind !== "result" || response.capability === undefined) {
      this.setError(generation, "Solver returned no capability report");
      return;
    }
    this.setSnapshot({
      status: deriveValidationStatus(response.capability),
      documentRevision: response.documentRevision,
      semanticRevision: response.semanticRevision,
      semanticHash: response.semanticHash,
      capability: response.capability,
      error: null,
    });
  }

  private setError(generation: number, message: string): void {
    if (generation !== this.generation) {
      return;
    }
    this.activeJob = null;
    this.setSnapshot({
      ...this.snapshot,
      status: "error",
      error: message,
    });
  }

  private setSnapshot(snapshot: DocumentValidationSnapshot): void {
    this.snapshot = Object.freeze(snapshot);
    for (const listener of [...this.listeners]) {
      listener();
    }
  }
}

export interface AppControllerSnapshot {
  readonly workspace: WorkspaceId;
  readonly walkthroughOpen: boolean;
}

export interface AppControllerOptions {
  readonly puzzle: PuzzleStore;
  readonly solver: SolverClient;
  readonly createRequestId?: () => string;
  readonly now?: () => number;
  readonly persist?: (document: PuzzlePackageV1) => void;
  readonly scheduleCandidateWork?: CandidateWorkScheduler;
}

export class AppController {
  public readonly puzzle: PuzzleStore;
  public readonly editor = new EditorStore();
  public readonly playtest: PlaytestSession;
  public readonly validation: DocumentValidationStore;
  public readonly candidates: CandidateContextController;
  public readonly solver: SolverClient;
  public readonly hasPersistence: boolean;

  private readonly listeners = new Set<StoreListener>();
  private readonly unsubscribePuzzle: () => void;
  private candidateHashGeneration = 0;
  private snapshot: AppControllerSnapshot = Object.freeze({
    workspace: "set",
    walkthroughOpen: false,
  });

  public constructor(options: AppControllerOptions) {
    this.puzzle = options.puzzle;
    this.solver = options.solver;
    this.hasPersistence = options.persist !== undefined;
    this.playtest = new PlaytestSession(options.now);
    const createRequestId =
      options.createRequestId ?? (() => crypto.randomUUID());
    const initialDocument = options.puzzle.getSnapshot().document;
    this.candidates = new CandidateContextController({
      definitions: initialDocument.authoring.candidateContexts,
      activeContextId: this.editor.getSnapshot().activeContextId,
      document: initialDocument,
      semanticHash: null,
      solver: options.solver,
      createRequestId,
      schedule: options.scheduleCandidateWork,
      onActiveContextChanged: (contextId) => {
        this.editor.setActiveContext(contextId);
        this.closeWalkthrough();
      },
    });
    this.validation = new DocumentValidationStore(
      options.solver,
      createRequestId,
      initialDocument,
    );
    this.unsubscribePuzzle = this.puzzle.subscribe(() => {
      const puzzleSnapshot = this.puzzle.getSnapshot();
      options.persist?.(puzzleSnapshot.document);
      if (puzzleSnapshot.lastChange?.semantic === true) {
        this.validation.validate(puzzleSnapshot.document);
        this.updateCandidateSemanticIdentity(puzzleSnapshot.document);
      } else {
        this.candidates.onPuzzleChanged({
          documentRevision: puzzleSnapshot.document.revision,
          semanticRevision: puzzleSnapshot.document.semanticRevision,
          semantic: false,
          document: puzzleSnapshot.document,
        });
      }
    });
    this.validation.validate(initialDocument);
    this.updateCandidateSemanticIdentity(initialDocument);
  }

  public readonly getSnapshot = (): AppControllerSnapshot => this.snapshot;

  public readonly subscribe = (listener: StoreListener): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public setWorkspace(workspace: WorkspaceId): void {
    if (workspace === this.snapshot.workspace) {
      return;
    }
    if (workspace === "playtest") {
      this.playtest.resumeTimer();
    } else {
      this.playtest.pauseTimer();
    }
    this.update({ workspace, walkthroughOpen: false });
  }

  public openWalkthrough(): void {
    if (
      !this.snapshot.walkthroughOpen &&
      this.candidates.getPanelDescriptor().panelKind === "logicalSolver"
    ) {
      this.update({ walkthroughOpen: true });
    }
  }

  public closeWalkthrough(): void {
    if (this.snapshot.walkthroughOpen) {
      this.update({ walkthroughOpen: false });
    }
  }

  public dispose(): void {
    this.unsubscribePuzzle();
    this.candidateHashGeneration += 1;
    this.candidates.dispose();
    this.validation.dispose();
    this.solver.dispose();
    this.listeners.clear();
  }

  private update(change: Partial<AppControllerSnapshot>): void {
    this.snapshot = Object.freeze({ ...this.snapshot, ...change });
    for (const listener of [...this.listeners]) {
      listener();
    }
  }

  private updateCandidateSemanticIdentity(document: PuzzlePackageV1): void {
    this.candidateHashGeneration += 1;
    const generation = this.candidateHashGeneration;
    this.candidates.onPuzzleChanged({
      documentRevision: document.revision,
      semanticRevision: document.semanticRevision,
      semanticHash: null,
      semantic: true,
      document,
    });
    void computeSemanticHash(document).then((semanticHash) => {
      if (generation !== this.candidateHashGeneration) {
        return;
      }
      const currentDocument = this.puzzle.getSnapshot().document;
      if (
        currentDocument.semanticRevision !== document.semanticRevision
      ) {
        return;
      }
      this.candidates.onPuzzleChanged({
        documentRevision: currentDocument.revision,
        semanticRevision: currentDocument.semanticRevision,
        semanticHash,
        semantic: true,
      });
    });
  }
}

export function documentValidationLabel(
  snapshot: DocumentValidationSnapshot,
): string {
  switch (snapshot.status) {
    case "validating":
      return "Checking puzzle…";
    case "valid":
      return "Valid · Fully solver-supported";
    case "partial":
      return "Validation limited · Partial solver coverage";
    case "visualOnly":
      return "Validation limited · Visual-only semantics";
    case "coverageUnknown":
      return "Validation complete · Coverage not reported";
    case "invalid":
      return snapshot.capability?.contradiction === true
        ? "Contradiction found"
        : "Invalid puzzle definition";
    case "error":
      return "Validation unavailable";
  }
}

function deriveValidationStatus(
  capability: CapabilityResult,
): DocumentValidationStatus {
  if (capability.contradiction) {
    return "invalid";
  }
  const statuses = Object.values(capability.entities).map(
    (entity) => entity.status,
  );
  if (statuses.includes("invalidDefinition")) {
    return "invalid";
  }
  if (statuses.includes("visualOnly")) {
    return "visualOnly";
  }
  if (statuses.includes("partiallyVerified")) {
    return "partial";
  }
  return statuses.length === 0 ? "coverageUnknown" : "valid";
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

function isCorrelatedValidation(
  request: ValidateSolverRequest,
  response: SolverResponse,
): boolean {
  return (
    response.protocolVersion === request.protocolVersion &&
    response.requestId === request.requestId &&
    response.operation === "validate" &&
    response.documentRevision === request.documentRevision &&
    response.semanticRevision === request.semanticRevision &&
    response.semanticHash === request.semanticHash &&
    response.contextId === request.contextId
  );
}
