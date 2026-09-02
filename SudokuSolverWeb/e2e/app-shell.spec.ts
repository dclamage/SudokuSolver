import { expect, test } from "@playwright/test";

test("shows only Set and Playtest as primary workspaces", async ({ page }) => {
  await page.goto("/");

  await expect(page.getByRole("tab", { name: "Set" })).toBeVisible();
  await expect(page.getByRole("tab", { name: "Playtest" })).toBeVisible();
  await expect(page.getByRole("tab", { name: "Analyze" })).toHaveCount(0);
});
