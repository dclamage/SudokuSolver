import { useEffect, useRef, useState } from "react";

import type { AppController } from "../../app/AppController";
import { useExternalStore } from "../../app/useExternalStore";
import { isTrueCandidatesContext } from "../../domain/puzzle/types";
import { TrueCandidatesOptionsPopover } from "./TrueCandidatesOptionsPopover";
import "./candidateContexts.css";

export interface CandidateContextTabsProps {
  controller: AppController;
}

export function CandidateContextTabs({
  controller,
}: CandidateContextTabsProps) {
  const candidateState = useExternalStore(controller.candidates);
  const [optionsContextId, setOptionsContextId] = useState<string | null>(null);
  const optionsTriggerRef = useRef<HTMLButtonElement>(null);
  const restoreOptionsFocusRef = useRef(false);
  const optionsContext = candidateState.definitions.find(
    (definition) =>
      definition.id === optionsContextId &&
      definition.id === candidateState.activeContextId &&
      isTrueCandidatesContext(definition),
  );

  useEffect(() => {
    if (optionsContextId !== null || !restoreOptionsFocusRef.current) {
      return;
    }
    restoreOptionsFocusRef.current = false;
    optionsTriggerRef.current?.focus();
  }, [optionsContextId]);

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
                aria-label={definition.name}
                aria-controls={`candidate-context-panel-${definition.id}`}
                aria-selected={active}
                tabIndex={active ? 0 : -1}
                data-status={runtime.status}
                onClick={() => controller.candidates.activate(definition.id)}
              >
                <span>
                  {definition.name}
                  {isTrueCandidatesContext(definition)
                    ? ` | ${definition.refresh === "automatic" ? "Auto" : "On request"}`
                    : ""}
                </span>
                <small aria-hidden="true">{runtime.status}</small>
              </button>
              {active && isTrueCandidatesContext(definition) ? (
                <div className="candidate-context-options-anchor">
                  <button
                    ref={optionsTriggerRef}
                    className="candidate-context-settings"
                    type="button"
                    aria-label={`${definition.name} options`}
                    aria-haspopup="dialog"
                    aria-expanded={optionsContextId === definition.id}
                    onClick={() =>
                      setOptionsContextId((current) =>
                        current === definition.id ? null : definition.id,
                      )
                    }
                  >
                    <span aria-hidden="true">⌄</span>
                  </button>
                </div>
              ) : null}
            </div>
          );
        })}
      </div>
      {optionsContext !== undefined && isTrueCandidatesContext(optionsContext) ? (
        <TrueCandidatesOptionsPopover
          controller={controller}
          context={optionsContext}
          onClose={() => {
            restoreOptionsFocusRef.current = true;
            setOptionsContextId(null);
          }}
        />
      ) : null}
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
