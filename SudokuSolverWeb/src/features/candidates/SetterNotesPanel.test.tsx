import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { App } from "../../app/App";
import { createTestAppController } from "../../test/createTestAppController";
import { SetterNotesPanel } from "./SetterNotesPanel";

describe("SetterNotesPanel", () => {
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
    ).toEqual(["7"]);
    expect(
      controller.playtest.getSnapshot().manualCandidates.corner.r1c2,
    ).toBeUndefined();
    expect(controller.testDependencies.persistence.current.authoring.manualMarks[
      "setter-notes"
    ].r1c2).toEqual(["7"]);

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
    ).toEqual(["7"]);
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
        puzzle.authoring.manualMarks["setter-notes"]["aux-1"] = [
          "omega-id",
          "omega-id",
        ];
      },
    });
    controller.editor.selectOnly("aux-1");
    render(<SetterNotesPanel controller={controller} />);

    await userEvent.click(screen.getByRole("button", { name: "Mark Ten" }));

    expect(
      controller.puzzle.getSnapshot().document.authoring.manualMarks[
        "setter-notes"
      ]["aux-1"],
    ).toEqual(["ten-id", "omega-id"]);
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
    ).toMatchObject({ r1c2: ["4"], r2c3: ["7"] });
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
