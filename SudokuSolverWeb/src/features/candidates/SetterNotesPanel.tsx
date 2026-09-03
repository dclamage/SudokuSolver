import type { CSSProperties } from "react";

import type { AppController } from "../../app/AppController";
import { useExternalStore } from "../../app/useExternalStore";
import type { ManualInputMode } from "../../domain/candidates/types";
import {
  isManualCandidateContext,
  type DomainValue,
  type ManualColorToken,
  type ValueId,
} from "../../domain/puzzle/types";
import { manualColorOptions } from "./manualColorPresentation";

export interface SetterNotesPanelProps {
  readonly controller: AppController;
}

interface ModePickerProps {
  readonly mode: ManualInputMode;
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

function ModePicker({ mode, onSelect }: ModePickerProps) {
  return (
    <div className="mode-picker" aria-label="Setter Notes input mode">
      {inputModes.map((inputMode) => (
        <button
          key={inputMode.id}
          type="button"
          aria-label={inputMode.label}
          aria-pressed={mode === inputMode.id}
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

function activeManualState(
  controller: AppController,
  workspace: "set" | "playtest",
) {
  if (controller.getSnapshot().workspace !== workspace) {
    return undefined;
  }
  const candidates = controller.candidates.getSnapshot();
  const definition = candidates.definitions.find(
    (context) => context.id === candidates.activeContextId,
  );
  if (
    definition === undefined ||
    !isManualCandidateContext(definition) ||
    !candidates.actions.manualCandidateEntry
  ) {
    return undefined;
  }
  const puzzle = controller.puzzle.getSnapshot().document;
  const editor = controller.editor.getSnapshot();
  const cellId = editor.selectedCellIds[0];
  const cell = cellId === undefined ? undefined : puzzle.cells[cellId];
  const domain = cell === undefined ? undefined : puzzle.domains[cell.domainId];
  return { definition, puzzle, editor, cellId, cell, domain };
}

function selectSetMode(controller: AppController, mode: ManualInputMode) {
  if (activeManualState(controller, "set") !== undefined) {
    controller.editor.setSetterNotesInputMode(mode);
  }
}

function toggleSetMark(
  controller: AppController,
  expectedMode: "corner" | "centre",
  valueId: ValueId,
) {
  const state = activeManualState(controller, "set");
  if (
    state === undefined ||
    state.editor.setterNotesInputMode !== expectedMode ||
    state.cellId === undefined ||
    state.cell?.input.acceptsCandidates !== true ||
    state.domain === undefined ||
    !state.domain.values.some((value) => value.id === valueId)
  ) {
    return;
  }
  const current =
    state.puzzle.authoring.manualMarks[state.definition.id]?.[state.cellId]?.[
      expectedMode
    ] ?? [];
  controller.puzzle.execute({
    type: "setManualMarks",
    contextId: state.definition.id,
    cellId: state.cellId,
    kind: expectedMode,
    valueIds: toggleInDomainOrder(state.domain.values, current, valueId),
  });
}

function setSetColor(controller: AppController, color: ManualColorToken) {
  const state = activeManualState(controller, "set");
  if (
    state === undefined ||
    state.editor.setterNotesInputMode !== "color" ||
    state.cellId === undefined ||
    state.cell?.input.acceptsCandidates !== true
  ) {
    return;
  }
  const current =
    state.puzzle.authoring.manualMarks[state.definition.id]?.[state.cellId]
      ?.color ?? null;
  controller.puzzle.execute({
    type: "setManualColor",
    contextId: state.definition.id,
    cellId: state.cellId,
    color: current === color ? null : color,
  });
}

function eraseSetNotes(controller: AppController) {
  const state = activeManualState(controller, "set");
  if (
    state === undefined ||
    state.editor.setterNotesInputMode !== "erase" ||
    state.cellId === undefined ||
    state.cell?.input.acceptsCandidates !== true
  ) {
    return;
  }
  controller.puzzle.execute({
    type: "clearManualCell",
    contextId: state.definition.id,
    cellId: state.cellId,
  });
}

function selectPlaytestMode(controller: AppController, mode: ManualInputMode) {
  if (activeManualState(controller, "playtest") !== undefined) {
    controller.playtest.setInputMode(mode);
  }
}

function enterPlaytestValue(
  controller: AppController,
  expectedMode: "digit" | "corner" | "centre",
  valueId: ValueId,
) {
  const state = activeManualState(controller, "playtest");
  const playtest = controller.playtest.getSnapshot();
  if (
    state === undefined ||
    playtest.inputMode !== expectedMode ||
    state.cellId === undefined ||
    state.cell === undefined ||
    state.domain === undefined ||
    state.puzzle.givens[state.cellId] !== undefined ||
    !state.domain.values.some((value) => value.id === valueId)
  ) {
    return;
  }
  if (expectedMode === "digit") {
    if (state.cell.input.acceptsValue) {
      controller.playtest.enterValue(state.cellId, valueId);
    }
    return;
  }
  if (!state.cell.input.acceptsCandidates) {
    return;
  }
  controller.playtest.setManualMarks(
    expectedMode,
    state.cellId,
    toggleInDomainOrder(
      state.domain.values,
      playtest.manualCandidates[expectedMode][state.cellId] ?? [],
      valueId,
    ),
  );
}

function setPlaytestColor(controller: AppController, color: ManualColorToken) {
  const state = activeManualState(controller, "playtest");
  const playtest = controller.playtest.getSnapshot();
  if (
    state === undefined ||
    playtest.inputMode !== "color" ||
    state.cellId === undefined ||
    state.cell === undefined ||
    state.puzzle.givens[state.cellId] !== undefined
  ) {
    return;
  }
  controller.playtest.applyColor(state.cellId, color);
}

function erasePlaytestCell(controller: AppController) {
  const state = activeManualState(controller, "playtest");
  if (
    state === undefined ||
    controller.playtest.getSnapshot().inputMode !== "erase" ||
    state.cellId === undefined ||
    state.cell === undefined ||
    state.puzzle.givens[state.cellId] !== undefined
  ) {
    return;
  }
  controller.playtest.erase(state.cellId);
}

function SetSetterNotesControls({ controller }: SetterNotesPanelProps) {
  const puzzle = useExternalStore(controller.puzzle).document;
  const editor = useExternalStore(controller.editor);
  const candidates = useExternalStore(controller.candidates);
  const contextId = candidates.activeContextId;
  const selectedCellId = editor.selectedCellIds[0];
  const selectedCell =
    selectedCellId === undefined ? undefined : puzzle.cells[selectedCellId];
  const domain =
    selectedCell === undefined ? undefined : puzzle.domains[selectedCell.domainId];
  const mode = editor.setterNotesInputMode;
  const currentValueIds =
    selectedCellId === undefined || (mode !== "corner" && mode !== "centre")
      ? []
      : (puzzle.authoring.manualMarks[contextId]?.[selectedCellId]?.[mode] ?? []);
  const currentColor =
    selectedCellId === undefined
      ? null
      : (puzzle.authoring.manualMarks[contextId]?.[selectedCellId]?.color ??
        null);
  const candidateInputDisabled =
    selectedCellId === undefined ||
    selectedCell?.input.acceptsCandidates !== true;

  return (
    <div className="setter-notes-controls">
      <p>Setter-controlled notes stay with this layer.</p>
      <ModePicker
        mode={mode}
        onSelect={(inputMode) => selectSetMode(controller, inputMode)}
      />
      {mode === "corner" || mode === "centre" ? (
        <ValueKeypad
          label="Setter Notes keypad"
          values={domain?.values ?? []}
          currentValueIds={currentValueIds}
          disabled={candidateInputDisabled}
          buttonLabel={(valueLabel) => `Mark ${valueLabel}`}
          onValue={(valueId) => toggleSetMark(controller, mode, valueId)}
        />
      ) : mode === "color" ? (
        <div className="color-picker" aria-label="Cell colors">
          {manualColorOptions.map((color) => (
            <button
              key={color.id}
              type="button"
              aria-label={`Apply ${color.id}`}
              aria-pressed={currentColor === color.id}
              disabled={candidateInputDisabled}
              style={{ "--swatch-color": color.value } as CSSProperties}
              onClick={() => setSetColor(controller, color.id)}
            >
              <span aria-hidden="true" />
            </button>
          ))}
        </div>
      ) : mode === "erase" ? (
        <button
          className="erase-selected-button"
          type="button"
          aria-label="Erase Setter Notes marks"
          disabled={candidateInputDisabled}
          onClick={() => eraseSetNotes(controller)}
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

  return (
    <div className="setter-notes-controls">
      <p>Notes, colors, and values stay with this playtest session.</p>
      <ModePicker
        mode={mode}
        onSelect={(inputMode) => selectPlaytestMode(controller, inputMode)}
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
          onValue={(valueId) =>
            enterPlaytestValue(controller, mode, valueId)
          }
        />
      ) : mode === "color" ? (
        <div className="color-picker" aria-label="Cell colors">
          {manualColorOptions.map((color) => (
            <button
              key={color.id}
              type="button"
              aria-label={`Apply ${color.id}`}
              aria-pressed={
                playtest.colors[selectedCellId ?? ""]?.[0] === color.id
              }
              disabled={inputBlocked}
              style={{ "--swatch-color": color.value } as CSSProperties}
              onClick={() => setPlaytestColor(controller, color.id)}
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
          onClick={() => erasePlaytestCell(controller)}
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
    <SetSetterNotesControls controller={controller} />
  ) : (
    <PlaytestSetterNotesControls controller={controller} />
  );
}
