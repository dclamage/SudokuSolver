import type { CSSProperties } from "react";

import type { AppController } from "../../app/AppController";
import { useExternalStore } from "../../app/useExternalStore";
import type { ManualInputMode } from "../../domain/candidates/types";
import {
  isManualCandidateContext,
  type CandidateContextId,
  type DomainValue,
  type ValueId,
} from "../../domain/puzzle/types";

export interface SetterNotesPanelProps {
  readonly controller: AppController;
}

interface ModePickerProps {
  readonly mode: ManualInputMode;
  readonly colorDisabled: boolean;
  readonly onSelect: (mode: ManualInputMode) => void;
}

interface ValueKeypadProps {
  readonly label: string;
  readonly values: readonly DomainValue[];
  readonly currentValueIds: readonly ValueId[];
  readonly disabled: boolean;
  readonly buttonLabel: (valueLabel: string) => string;
  readonly onValue: (valueId: ValueId) => void;
}

const inputModes: readonly {
  id: ManualInputMode;
  label: string;
  glyph: string;
}[] = [
  { id: "digit", label: "Digit", glyph: "▦" },
  { id: "corner", label: "Corner", glyph: "⌜" },
  { id: "centre", label: "Centre", glyph: "⊙" },
  { id: "color", label: "Color", glyph: "◉" },
  { id: "erase", label: "Erase", glyph: "⌫" },
];

const colors = Object.freeze([
  Object.freeze({ id: "cyan", value: "#39c6f4" }),
  Object.freeze({ id: "green", value: "#4bd37b" }),
  Object.freeze({ id: "yellow", value: "#f2c94c" }),
  Object.freeze({ id: "rose", value: "#ee6c8a" }),
]);

function toggleInDomainOrder(
  domainValues: readonly DomainValue[],
  currentValueIds: readonly ValueId[],
  valueId: ValueId,
): readonly ValueId[] {
  const selected = new Set(currentValueIds);
  if (selected.has(valueId)) {
    selected.delete(valueId);
  } else {
    selected.add(valueId);
  }
  return domainValues
    .map((domainValue) => domainValue.id)
    .filter((domainValueId) => selected.has(domainValueId));
}

function ModePicker({ mode, colorDisabled, onSelect }: ModePickerProps) {
  return (
    <div className="mode-picker" aria-label="Setter Notes input mode">
      {inputModes.map((inputMode) => (
        <button
          key={inputMode.id}
          type="button"
          aria-label={inputMode.label}
          aria-pressed={mode === inputMode.id}
          disabled={colorDisabled && inputMode.id === "color"}
          title={
            colorDisabled && inputMode.id === "color"
              ? "Cell colors belong to Playtest in this milestone"
              : undefined
          }
          onClick={() => onSelect(inputMode.id)}
        >
          <span aria-hidden="true">{inputMode.glyph}</span>
          <span>{inputMode.label}</span>
        </button>
      ))}
    </div>
  );
}

function ValueKeypad({
  label,
  values,
  currentValueIds,
  disabled,
  buttonLabel,
  onValue,
}: ValueKeypadProps) {
  return (
    <div className="setter-notes-keypad keypad" aria-label={label}>
      {values.map((value) => (
        <button
          key={value.id}
          type="button"
          aria-label={buttonLabel(value.label)}
          aria-pressed={currentValueIds.includes(value.id)}
          disabled={disabled}
          onClick={() => onValue(value.id)}
        >
          {value.label}
        </button>
      ))}
    </div>
  );
}

function SetSetterNotesControls({
  controller,
  contextId,
}: SetterNotesPanelProps & { readonly contextId: CandidateContextId }) {
  const puzzle = useExternalStore(controller.puzzle).document;
  const editor = useExternalStore(controller.editor);
  const selectedCellId = editor.selectedCellIds[0];
  const selectedCell =
    selectedCellId === undefined ? undefined : puzzle.cells[selectedCellId];
  const domain =
    selectedCell === undefined ? undefined : puzzle.domains[selectedCell.domainId];
  const mode = editor.setterNotesInputMode;
  const currentValueIds =
    selectedCellId === undefined || (mode !== "corner" && mode !== "centre")
      ? []
      : (puzzle.authoring.manualMarks[contextId]?.[selectedCellId] ?? []);
  const candidateInputDisabled =
    selectedCellId === undefined ||
    selectedCell?.input.acceptsCandidates !== true;

  const toggleCandidate = (valueId: ValueId) => {
    if (
      selectedCellId === undefined ||
      domain === undefined ||
      candidateInputDisabled
    ) {
      return;
    }
    controller.puzzle.execute({
      type: "setManualMarks",
      contextId,
      cellId: selectedCellId,
      valueIds: toggleInDomainOrder(
        domain.values,
        currentValueIds,
        valueId,
      ),
    });
  };

  return (
    <div className="setter-notes-controls">
      <p>Setter-controlled notes stay with this layer.</p>
      <ModePicker
        mode={mode}
        colorDisabled
        onSelect={controller.editor.setSetterNotesInputMode.bind(
          controller.editor,
        )}
      />
      {mode === "corner" || mode === "centre" ? (
        <ValueKeypad
          label="Setter Notes keypad"
          values={domain?.values ?? []}
          currentValueIds={currentValueIds}
          disabled={candidateInputDisabled}
          buttonLabel={(valueLabel) => `Mark ${valueLabel}`}
          onValue={toggleCandidate}
        />
      ) : mode === "erase" ? (
        <button
          className="erase-selected-button"
          type="button"
          aria-label="Erase Setter Notes marks"
          disabled={candidateInputDisabled}
          onClick={() => {
            if (selectedCellId !== undefined) {
              controller.puzzle.execute({
                type: "setManualMarks",
                contextId,
                cellId: selectedCellId,
                valueIds: [],
              });
            }
          }}
        >
          Erase marks
        </button>
      ) : null}
    </div>
  );
}

