import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { App } from "../../app/App";
import { createTestAppController } from "../../test/createTestAppController";

describe("Set workspace", () => {
  it("writes givens through puzzle history", async () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);

    await userEvent.click(screen.getByRole("button", { name: "Given" }));
    await userEvent.click(screen.getByTestId("cell-r1c1"));
    await userEvent.click(screen.getByRole("button", { name: "Enter 5" }));

    expect(controller.puzzle.getSnapshot().document.givens.r1c1).toBe("5");
    expect(controller.playtest.getSnapshot().values.r1c1).toBeUndefined();

    await userEvent.click(screen.getByRole("button", { name: "Undo" }));
    expect(
      controller.puzzle.getSnapshot().document.givens.r1c1,
    ).toBeUndefined();
    await userEvent.click(screen.getByRole("button", { name: "Redo" }));
    expect(controller.puzzle.getSnapshot().document.givens.r1c1).toBe("5");
  });

  it("moves the auxiliary cell without changing semantic revision", async () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);

    await userEvent.click(
      screen.getByRole("button", { name: "Auxiliary cell" }),
    );
    await userEvent.click(screen.getByTestId("cell-aux-1"));
    await userEvent.click(screen.getByRole("button", { name: "Move right" }));

    expect(controller.puzzle.getSnapshot().document.cells["aux-1"].shape).toMatchObject({
      x: 10.25,
      y: 4,
    });
    expect(controller.puzzle.getSnapshot().document.semanticRevision).toBe(1);
  });

  it("renders the milestone element library and quiet puzzle status", () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);

    expect(screen.getByRole("heading", { name: "Elements" })).toBeVisible();
    expect(screen.getByRole("button", { name: "Given" })).toBeVisible();
    expect(screen.getByRole("button", { name: "Region" })).toBeVisible();
    expect(
      screen.getByRole("button", { name: "Auxiliary cell" }),
    ).toBeVisible();
    expect(screen.getByRole("button", { name: "More" })).toBeVisible();
    expect(screen.getByRole("status", { name: "Puzzle status" })).toBeVisible();
  });

  it("opens and closes the labelled mobile Elements sheet", async () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);

    await userEvent.click(
      screen.getByRole("button", { name: "Open Elements" }),
    );
    expect(controller.editor.getSnapshot().mobileSheet).toBe("elements");
    expect(screen.getByRole("complementary", { name: "Elements" })).toHaveAttribute(
      "data-open",
      "true",
    );

    await userEvent.click(screen.getByRole("button", { name: "Region" }));
    expect(controller.editor.getSnapshot().activeTool).toBe("region");
    expect(controller.editor.getSnapshot().mobileSheet).toBeNull();
  });
});
