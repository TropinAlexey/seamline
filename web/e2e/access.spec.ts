import { expect, test } from "@playwright/test";
import { api, loginAs, unique } from "./helpers";

test("6. A trade created in tenant A is invisible in tenant B", async ({ page }) => {
  const traderA = await api("A", "trader");
  const counterparty = await traderA.counterparty(unique("E2E Isolation"), 1_000_000);
  const tradeId = await traderA.trade({
    commodityCode: "POWER",
    deliveryPeriod: "2027-03",
    direction: "Sell",
    volume: 5,
    price: 80,
    counterpartyId: counterparty.id,
  });

  await loginAs(page, "B", "trader");
  await page.goto("/trades");
  await expect(page.getByTestId("tenant-name")).toContainText("Tenant B");
  await expect(page.locator(`[data-trade-id="${tradeId}"]`)).toHaveCount(0);

  await page.goto(`/trades/${tradeId}`);
  await expect(page.getByText("Could not load data")).toBeVisible();
});

test("7. Back office sees no trade actions and cannot open approvals", async ({ page }) => {
  const trader = await api("A", "trader");
  const counterparty = await trader.counterparty(unique("E2E Roles"), 1_000_000);
  const tradeId = await trader.trade({
    commodityCode: "GAS",
    deliveryPeriod: "2027-02",
    direction: "Buy",
    volume: 5,
    price: 30,
    counterpartyId: counterparty.id,
  });

  await loginAs(page, "A", "backoffice");
  await page.goto(`/trades/${tradeId}`);
  await expect(page.getByTestId("state-badge").first()).toHaveText("Draft");
  await expect(page.getByTestId("trade-actions").getByRole("button")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "New trade" })).toHaveCount(0);

  await page.goto("/approvals");
  await expect(page).toHaveURL(/\/invoices$/);
  await expect(page.getByRole("link", { name: "Approvals" })).toHaveCount(0);
});

test("A deep link survives a full page reload", async ({ page }) => {
  await loginAs(page, "A", "trader");
  await page.goto("/counterparties");
  await page.reload();
  await expect(page.getByRole("heading", { name: "Counterparties" })).toBeVisible();
});

test("Commodity master data labels volumes with their unit", async ({ page }) => {
  await loginAs(page, "A", "trader");
  await page.goto("/commodities");
  const rows = page.getByTestId("commodity-row");
  await expect(rows.filter({ hasText: "POWER" })).toContainText("MWh");
  await expect(rows.filter({ hasText: "GAS" })).toContainText("Flow");
});
