import { useMemo } from "react";

import type { AppController } from "../../app/AppController";
import { useExternalStore } from "../../app/useExternalStore";
import { PuzzleCanvas } from "../../scene/PuzzleCanvas";
import type { PuzzleSceneView } from "../../scene/types";
import type { LogicalDeduction } from "../../solver/protocol";
import "./walkthrough.css";

export interface LogicalWalkthroughProps {
  controller: AppController;
}

function LogicalHistorySummary({ deduction }: { deduction: LogicalDeduction }) {
  const affected = new Set<string>();
  for (const frame of deduction.frames) {
    for (const entity of [...frame.focus, ...frame.highlight]) {
      affected.add(`${entity.kind} ${entity.id}`);
    }
  }
  return (
    <article className="logical-history-summary">
      <strong>{deduction.techniqueId}</strong>
      <span>Affected: {[...affected].join(", ")}</span>
      {deduction.delta.placements.map((placement) => (
        <span key={`placement-${placement.cellId}-${placement.valueId}`}>
          Placement cell {placement.cellId} = value {placement.valueId}
        </span>
      ))}
      {deduction.delta.eliminations.map((elimination) => (
        <span key={`elimination-${elimination.cellId}-${elimination.valueId}`}>
          Elimination cell {elimination.cellId} ≠ value {elimination.valueId}
        </span>
      ))}
      {deduction.frames[0] === undefined ? null : (
        <small>{deduction.frames[0].explanation.key}</small>
      )}
    </article>
  );
}

export function LogicalWalkthrough({ controller }: LogicalWalkthroughProps) {
  const puzzle = useExternalStore(controller.puzzle).document;
  const editor = useExternalStore(controller.editor);
  const candidates = useExternalStore(controller.candidates);
  const validation = useExternalStore(controller.validation);
  const runtime = candidates.contexts[candidates.activeContextId];
  const logical = runtime.logical;
  const selected = logical?.availableDeductions.find(
    (deduction) => deduction.id === logical.selectedDeductionId,
  );
  const frame = selected?.frames[logical?.selectedFrameIndex ?? 0];
  const sceneView = useMemo<PuzzleSceneView>(() => ({
    values: candidates.sceneProjection.values ?? {},
    candidates: candidates.sceneProjection.candidates,
    candidatePresentation: candidates.sceneProjection.candidatePresentation,
    selectedCellIds: editor.selectedCellIds,
    annotations: candidates.sceneProjection.annotations,
    entityCapabilities: validation.capability?.entities ?? {},
  }), [candidates.sceneProjection, editor.selectedCellIds, validation.capability]);

  const back = () => {
    controller.closeWalkthrough();
    queueMicrotask(() => document.getElementById("open-logical-walkthrough")?.focus());
  };

  return (
    <section className="logical-walkthrough" role="region" aria-label="Logical walkthrough">
      <div className="logical-walkthrough__bar">
        <button type="button" aria-label="Back to workspace" onClick={back}>
          ← Back
        </button>
        <div>
          <span className="eyebrow">Logical Solver</span>
          <h1>Walkthrough</h1>
        </div>
        <span>Revision {runtime.baseSemanticRevision ?? puzzle.semanticRevision}</span>
      </div>
      <div className="logical-walkthrough__layout">
        <aside className="logical-walkthrough__history" aria-label="Logical history">
          <h2>History</h2>
          {logical?.historyDeductionIds.length === 0 ? (
            <p>No steps applied yet.</p>
          ) : (
            <ol>
              {logical?.historyDeductionIds.map((deductionId, index) => (
                <li key={`${deductionId}-${index}`}>
                  {logical.appliedDeductions[index] === undefined
                    ? deductionId
                    : <LogicalHistorySummary deduction={logical.appliedDeductions[index]} />}
                </li>
              ))}
            </ol>
          )}
          {logical?.archivedRevisions.map((archive) => (
            <section className="logical-walkthrough__archive" key={`${archive.semanticRevision}-${archive.semanticHash}`}>
              <h3>Revision {archive.semanticRevision} · archived</h3>
              <p>Read-only history</p>
              <ol>
                {archive.historyDeductionIds.map((id, index) => (
                  <li key={`${id}-${index}`}>
                    {archive.appliedDeductions[index] === undefined
                      ? id
                      : <LogicalHistorySummary deduction={archive.appliedDeductions[index]} />}
                  </li>
                ))}
              </ol>
            </section>
          ))}
        </aside>
        <div className="logical-walkthrough__canvas">
          <PuzzleCanvas
            puzzle={puzzle}
            view={sceneView}
            onSelectCell={(cellId) => controller.editor.selectOnly(cellId)}
          />
        </div>
        <aside className="logical-walkthrough__explanation" aria-label="Step explanation">
          <h2>{selected?.techniqueId ?? "No available deduction"}</h2>
          {frame === undefined ? null : (
            <div className="logical-explanation">
              <strong>{frame.explanation.key}</strong>
              <dl>
                {frame.explanation.arguments.map((argument, index) => (
                  <div key={`${argument.kind}-${argument.value}-${index}`}>
                    <dt>{argument.kind}</dt>
                    <dd>{argument.kind}: {argument.value}</dd>
                  </div>
                ))}
              </dl>
            </div>
          )}
          <div className="logical-frame-actions">
            <button
              type="button"
              aria-label="Previous Frame"
              disabled={(logical?.selectedFrameIndex ?? 0) === 0}
              onClick={() => controller.candidates.selectLogicalFrame((logical?.selectedFrameIndex ?? 0) - 1)}
            >
              Previous Frame
            </button>
            <span>{frame === undefined ? "0 / 0" : `${(logical?.selectedFrameIndex ?? 0) + 1} / ${selected?.frames.length ?? 0}`}</span>
            <button
              type="button"
              aria-label="Next Frame"
              disabled={frame === undefined || (logical?.selectedFrameIndex ?? 0) >= (selected?.frames.length ?? 1) - 1}
              onClick={() => controller.candidates.selectLogicalFrame((logical?.selectedFrameIndex ?? 0) + 1)}
            >
              Next Frame
            </button>
          </div>
          <button
            className="logical-next-step"
            type="button"
            aria-label="Next Step"
            disabled={selected === undefined || logical?.applyingDeductionId !== null}
            onClick={() => selected === undefined ? undefined : void controller.candidates.nextLogicalStep(selected.id)}
          >
            Next Step
          </button>
          <button
            type="button"
            aria-label="Reset logical session"
            disabled={runtime.status === "calculating"}
            onClick={() => controller.candidates.resetLogicalSession()}
          >
            Reset
          </button>
        </aside>
      </div>
    </section>
  );
}