function PlaytestSetterNotesControls({ controller }: SetterNotesPanelProps) {
  const puzzle = useExternalStore(controller.puzzle).document;
  const editor = useExternalStore(controller.editor);
  const playtest = useExternalStore(controller.playtest);
  const selectedCellId = editor.selectedCellIds[0];
  const selectedCell =
    selectedCellId === undefined ? undefined : puzzle.cells[selectedCellId];
  const domain =
    selectedCell === undefined ? undefined : puzzle.domains[selectedCell.domainId];
  const mode = playtest.inputMode;
  const currentValueIds =
    selectedCellId === undefined || (mode !== "corner" && mode !== "centre")
      ? []
      : (playtest.manualCandidates[mode][selectedCellId] ?? []);
  const inputBlocked =
    selectedCellId === undefined ||
    selectedCell === undefined ||
    puzzle.givens[selectedCellId] !== undefined;
  const valueInputDisabled =
    inputBlocked || selectedCell?.input.acceptsValue !== true;
  const candidateInputDisabled =
    inputBlocked || selectedCell?.input.acceptsCandidates !== true;

  const enterValue = (valueId: ValueId) => {
    if (selectedCellId === undefined) {
      return;
    }
    if (mode === "digit") {
      if (!valueInputDisabled) {
        controller.playtest.enterValue(selectedCellId, valueId);
      }
      return;
    }
    if (
      (mode !== "corner" && mode !== "centre") ||
      domain === undefined ||
      candidateInputDisabled
    ) {
      return;
    }
    controller.playtest.setManualMarks(
      mode,
      selectedCellId,
      toggleInDomainOrder(domain.values, currentValueIds, valueId),
    );
  };

  return (
    <div className="setter-notes-controls">
      <p>Notes, colors, and values stay with this playtest session.</p>
      <ModePicker
        mode={mode}
        colorDisabled={false}
        onSelect={controller.playtest.setInputMode.bind(controller.playtest)}
      />
      {mode === "digit" || mode === "corner" || mode === "centre" ? (
        <ValueKeypad
          label="Playtest keypad"
          values={domain?.values ?? []}
          currentValueIds={
            mode === "digit" && selectedCellId !== undefined
              ? [playtest.values[selectedCellId]].filter(
                  (valueId): valueId is ValueId => valueId !== undefined,
                )
              : currentValueIds
          }
          disabled={
            mode === "digit" ? valueInputDisabled : candidateInputDisabled
          }
          buttonLabel={(valueLabel) =>
            mode === "digit"
              ? `Enter ${valueLabel}`
              : `Toggle ${mode} ${valueLabel}`
          }
          onValue={enterValue}
        />
      ) : mode === "color" ? (
        <div className="color-picker" aria-label="Cell colors">
          {colors.map((color) => (
            <button
              key={color.id}
              type="button"
              aria-label={`Apply ${color.id}`}
              aria-pressed={
                playtest.colors[selectedCellId ?? ""]?.[0] === color.id
              }
              disabled={inputBlocked}
              style={{ "--swatch-color": color.value } as CSSProperties}
              onClick={() => {
                if (selectedCellId !== undefined) {
                  controller.playtest.applyColor(selectedCellId, color.id);
                }
              }}
            >
              <span aria-hidden="true" />
            </button>
          ))}
        </div>
      ) : (
        <button
          className="erase-selected-button"
          type="button"
          aria-label="Erase selected cell"
          disabled={inputBlocked}
          onClick={() => {
            if (selectedCellId !== undefined) {
              controller.playtest.erase(selectedCellId);
            }
          }}
        >
          Erase selected cell
        </button>
      )}
    </div>
  );
}

export function SetterNotesPanel({ controller }: SetterNotesPanelProps) {
  const app = useExternalStore(controller);
  const candidates = useExternalStore(controller.candidates);
  const definition = candidates.definitions.find(
    (context) => context.id === candidates.activeContextId,
  );

  if (
    definition === undefined ||
    !isManualCandidateContext(definition) ||
    !candidates.actions.manualCandidateEntry
  ) {
    return null;
  }

  return app.workspace === "set" ? (
    <SetSetterNotesControls
      controller={controller}
      contextId={definition.id}
    />
  ) : (
    <PlaytestSetterNotesControls controller={controller} />
  );
}
