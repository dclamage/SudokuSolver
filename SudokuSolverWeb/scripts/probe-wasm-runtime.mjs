import { chromium } from "@playwright/test";

const baseUrl = process.argv[2];
if (baseUrl === undefined) {
  throw new Error("Usage: node scripts/probe-wasm-runtime.mjs <base-url>");
}
const timeoutMs = Number(process.argv[3] ?? 30_000);

const browser = await chromium.launch({ headless: true });
const page = await browser.newPage();
const failures = [];
const runtimeResponses = [];

page.on("pageerror", (error) => {
  failures.push({ kind: "pageerror", error: error.message });
});
page.on("console", (message) => {
  if (message.type() === "error") {
    failures.push({ kind: "console", error: message.text() });
  }
});

page.on("requestfailed", (request) => {
  failures.push({
    kind: "requestfailed",
    url: request.url(),
    error: request.failure()?.errorText ?? "unknown request failure",
  });
});
page.on("response", (response) => {
  if (
    response.url().includes("/solver/") ||
    response.url().includes("solver.worker")
  ) {
    runtimeResponses.push({ url: response.url(), status: response.status() });
  }
  if (response.status() >= 400) {
    failures.push({
      kind: "response",
      url: response.url(),
      status: response.status(),
    });
  }
});

try {
  await page.goto(new URL("/wasm-probe.html", baseUrl).href);
  let waitError;
  try {
    await page.waitForFunction(
      () =>
        document
          .querySelector("#wasm-probe-status")
          ?.getAttribute("data-state") !== "pending",
      undefined,
      { timeout: timeoutMs },
    );
  } catch (error) {
    waitError = error instanceof Error ? error.message : String(error);
  }
  const state = await page
    .locator("#wasm-probe-status")
    .getAttribute("data-state");
  const probeText = await page.locator("#wasm-probe-status").textContent();
  let probe;
  try {
    probe = JSON.parse(probeText);
  } catch {
    probe = { outcome: "invalid-probe-output", text: probeText };
  }
  const solverWorker = runtimeResponses.find(({ url }) =>
    url.includes("solver.worker"),
  );
  const dotnetEntrypoint = runtimeResponses.find(({ url }) =>
    /\/solver\/_framework\/dotnet\.js(?:$|\?)/.test(url),
  );
  const hotReloadInitializerRequests = runtimeResponses.filter(({ url }) =>
    url.includes("Microsoft.DotNet.HotReload"),
  );
  const evidence = {
    state,
    probe,
    failures,
    runtime: {
      solverWorker,
      dotnetEntrypoint,
      frameworkAssetCount: runtimeResponses.filter(({ url }) =>
        url.includes("/solver/_framework/"),
      ).length,
      hotReloadInitializerRequests,
    },
    waitError,
  };
  console.log(JSON.stringify(evidence, undefined, 2));

  if (
    state !== "succeeded" ||
    probe.outcome !== "resolved" ||
    probe.kind !== "result" ||
    probe.correlation?.requestId !== "live-validate" ||
    probe.correlation?.operation !== "validate" ||
    solverWorker?.status !== 200 ||
    dotnetEntrypoint?.status !== 200 ||
    dotnetEntrypoint.url.includes("?import") ||
    hotReloadInitializerRequests.length !== 0 ||
    failures.length !== 0
  ) {
    process.exitCode = 1;
  }
} finally {
  await browser.close();
}
