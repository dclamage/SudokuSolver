import type { AppController } from "../../app/AppController";
import {
  isTrueCandidatesContext,
  type TrueCandidatesContext,
} from "../../domain/puzzle/types";

export interface TrueCandidatesOptionsPopoverProps {
  readonly controller: AppController;
  readonly context: TrueCandidatesContext;
  readonly onClose: () => void;
}

function configure(
  controller: AppController,
  contextId: string,
  change: Partial<
    Pick<TrueCandidatesContext, "refresh" | "display" | "solutionCountCap">
  >,
): void {
  const candidates = controller.candidates.getSnapshot();
  const current = candidates.definitions.find(
    (definition) => definition.id === contextId,
  );
  if (
    candidates.activeContextId !== contextId ||
    current === undefined ||
    !isTrueCandidatesContext(current)
  ) {
    return;
  }
  controller.puzzle.execute({
    type: "configureTrueCandidates",
    contextId,
    refresh: change.refresh ?? current.refresh,
    display: change.display ?? current.display,
    solutionCountCap: change.solutionCountCap ?? current.solutionCountCap,
  });
}

export function TrueCandidatesOptionsPopover({
  controller,
  context,
  onClose,
}: TrueCandidatesOptionsPopoverProps) {
  return (
    <div
      className="true-candidates-options"
      role="dialog"
      aria-label={`${context.name} options`}
      onKeyDown={(event) => {
        if (event.key === "Escape") {
          onClose();
        }
      }}
    >
      <div className="true-candidates-options__heading">
        <strong>{context.name} options</strong>
        <button
          type="button"
          aria-label="Close True Candidates options"
          autoFocus
          onClick={onClose}
        >
          ×
        </button>
      </div>
      <fieldset>
        <legend>Refresh</legend>
        <label>
          <input
            type="radio"
            name={`refresh-${context.id}`}
            checked={context.refresh === "automatic"}
            onChange={() => configure(controller, context.id, { refresh: "automatic" })}
          />
          Automatic
        </label>
        <label>
          <input
            type="radio"
            name={`refresh-${context.id}`}
            checked={context.refresh === "onRequest"}
            onChange={() => configure(controller, context.id, { refresh: "onRequest" })}
          />
          On request
        </label>
      </fieldset>
      <fieldset>
        <legend>Display</legend>
        <label>
          <input
            type="radio"
            name={`display-${context.id}`}
            checked={context.display === "possibility"}
            onChange={() => configure(controller, context.id, { display: "possibility" })}
          />
          Possibility
        </label>
        <label>
          <input
            type="radio"
            name={`display-${context.id}`}
            checked={context.display === "solutionFrequency"}
            onChange={() =>
              configure(controller, context.id, { display: "solutionFrequency" })
            }
          />
          Solution frequency
        </label>
        <label>
          <input
            type="radio"
            name={`display-${context.id}`}
            checked={context.display === "logicComparison"}
            onChange={() => configure(controller, context.id, { display: "logicComparison" })}
          />
          Logic comparison
        </label>
      </fieldset>
      {context.display === "solutionFrequency" ? (
        <label className="true-candidates-options__cap">
          <span>Solution count cap</span>
          <input
            type="number"
            min={1}
            max={1024}
            step={1}
            defaultValue={context.solutionCountCap}
            onBlur={(event) => {
              const value = Number(event.currentTarget.value);
              if (Number.isSafeInteger(value) && value >= 1 && value <= 1024) {
                configure(controller, context.id, { solutionCountCap: value });
              } else {
                event.currentTarget.value = String(context.solutionCountCap);
              }
            }}
          />
        </label>
      ) : null}
    </div>
  );
}
