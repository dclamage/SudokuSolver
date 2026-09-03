import type { AppController } from "../../app/AppController";
import { useExternalStore } from "../../app/useExternalStore";
import "./candidateContexts.css";

export interface CandidateContextTabsProps {
  controller: AppController;
}

export function CandidateContextTabs({
  controller,
}: CandidateContextTabsProps) {
  const candidateState = useExternalStore(controller.candidates);

  return (
    <div className="candidate-context-navigation">
      <div
        className="candidate-context-tabs"
        role="tablist"
        aria-label="Candidate layers"
      >
        {candidateState.definitions.map((definition) => {
          const active = definition.id === candidateState.activeContextId;
          const runtime = candidateState.contexts[definition.id];
          return (
            <div className="candidate-context-tab-group" key={definition.id}>
              <button
                id={`candidate-context-tab-${definition.id}`}
                className="candidate-context-tab"
                type="button"
                role="tab"
                aria-controls={`candidate-context-panel-${definition.id}`}
                aria-selected={active}
                tabIndex={active ? 0 : -1}
                data-status={runtime.status}
                onClick={() => controller.candidates.activate(definition.id)}
              >
                <span>{definition.name}</span>
                <small aria-hidden="true">{runtime.status}</small>
              </button>
              {active && definition.kind === "trueCandidates" ? (
                <button
                  className="candidate-context-settings"
                  type="button"
                  aria-label={`Open settings for ${definition.name}`}
                  aria-haspopup="dialog"
                  title="True Candidates settings are introduced in Task 11"
                >
                  <span aria-hidden="true">⌄</span>
                </button>
              ) : null}
            </div>
          );
        })}
      </div>
      <button
        className="candidate-context-add"
        type="button"
        aria-label="Add Layer"
        disabled
        title="Layer creation is not part of this milestone slice"
      >
        <span aria-hidden="true">＋</span>
        Add Layer
      </button>
    </div>
  );
}
