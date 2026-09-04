import { chromium } from "../../../SudokuSolverWeb/node_modules/@playwright/test/index.mjs";
import { mkdir } from "node:fs/promises";

const baseUrl = "http://127.0.0.1:4195";
const artifactDir = "C:/Users/rangs/.codex/visualizations/2026/09/01/01a05ce7-6a83-73b2-9343-243a85c5d8fd/task13";
await mkdir(artifactDir, { recursive: true });

const browser = await chromium.launch({ headless: true });
const results = [];
try {
  for (const viewport of [
    { name: "desktop", width: 1440, height: 1000 },
    { name: "mobile", width: 390, height: 844 },
  ]) {
    const page = await browser.newPage({ viewport });
    const messages = [];
    page.on("console", (message) => {
      if (["error", "warning"].includes(message.type())) {
        messages.push(`${message.type()}: ${message.text()}`);
      }
    });
    page.on("pageerror", (error) => messages.push(`pageerror: ${error.message}`));
    await page.goto(baseUrl, { waitUntil: "networkidle" });
    await page.evaluate(async () => {
      const [ReactDomClient, ReactModule, { App }, fixtures] = await Promise.all([
        import("/node_modules/.vite/deps/react-dom_client.js"),
        import("/node_modules/.vite/deps/react.js"),
        import("/src/app/App.tsx"),
        import("/src/test/logicalFixtures.ts"),
      ]);
      const createRoot = ReactDomClient.createRoot ?? ReactDomClient.default.createRoot;
      const React = ReactModule.default ?? ReactModule;
      const root = document.getElementById("root");
      root.replaceChildren();
      const harness = fixtures.createLogicalHarness();
      globalThis.__task13Harness = harness;
      globalThis.__task13Fixtures = fixtures;
      createRoot(root).render(React.createElement(App, { controller: harness.controller }));
    });
    await page.getByRole("tab", { name: /Logical solver/i }).waitFor();
    if (await page.getByRole("button", { name: "Next Step" }).count() !== 0) {
      throw new Error(`${viewport.name} exposed logical controls before activation`);
    }
    await page.getByRole("tab", { name: /Logical solver/i }).click();
    await page.evaluate(() => {
      const { solver } = globalThis.__task13Harness;
      const request = solver.requests.at(-1);
      const { logicalResponse, logicalState, nakedSingle } = globalThis.__task13Fixtures;
      solver.resolve(request.requestId, logicalResponse(request, logicalState({ semanticRevision: 1, deductions: [nakedSingle] })));
    });
    await page.getByRole("button", { name: "Open Walkthrough" }).waitFor();
    await page.screenshot({ path: `${artifactDir}/${viewport.name}-panel.png`, fullPage: true });
    await page.getByRole("button", { name: "Open Walkthrough" }).click();
    await page.getByRole("button", { name: "Next Frame" }).click();
    await page.screenshot({ path: `${artifactDir}/${viewport.name}-walkthrough.png`, fullPage: true });
    await page.getByRole("button", { name: "Back to workspace" }).click();
    const measurements = await page.evaluate(() => {
      const opener = document.querySelector("#open-logical-walkthrough");
      const controls = [...document.querySelectorAll("button")]
        .map((button) => button.getBoundingClientRect())
        .filter((rect) => rect.width > 0 && rect.height > 0);
      return {
        viewportWidth: innerWidth,
        documentWidth: document.documentElement.scrollWidth,
        focusRestored: document.activeElement === opener,
        minimumControlHeight: Math.min(...controls.map((rect) => rect.height)),
      };
    });
    await page.getByRole("tab", { name: /Setter notes/i }).click();
    const inactiveLogicalDom = await page.getByRole("button", { name: "Next Step" }).count();
    const inactiveLogicalScene = await page.locator(".puzzle-scene-path--annotation").count();
    if (
      measurements.documentWidth > measurements.viewportWidth ||
      !measurements.focusRestored ||
      measurements.minimumControlHeight < 44 ||
      inactiveLogicalDom !== 0 ||
      inactiveLogicalScene !== 0 ||
      messages.length > 0
    ) {
      throw new Error(`${viewport.name} acceptance failed: ${JSON.stringify({ measurements, inactiveLogicalDom, inactiveLogicalScene, messages })}`);
    }
    results.push({ viewport: viewport.name, measurements, inactiveLogicalDom, inactiveLogicalScene, messages });
    await page.close();
  }
  console.log(JSON.stringify(results, null, 2));
} finally {
  await browser.close();
}
