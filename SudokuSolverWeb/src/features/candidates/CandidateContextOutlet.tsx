import type { ReactNode } from "react";

import type { AppController } from "../../app/AppController";
import { useExternalStore } from "../../app/useExternalStore";
import type {
  CandidateContextStatus,
  CandidatePanelDescriptor,
} from "../../domain/candidates/types";
import { SetterNotesPanel } from "./SetterNotesPanel";

export interface CandidateContextOutletProps {
  controller: AppController;
}

interface CandidatePanelProps {
  readonly panel: CandidatePanelDescriptor;
  readonly controller: AppController;
}

function statusLabel(status: CandidateContextStatus): string {
  switch (status) {
    case "idle":
      return "Ready";
    case "live":
      return "Live";
    case "stale":
      return "Needs update";
    case "calculating":
      return "Calculating…";
    case "error":
      return "Unavailable";
  }
}

function SetterNotesPanelOutlet({ panel, controller }: CandidatePanelProps) {
  return (
    <PanelFrame panel={panel}>
      <SetterNotesPanel controller={controller} />
    </PanelFrame>
  );
}

function TrueCandidatesPanel({ panel }: CandidatePanelProps) {
  return (
    <PanelFrame panel={panel}>
      <p>Solver candidates use this layer only while it is active.</p>
    </PanelFrame>
  );
}

function LogicalSolverPanel({ panel }: CandidatePanelProps) {
  return (
    <PanelFrame panel={panel}>
      <p>The logical candidate-board seam is ready for structured deductions.</p>
    </PanelFrame>
  );
}

function UnsupportedPanel({ panel }: CandidatePanelProps) {
  return (
    <PanelFrame panel={panel}>
      <p>
        This version does not support the {panel.contextKind} layer type. Its
        configuration remains in the puzzle.
      </p>
    </PanelFrame>
  );
}

function PanelFrame({
  panel,
  children,
}: Pick<CandidatePanelProps, "panel"> & { children: ReactNode }) {
  return (
    <section
      className="candidate-context-panel"
      data-panel-kind={panel.panelKind}
      id={`candidate-context-panel-${panel.contextId}`}
      role="tabpanel"
      aria-label={panel.name}
      aria-labelledby={`candidate-context-tab-${panel.contextId}`}
    >
      <div>
        <span className="eyebrow">Active layer</span>
        <h2>{panel.name}</h2>
      </div>
      {children}
      <span className="candidate-context-status" data-status={panel.status}>
        {statusLabel(panel.status)}
      </span>
    </section>
  );
}

const panelRegistry: Readonly<
  Record<string, (props: CandidatePanelProps) => ReactNode>
> = Object.freeze({
  setterNotes: SetterNotesPanelOutlet,
  trueCandidates: TrueCandidatesPanel,
  logicalSolver: LogicalSolverPanel,
});

export function CandidateContextOutlet({
  controller,
}: CandidateContextOutletProps) {
  const panel = useExternalStore(controller.candidates).panel;
  const Panel = panelRegistry[panel.panelKind] ?? UnsupportedPanel;
  return <Panel panel={panel} controller={controller} />;
}
