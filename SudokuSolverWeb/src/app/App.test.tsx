import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { App } from "./App";

describe("App", () => {
  it("renders only Set and Playtest as primary workspaces", () => {
    render(<App />);

    expect(screen.getByRole("tab", { name: "Set" })).toBeVisible();
    expect(screen.getByRole("tab", { name: "Playtest" })).toBeVisible();
    expect(screen.queryByRole("tab", { name: "Analyze" })).toBeNull();
  });
});
