import type { AppController } from "../../app/AppController";
import { useExternalStore } from "../../app/useExternalStore";

export interface LogicalSolverPanelProps {
  controller: AppController;
}

function affectedEntities(
  deduction: NonNullable<ReturnType<AppController["candidates"]["getSnapshot"]>["contexts"][string]["logical"]>["availableDeductions"][number],
) {
  const entities = new Set<string>();
  for (const frame of deduction.frames) {
    for (const reference of [...frame.focus, ...frame.highlight]) {
      entities.add(`${reference.kind} ${reference.id}`);
    }
  }
  return [...entities].join(", ");
}

export function LogicalSolverPanel({ controller }: LogicalSolverPanelProps) {
  const snapshot = useExternalStore(controller.candidates);
  const runtime = snapshot.contexts[snapshot.activeContextId];
  const logical = runtime.logical;
  const selected = logical?.availableDeductions.find(
    (deduction) => deduction.id === logical.selectedDeductionId,
  );

  return (
    <div className="logical-solver-panel">
      {runtime.error === null ? null : <p role="alert">{runtime.error}</p>}
      {selected === undefined ? (
        <p>
          {runtime.status === "calculating"
            ? "Finding logical deductions…"
            : "No logical deduction is currently available."}
        </p>
      ) : (
        <div className="logical-solver-panel__current">
          <strong>{selected.techniqueId}</strong>
          <span>Affects {affectedEntities(selected)}</span>
        </div>
      )}
      {(logical?.availableDeductions.length ?? 0) > 1 ? (
        <div className="logical-deduction-list" aria-label="Available deductions">
          {logical?.availableDeductions.map((deduction) => (
            <button
              aria-pressed={deduction.id === logical.selectedDeductionId}
              key={deduction.id}
              type="button"
              disabled={logical.applyingDeductionId !== null}
              onClick={() => controller.candidates.selectLogicalDeduction(deduction.id)}
            >
              {deduction.techniqueId} · {affectedEntities(deduction)}
            </button>
          ))}
        </div>
      ) : null}
      <div className="logical-solver-panel__actions">
        <button
          type="button"
          aria-label="Next Step"
          disabled={selected === undefined || logical?.applyingDeductionId !== null}
          onClick={() => {
            if (selected !== undefined) {
              void controller.candidates.nextLogicalStep(selected.id);
            }
          }}
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
        <button
          id="open-logical-walkthrough"
          type="button"
          aria-label="Open Walkthrough"
          disabled={selected === undefined}
          onClick={() => controller.openWalkthrough()}
        >
          Open Walkthrough
        </button>
      </div>
      {(logical?.archivedRevisions.length ?? 0) > 0 ? (
        <section className="logical-archives" aria-label="Archived logical revisions">
          {logical?.archivedRevisions.map((archive) => (
            <div key={`${archive.semanticRevision}-${archive.semanticHash}`}>
              <strong>Revision {archive.semanticRevision} · archived</strong>
              <span>Read-only history</span>
              <small>{archive.historyDeductionIds.length} applied steps</small>
            </div>
          ))}
        </section>
      ) : null}
    </div>
  );
}
