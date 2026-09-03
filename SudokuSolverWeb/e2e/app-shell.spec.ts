import { expect, test } from "@playwright/test";

test("shows only Set and Playtest as primary workspaces", async ({ page }) => {
  await page.goto("/");

  await expect(page.getByRole("tab", { name: "Set" })).toBeVisible();
  await expect(page.getByRole("tab", { name: "Playtest" })).toBeVisible();
  await expect(page.getByRole("tab", { name: "Analyze" })).toHaveCount(0);
});

test("keeps mobile cells touch-sized without page overflow and sheets on-screen", async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/");

  const firstCell = await page.getByTestId("cell-r1c1").boundingBox();
  expect(firstCell?.width).toBeGreaterThanOrEqual(44);
  expect(firstCell?.x).toBeGreaterThanOrEqual(0);
  expect(
    await page.evaluate<number>("document.documentElement.scrollWidth"),
  ).toBeLessThanOrEqual(390);

  for (const name of ["Elements", "Inspector", "Layers"] as const) {
    await page
      .getByRole("button", {
        name: name === "Layers" ? "Layers" : `Open ${name}`,
      })
      .click();
    const sheet =
      name === "Layers"
        ? page.getByRole("region", { name })
        : page.getByRole("complementary", { name });
    await expect(sheet).toBeVisible();
    const bounds = await sheet.boundingBox();
    expect(bounds?.x).toBeGreaterThanOrEqual(0);
    expect((bounds?.x ?? 0) + (bounds?.width ?? 0)).toBeLessThanOrEqual(390);
    await page.getByRole("button", { name: `Close ${name}` }).click();
  }

  for (const workspace of ["Set", "Playtest"] as const) {
    await page.getByRole("tab", { name: workspace }).click();
    if (workspace === "Playtest") {
      await expect(page.locator(".playtest-heading")).toHaveCSS(
        "background-color",
        "rgb(17, 24, 30)",
      );
      const checkBounds = await page
        .getByRole("button", { name: "Check" })
        .boundingBox();
      expect((checkBounds?.x ?? 0) + (checkBounds?.width ?? 0)).toBeLessThanOrEqual(
        390,
      );
    }
    await page.evaluate("window.scrollTo(0, document.documentElement.scrollHeight)");
    const statusBounds = await page
      .getByRole("status", { name: "Puzzle status" })
      .boundingBox();
    const dockBounds = await page
      .getByRole("tablist", { name: "Workspace" })
      .boundingBox();
    expect(
      (statusBounds?.y ?? 0) + (statusBounds?.height ?? 0),
    ).toBeLessThanOrEqual(dockBounds?.y ?? 0);
  }
});

test("uses a dominant desktop grid", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/");

  const firstCell = await page.getByTestId("cell-r1c1").boundingBox();
  expect(firstCell?.width).toBeGreaterThanOrEqual(56);
});
