import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { createTestAppController } from "../test/createTestAppController";
import { App } from "./App";

describe("App", () => {
  it("renders only Set and Playtest as primary workspaces", () => {
    render(<App controller={createTestAppController()} />);

    expect(screen.getByRole("tab", { name: "Set" })).toBeVisible();
    expect(screen.getByRole("tab", { name: "Playtest" })).toBeVisible();
    expect(screen.queryByRole("tab", { name: "Analyze" })).toBeNull();
  });

  it("opens Layers as a tool sheet rather than a third workspace", async () => {
    const controller = createTestAppController();
    controller.editor.setMobileSheet("layers");
    render(<App controller={controller} />);

    const sheet = screen.getByRole("region", { name: "Layers" });
    await userEvent.click(
      screen.getByRole("button", { name: "True candidates" }),
    );

    expect(sheet).toBeVisible();
    expect(controller.editor.getSnapshot().activeContextId).toBe(
      "true-candidates",
    );
    expect(screen.queryByRole("tab", { name: "Layers" })).toBeNull();
  });

  it("labels a production-like in-memory session as not saved", () => {
    render(
      <App controller={createTestAppController({ withPersistence: false })} />,
    );

    expect(screen.getByText("Session only · not saved")).toBeVisible();
    expect(screen.queryByText("Saved locally")).toBeNull();
  });
});
