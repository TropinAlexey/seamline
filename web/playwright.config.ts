import { defineConfig, devices } from "@playwright/test";

// Runs against the full stack: `docker compose up -d --build` from the repo
// root, then `npm run e2e`. One worker — the specs share demo users and the
// per-IP login limit, and the saga steps are timing-sensitive anyway.
export default defineConfig({
  testDir: "./e2e",
  timeout: 90_000,
  expect: { timeout: 15_000 },
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:8081",
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
