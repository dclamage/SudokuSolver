import { act, fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { App } from "../../app/App";
import { validatePuzzlePackage } from "../../domain/puzzle/validatePuzzlePackage";
import { createTestAppController } from "../../test/createTestAppController";
import { SetterNotesPanel } from "./SetterNotesPanel";

function createControllerWithSecondManualContext() {
  const controller = createTestAppController();
  controller.puzzle.execute({
    type: "addCandidateContext",
    context: {
      id: "setter-notes-b",
      name: "Setter notes B",
      kind: "manual",
    },
    index: 1,
  });
  return controller;
}

describe("SetterNotesPanel", () => {
  it("renders independent Set corner, centre, and color state accessibly", async () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);
    await userEvent.click(screen.getByTestId("cell-r1c2"));

    await userEvent.click(screen.getByRole("button", { name: "Mark 4" }));
    await userEvent.click(screen.getByRole("button", { name: "Centre" }));
    await userEvent.click(screen.getByRole("button", { name: "Mark 4" }));
    await userEvent.click(screen.getByRole("button", { name: "Color" }));
    await userEvent.click(screen.getByRole("button", { name: "Apply cyan" }));

    expect(screen.getByTestId("corner-candidates-r1c2")).toHaveTextContent("4");
    expect(screen.getByTestId("centre-candidates-r1c2")).toHaveTextContent("4");
    expect(screen.getByTestId("cell-r1c2")).toHaveAttribute(
      "data-cell-fill",
      "cyan",
    );
    expect(screen.getByTestId("cell-r1c2")).toHaveAccessibleName(
      /corner candidates 4; centre candidates 4; color cyan/,
    );
    const reloaded = validatePuzzlePackage(
      JSON.parse(JSON.stringify(controller.testDependencies.persistence.current)),
    );
    expect(reloaded.authoring.manualMarks["setter-notes"].r1c2).toEqual({
      corner: ["4"],
      centre: ["4"],
      color: "cyan",
    });

    await userEvent.click(screen.getByRole("button", { name: "Erase" }));
    await userEvent.click(
      screen.getByRole("button", { name: "Erase Setter Notes marks" }),
    );
    expect(
      controller.puzzle.getSnapshot().document.authoring.manualMarks[
        "setter-notes"
      ].r1c2,
    ).toBeUndefined();
    act(() => controller.puzzle.undo());
    expect(screen.getByTestId("cell-r1c2")).toHaveAccessibleName(
      /corner candidates 4; centre candidates 4; color cyan/,
    );
    expect(
      controller.testDependencies.solver.requests.filter(
        (request) => request.operation === "count",
      ),
    ).toHaveLength(0);
  });

  it("rejects a retained Set action after its context becomes inactive", () => {
    const controller = createTestAppController();
    controller.editor.selectOnly("r1c2");
    render(
      <div
        onClickCapture={() => controller.candidates.activate("true-candidates")}
      >
        <SetterNotesPanel controller={controller} />
      </div>,
    );
    fireEvent.click(screen.getByRole("button", { name: "Mark 4" }));
    expect(
      controller.puzzle.getSnapshot().document.authoring.manualMarks[
        "setter-notes"
      ].r1c2,
    ).toBeUndefined();
  });

  it("applies a retained Set action to the selection current at invocation", () => {
    const controller = createTestAppController();
    controller.editor.selectOnly("r1c2");
    render(
      <div
        onClickCapture={() => controller.editor.selectOnly("r2c3")}
      >
        <SetterNotesPanel controller={controller} />
      </div>,
    );
    fireEvent.click(screen.getByRole("button", { name: "Mark 4" }));
    expect(
      controller.puzzle.getSnapshot().document.authoring.manualMarks["setter-notes"],
    ).toMatchObject({ r2c3: expect.anything() });
    expect(
      controller.puzzle.getSnapshot().document.authoring.manualMarks[
        "setter-notes"
      ].r1c2,
    ).toBeUndefined();
  });

  it("rejects a retained Set action after switching to Playtest", () => {
    const controller = createTestAppController();
    controller.editor.selectOnly("r1c2");
    render(
      <div onClickCapture={() => controller.setWorkspace("playtest")}>
        <SetterNotesPanel controller={controller} />
      </div>,
    );
    fireEvent.click(screen.getByRole("button", { name: "Mark 4" }));
    expect(
      controller.puzzle.getSnapshot().document.authoring.manualMarks[
        "setter-notes"
      ].r1c2,
    ).toBeUndefined();
    expect(
      controller.playtest.getSnapshot().manualCandidates.corner.r1c2,
    ).toBeUndefined();
  });

  it("rejects a retained Playtest action after its context becomes inactive", () => {
    const controller = createTestAppController();
    controller.setWorkspace("playtest");
    controller.editor.selectOnly("r1c2");
    controller.playtest.setInputMode("corner");
    render(
      <div
        onClickCapture={() => controller.candidates.activate("true-candidates")}
      >
        <SetterNotesPanel controller={controller} />
      </div>,
    );
    fireEvent.click(
      screen.getByRole("button", { name: "Toggle corner 4" }),
    );
    expect(
      controller.playtest.getSnapshot().manualCandidates.corner.r1c2,
    ).toBeUndefined();
  });

  it.each([
    { action: "mark", mode: "corner", label: "Mark 4" },
    { action: "color", mode: "color", label: "Apply cyan" },
    { action: "erase", mode: "erase", label: "Erase Setter Notes marks" },
    { action: "mode", mode: "corner", label: "Centre" },
  ] as const)(
    "rejects a retained Set $action callback after switching manual contexts",
    ({ action, mode, label }) => {
      const controller = createControllerWithSecondManualContext();
      controller.editor.selectOnly("r1c2");
      controller.editor.setSetterNotesInputMode(mode);
      if (action === "erase") {
        for (const contextId of ["setter-notes", "setter-notes-b"]) {
          controller.puzzle.execute({
            type: "setManualMarks",
            contextId,
            cellId: "r1c2",
            valueIds: ["4"],
          });
        }
      }
      render(
        <div
          onClickCapture={() =>
            controller.candidates.activate("setter-notes-b")
          }
        >
          <SetterNotesPanel controller={controller} />
        </div>,
      );

      fireEvent.click(screen.getByRole("button", { name: label }));

      expect(controller.candidates.getSnapshot().activeContextId).toBe(
        "setter-notes-b",
      );
      if (action === "erase") {
        expect(
          controller.puzzle.getSnapshot().document.authoring.manualMarks[
            "setter-notes-b"
          ].r1c2,
        ).toEqual({ corner: ["4"], centre: [], color: null });
      } else if (action === "mode") {
        expect(controller.editor.getSnapshot().setterNotesInputMode).toBe(
          "corner",
        );
      } else {
        expect(
          controller.puzzle.getSnapshot().document.authoring.manualMarks[
            "setter-notes-b"
          ].r1c2,
        ).toBeUndefined();
      }
    },
  );

  it.each([
    { action: "mark", mode: "corner", label: "Toggle corner 4" },
    { action: "color", mode: "color", label: "Apply cyan" },
    { action: "erase", mode: "erase", label: "Erase selected cell" },
    { action: "mode", mode: "corner", label: "Centre" },
  ] as const)(
    "rejects a retained Playtest $action callback after switching manual contexts",
    ({ action, mode, label }) => {
      const controller = createControllerWithSecondManualContext();
      controller.setWorkspace("playtest");
      controller.editor.selectOnly("r1c2");
      controller.playtest.setInputMode(mode);
      if (action === "erase") {
        controller.playtest.setManualMarks("corner", "r1c2", ["4"]);
      }
      render(
        <div
          onClickCapture={() =>
            controller.candidates.activate("setter-notes-b")
          }
        >
          <SetterNotesPanel controller={controller} />
        </div>,
      );

      fireEvent.click(screen.getByRole("button", { name: label }));

      expect(controller.candidates.getSnapshot().activeContextId).toBe(
        "setter-notes-b",
      );
      if (action === "erase") {
        expect(
          controller.playtest.getSnapshot().manualCandidates.corner.r1c2,
        ).toEqual(["4"]);
      } else if (action === "mode") {
        expect(controller.playtest.getSnapshot().inputMode).toBe("corner");
      } else if (action === "color") {
        expect(controller.playtest.getSnapshot().colors.r1c2).toBeUndefined();
      } else {
        expect(
          controller.playtest.getSnapshot().manualCandidates.corner.r1c2,
        ).toBeUndefined();
      }
    },
  );

  it("rejects a retained mark action after the Set input mode changes", () => {
    const controller = createTestAppController();
    controller.editor.selectOnly("r1c2");
    render(
      <div
        onClickCapture={() =>
          controller.editor.setSetterNotesInputMode("centre")
        }
      >
        <SetterNotesPanel controller={controller} />
      </div>,
    );
    fireEvent.click(screen.getByRole("button", { name: "Mark 4" }));
    expect(
      controller.puzzle.getSnapshot().document.authoring.manualMarks[
        "setter-notes"
      ].r1c2,
    ).toBeUndefined();
  });

  it("toggles against marks current at invocation", () => {
    const controller = createTestAppController();
    controller.editor.selectOnly("r1c2");
    render(
      <div
        onClickCapture={() =>
          controller.puzzle.execute({
            type: "setManualMarks",
            contextId: "setter-notes",
            cellId: "r1c2",
            valueIds: ["4"],
          })
        }
      >
        <SetterNotesPanel controller={controller} />
      </div>,
    );
    fireEvent.click(screen.getByRole("button", { name: "Mark 4" }));
    expect(
      controller.puzzle.getSnapshot().document.authoring.manualMarks[
        "setter-notes"
      ].r1c2,
    ).toBeUndefined();
  });

  it.each(["Region", "Auxiliary cell", "More"])(
    "keeps Digit and Given ownership synchronized after %s",
    async (toolName) => {
      const controller = createTestAppController();
      render(<App controller={controller} />);
      await userEvent.click(screen.getByTestId("cell-r1c2"));
      await userEvent.click(screen.getByRole("button", { name: toolName }));
      await userEvent.click(screen.getByRole("button", { name: "Digit" }));

      expect(screen.getByRole("button", { name: "Digit" })).toHaveAttribute(
        "aria-pressed",
        "true",
      );
      expect(screen.getByRole("button", { name: "Given" })).toHaveAttribute(
        "aria-pressed",
        "true",
      );
      expect(screen.getByRole("button", { name: "Enter 4" })).toBeEnabled();

      await userEvent.click(screen.getByRole("button", { name: toolName }));
      expect(screen.getByRole("button", { name: "Digit" })).toHaveAttribute(
        "aria-pressed",
        "false",
      );
      expect(screen.getByRole("button", { name: "Given" })).toHaveAttribute(
        "aria-pressed",
        "false",
      );
      expect(screen.queryByLabelText("Given keypad")).toBeNull();
    },
  );
  it("preserves manual marks while another context is active", async () => {
    const controller = createTestAppController();
    controller.candidates.activate("setter-notes");
    controller.editor.selectOnly("r1c2");
    render(<SetterNotesPanel controller={controller} />);
    await userEvent.click(screen.getByRole("button", { name: "Mark 4" }));
    controller.candidates.activate("true-candidates");
    expect(
      controller.candidates.getSceneProjection().candidates.r1c2,
    ).toBeUndefined();
    controller.candidates.activate("setter-notes");
    expect(controller.candidates.getSceneProjection().candidates.r1c2).toEqual([
      "4",
    ]);
  });

  it("writes Set marks through puzzle history and persistence only", async () => {
    const controller = createTestAppController();
    controller.editor.selectOnly("r1c2");
    render(<SetterNotesPanel controller={controller} />);

    await userEvent.click(screen.getByRole("button", { name: "Mark 7" }));

    expect(
      controller.puzzle.getSnapshot().document.authoring.manualMarks[
        "setter-notes"
      ].r1c2,
    ).toEqual({ corner: ["7"], centre: [], color: null });
    expect(
      controller.playtest.getSnapshot().manualCandidates.corner.r1c2,
    ).toBeUndefined();
    expect(controller.testDependencies.persistence.current.authoring.manualMarks[
      "setter-notes"
    ].r1c2).toEqual({ corner: ["7"], centre: [], color: null });

    act(() => controller.puzzle.undo());
    expect(
      controller.puzzle.getSnapshot().document.authoring.manualMarks[
        "setter-notes"
      ].r1c2,
    ).toBeUndefined();
    act(() => controller.puzzle.redo());
    expect(
      controller.puzzle.getSnapshot().document.authoring.manualMarks[
        "setter-notes"
      ].r1c2,
    ).toEqual({ corner: ["7"], centre: [], color: null });
    expect(
      controller.testDependencies.solver.requests.filter(
        (request) => request.operation === "count",
      ),
    ).toHaveLength(0);
  });

  it("uses the selected auxiliary cell domain labels and stable value order", async () => {
    const controller = createTestAppController({
      preparePuzzle: (puzzle) => {
        puzzle.domains.symbols = {
          id: "symbols",
          values: [
            { id: "ten-id", label: "Ten" },
            { id: "alpha-id", label: "Alpha" },
            { id: "omega-id", label: "Omega" },
          ],
        };
        puzzle.cells["aux-1"].domainId = "symbols";
        puzzle.authoring.manualMarks["setter-notes"]["aux-1"] = {
          corner: ["omega-id", "omega-id"],
          centre: [],
          color: null,
        };
      },
    });
    controller.editor.selectOnly("aux-1");
    render(<SetterNotesPanel controller={controller} />);

    await userEvent.click(screen.getByRole("button", { name: "Mark Ten" }));

    expect(
      controller.puzzle.getSnapshot().document.authoring.manualMarks[
        "setter-notes"
      ]["aux-1"],
    ).toEqual({
      corner: ["ten-id", "omega-id"],
      centre: [],
      color: null,
    });
    expect(screen.queryByRole("button", { name: "Mark 1" })).toBeNull();
  });

  it("writes Playtest marks only to the independent solve session", async () => {
    const controller = createTestAppController();
    controller.setWorkspace("playtest");
    controller.editor.selectOnly("r1c2");
    const startingRevision = controller.puzzle.getSnapshot().document.revision;
    render(<SetterNotesPanel controller={controller} />);

    await userEvent.click(screen.getByRole("button", { name: "Corner" }));
    await userEvent.click(
      screen.getByRole("button", { name: "Toggle corner 7" }),
    );

    expect(
      controller.playtest.getSnapshot().manualCandidates.corner.r1c2,
    ).toEqual(["7"]);
    expect(
      controller.puzzle.getSnapshot().document.authoring.manualMarks[
        "setter-notes"
      ].r1c2,
    ).toBeUndefined();
    expect(controller.puzzle.getSnapshot().document.revision).toBe(
      startingRevision,
    );
    expect(controller.testDependencies.persistence.revisions).toEqual([]);
    expect(
      controller.testDependencies.solver.requests.filter(
        (request) => request.operation === "count",
      ),
    ).toHaveLength(0);
  });

  it("routes Digit through existing workspace value ownership", async () => {
    const setController = createTestAppController();
    setController.editor.selectOnly("r1c2");
    const { unmount } = render(<App controller={setController} />);

    await userEvent.click(screen.getByRole("button", { name: "Digit" }));
    await userEvent.click(screen.getByRole("button", { name: "Enter 5" }));
    expect(setController.puzzle.getSnapshot().document.givens.r1c2).toBe("5");
    expect(
      setController.puzzle.getSnapshot().document.authoring.manualMarks[
        "setter-notes"
      ].r1c2,
    ).toBeUndefined();

    unmount();
    const playtestController = createTestAppController();
    playtestController.setWorkspace("playtest");
    playtestController.editor.selectOnly("r1c2");
    render(<SetterNotesPanel controller={playtestController} />);

    await userEvent.click(screen.getByRole("button", { name: "Enter 5" }));
    expect(playtestController.playtest.getSnapshot().values.r1c2).toBe("5");
    expect(
      playtestController.puzzle.getSnapshot().document.givens.r1c2,
    ).toBeUndefined();
  });

  it("owns active Playtest modes, color, and erase without duplicate controls", async () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);
    await userEvent.click(screen.getByRole("tab", { name: "Playtest" }));
    await userEvent.click(screen.getByTestId("cell-r1c2"));

    expect(screen.getAllByRole("button", { name: "Digit" })).toHaveLength(1);
    expect(screen.getAllByLabelText("Playtest keypad")).toHaveLength(1);
    await userEvent.click(screen.getByRole("button", { name: "Color" }));
    await userEvent.click(screen.getByRole("button", { name: "Apply cyan" }));
    expect(screen.getByTestId("cell-r1c2")).toHaveAttribute(
      "data-cell-fill",
      "cyan",
    );

    await userEvent.click(screen.getByRole("tab", { name: "True candidates" }));
    expect(screen.queryByRole("button", { name: "Color" })).toBeNull();
    expect(screen.getByTestId("cell-r1c2")).not.toHaveAttribute(
      "data-cell-fill",
    );
    expect(controller.playtest.getSnapshot().colors.r1c2).toEqual(["cyan"]);

    await userEvent.click(screen.getByRole("tab", { name: "Setter notes" }));
    expect(screen.getByTestId("cell-r1c2")).toHaveAttribute(
      "data-cell-fill",
      "cyan",
    );
    await userEvent.click(screen.getByRole("button", { name: "Erase" }));
    await userEvent.click(
      screen.getByRole("button", { name: "Erase selected cell" }),
    );
    expect(controller.playtest.getSnapshot().colors.r1c2).toBeUndefined();
  });

  it("applies marks only to the currently selected cell", async () => {
    const controller = createTestAppController();
    controller.editor.selectOnly("r1c2");
    render(<SetterNotesPanel controller={controller} />);

    await userEvent.click(screen.getByRole("button", { name: "Mark 4" }));
    act(() => controller.editor.selectOnly("r2c3"));
    await userEvent.click(screen.getByRole("button", { name: "Mark 7" }));

    expect(
      controller.puzzle.getSnapshot().document.authoring.manualMarks[
        "setter-notes"
      ],
    ).toMatchObject({
      r1c2: { corner: ["4"], centre: [], color: null },
      r2c3: { corner: ["7"], centre: [], color: null },
    });
  });

  it("shows only the keypad owned by the active Set input mode", async () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);
    await userEvent.click(screen.getByTestId("cell-r1c2"));

    expect(screen.getByLabelText("Setter Notes keypad")).toBeVisible();
    expect(screen.queryByLabelText("Given keypad")).toBeNull();

    await userEvent.click(screen.getByRole("button", { name: "Digit" }));
    expect(screen.queryByLabelText("Setter Notes keypad")).toBeNull();
    expect(screen.getByLabelText("Given keypad")).toBeVisible();
  });
});
