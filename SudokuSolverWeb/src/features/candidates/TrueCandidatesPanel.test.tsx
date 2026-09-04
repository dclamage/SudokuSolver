import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { App } from "../../app/App";
import { createTestAppController } from "../../test/createTestAppController";

describe("True Candidates controls", () => {
  it("exposes refresh and display configuration from the active split control", async () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);
    await userEvent.click(screen.getByRole("tab", { name: "True candidates" }));

    expect(screen.getByRole("tab", { name: "True candidates" })).toHaveTextContent(
      "True candidates | Auto",
    );
    await userEvent.click(
      screen.getByRole("button", { name: "True candidates options" }),
    );

    expect(screen.getByRole("dialog", { name: "True candidates options" })).toBeVisible();
    expect(screen.getByRole("radio", { name: "Automatic" })).toBeChecked();
    expect(screen.getByRole("radio", { name: "On request" })).not.toBeChecked();
    expect(screen.getByRole("radio", { name: "Possibility" })).toBeChecked();
    expect(screen.getByRole("radio", { name: "Solution frequency" })).not.toBeChecked();
    expect(screen.queryByRole("spinbutton", { name: "Solution count cap" })).toBeNull();

    await userEvent.click(screen.getByRole("radio", { name: "On request" }));
    await userEvent.click(screen.getByRole("radio", { name: "Solution frequency" }));
    const cap = screen.getByRole("spinbutton", { name: "Solution count cap" });
    await userEvent.clear(cap);
    await userEvent.type(cap, "8");
    await userEvent.tab();

    expect(controller.candidates.getSnapshot().activeContextId).toBe("true-candidates");
    expect(screen.getByRole("tab", { name: "True candidates" })).toHaveTextContent(
      "True candidates | On request",
    );
    expect(
      controller.puzzle.getSnapshot().document.authoring.candidateContexts.find(
        (context) => context.id === "true-candidates",
      ),
    ).toMatchObject({
      refresh: "onRequest",
      display: "solutionFrequency",
      solutionCountCap: 8,
    });
  });

  it("renders only calculation state, revision, actions, and legend in the active panel", async () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);
    await userEvent.click(screen.getByRole("tab", { name: "True candidates" }));

    const panel = screen.getByRole("tabpanel", { name: "True candidates" });
    expect(panel).toHaveTextContent("Calculating");
    expect(panel).toHaveTextContent("Last solved revision: Not yet solved");
    expect(screen.getByRole("button", { name: "Refresh true candidates" })).toBeVisible();
    expect(screen.getByRole("button", { name: "Cancel true candidates" })).toBeVisible();
    expect(panel).toHaveTextContent("Possible in at least one solution");
    expect(screen.queryByRole("button", { name: "Corner" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Next step" })).toBeNull();
  });

  it("moves keyboard focus into the options dialog and closes it with Escape", async () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);
    await userEvent.click(screen.getByRole("tab", { name: "True candidates" }));
    await userEvent.click(
      screen.getByRole("button", { name: "True candidates options" }),
    );

    expect(
      screen.getByRole("button", { name: "Close True Candidates options" }),
    ).toHaveFocus();
    await userEvent.keyboard("{Escape}");

    expect(
      screen.queryByRole("dialog", { name: "True candidates options" }),
    ).toBeNull();
  });
});
