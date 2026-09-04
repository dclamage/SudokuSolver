import type { AppController } from "../../app/AppController";
import { useExternalStore } from "../../app/useExternalStore";
import { trueCandidateLegend } from "../../domain/candidates/trueCandidatesBehavior";
import { isTrueCandidatesContext } from "../../domain/puzzle/types";

export interface TrueCandidatesPanelProps {
  readonly controller: AppController;
}

function calculationLabel(status: string): string {
  switch (status) {
    case "calculating":
      return "Calculating candidates…";
    case "live":
      return "Candidates are current";
    case "error":
      return "Calculation unavailable";
    default:
      return "Candidates need an update";
  }
}

export function TrueCandidatesPanel({ controller }: TrueCandidatesPanelProps) {
  const snapshot = useExternalStore(controller.candidates);
  const definition = snapshot.definitions.find(
    (context) => context.id === snapshot.activeContextId,
  );
  if (definition === undefined || !isTrueCandidatesContext(definition)) {
    return null;
  }
  const runtime = snapshot.contexts[definition.id];
  const legend = runtime.trueCandidates?.legend ?? trueCandidateLegend(definition.display);

  return (
    <div className="true-candidates-panel">
      <div className="true-candidates-panel__state" role="status">
        <strong>{calculationLabel(runtime.status)}</strong>
        {runtime.progress === undefined || runtime.progress === null ? null : (
          <span>
            {runtime.progress.discoveredCandidates} candidates found
          </span>
        )}
        {runtime.error === null ? null : <span>{runtime.error}</span>}
      </div>
      <span>
        Last solved revision: {runtime.baseSemanticRevision ?? "Not yet solved"}
      </span>
      <div className="true-candidates-panel__actions">
        <button
          type="button"
          aria-label="Refresh true candidates"
          onClick={() => controller.candidates.refresh()}
        >
          ↻ Refresh
        </button>
        <button
          type="button"
          aria-label="Cancel true candidates"
          disabled={runtime.status !== "calculating"}
          onClick={() => controller.candidates.cancel()}
        >
          × Cancel
        </button>
      </div>
      <div className="true-candidates-legend" aria-label="True Candidates legend">
        {legend.map((entry) => (
          <span key={entry.tone} data-candidate-tone={entry.tone}>
            <i aria-hidden="true" />
            {entry.label}
          </span>
        ))}
      </div>
    </div>
  );
}
