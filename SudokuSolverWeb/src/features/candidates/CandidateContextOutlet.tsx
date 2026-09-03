import type { ReactNode } from "react";

import type { AppController } from "../../app/AppController";
import { useExternalStore } from "../../app/useExternalStore";
import type {
  CandidateContextStatus,
  CandidatePanelDescriptor,
  CandidatePanelKind,
} from "../../domain/candidates/types";

export interface CandidateContextOutletProps {
  controller: AppController;
}

interface CandidatePanelProps {
  readonly panel: CandidatePanelDescriptor;
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

function SetterNotesPanel({ panel }: CandidatePanelProps) {
  return (
    <PanelFrame panel={panel}>
      <p>Setter-controlled notes stay with this puzzle.</p>
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

function PanelFrame({
  panel,
  children,
}: CandidatePanelProps & { children: ReactNode }) {
  return (
    <section
      className="candidate-context-panel"
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
  Record<CandidatePanelKind, (props: CandidatePanelProps) => ReactNode>
> = Object.freeze({
  setterNotes: SetterNotesPanel,
  trueCandidates: TrueCandidatesPanel,
  logicalSolver: LogicalSolverPanel,
});

export function CandidateContextOutlet({
  controller,
}: CandidateContextOutletProps) {
  const panel = useExternalStore(controller.candidates).panel;
  const Panel = panelRegistry[panel.panelKind];
  return <Panel panel={panel} />;
}
