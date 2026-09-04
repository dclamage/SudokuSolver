import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { App } from "../../app/App";
import { createTestAppController } from "../../test/createTestAppController";

describe("CandidateContextTabs", () => {
  it("renders only the active Setter Notes panel without other-context actions", () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);

    expect(
      screen.getByRole("tabpanel", { name: "Setter notes" }),
    ).toBeVisible();
    expect(
      screen.queryByRole("tabpanel", { name: "True candidates" }),
    ).toBeNull();
    expect(
      screen.queryByRole("tabpanel", { name: "Logical solver" }),
    ).toBeNull();
    expect(
      screen.queryByRole("button", {
        name: "Open settings for True candidates",
      }),
    ).toBeNull();
    expect(screen.queryByRole("button", { name: "Next step" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Refresh now" })).toBeNull();
  });

  it("focuses the selected named layer and swaps rather than hides panels", async () => {
    const controller = createTestAppController({
      preparePuzzle: (puzzle) => {
        puzzle.authoring.candidateContexts =
          puzzle.authoring.candidateContexts.map((context) =>
            context.id === "true-candidates"
              ? { ...context, name: "Analysis Scratch" }
              : context,
          );
      },
    });
    render(<App controller={controller} />);
    const trueCandidatesTab = screen.getByRole("tab", {
      name: "Analysis Scratch",
    });

    await userEvent.click(trueCandidatesTab);

    expect(trueCandidatesTab).toHaveFocus();
    expect(trueCandidatesTab).toHaveAttribute("aria-selected", "true");
    expect(
      screen.getByRole("tabpanel", { name: "Analysis Scratch" }),
    ).toBeVisible();
    expect(
      screen.queryByRole("tabpanel", { name: "Setter notes" }),
    ).toBeNull();
    expect(
      screen.getByRole("button", { name: "Analysis Scratch options" }),
    ).toBeVisible();
    expect(
      screen.queryByRole("button", { name: "Open settings for Setter notes" }),
    ).toBeNull();
    expect(screen.getByRole("button", { name: "Add Layer" })).toBeVisible();
  });

  it("shares one active context across Set and Playtest", async () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);

    await userEvent.click(screen.getByRole("tab", { name: "Logical solver" }));
    await userEvent.click(screen.getByRole("tab", { name: "Playtest" }));

    expect(
      screen.getByRole("tab", { name: "Logical solver" }),
    ).toHaveAttribute("aria-selected", "true");
    expect(
      screen.getByRole("tabpanel", { name: "Logical solver" }),
    ).toBeVisible();
  });

  it("renders an unknown context with the generic inert panel", async () => {
    const controller = createTestAppController({
      preparePuzzle: (puzzle) => {
        puzzle.authoring.candidateContexts = [
          ...puzzle.authoring.candidateContexts,
          {
            id: "future-candidates",
            name: "Future candidates",
            kind: "futureCandidates",
            futurePolicy: { retain: true },
          } as never,
        ];
        puzzle.authoring.manualMarks["future-candidates"] = {};
      },
    });
    render(<App controller={controller} />);

    await userEvent.click(
      screen.getByRole("tab", { name: "Future candidates" }),
    );

    expect(
      screen.getByRole("tabpanel", { name: "Future candidates" }),
    ).toHaveTextContent(
      "This version does not support the futureCandidates layer type.",
    );
    expect(
      controller.testDependencies.solver.requests.filter(
        (request) => request.operation === "count",
      ),
    ).toHaveLength(0);
  });

  it("removes inactive manual marks from the Playtest scene without losing them", async () => {
    const controller = createTestAppController();
    render(<App controller={controller} />);

    await userEvent.click(screen.getByRole("tab", { name: "Playtest" }));
    await userEvent.click(screen.getByTestId("cell-r1c2"));
    await userEvent.click(screen.getByRole("button", { name: "Corner" }));
    await userEvent.click(
      screen.getByRole("button", { name: "Toggle corner 4" }),
    );
    expect(screen.getByTestId("corner-candidates-r1c2")).toHaveTextContent("4");

    await userEvent.click(screen.getByRole("tab", { name: "True candidates" }));
    expect(screen.queryByTestId("corner-candidates-r1c2")).toBeNull();

    await userEvent.click(screen.getByRole("tab", { name: "Setter notes" }));
    expect(screen.getByTestId("corner-candidates-r1c2")).toHaveTextContent("4");
  });
});
