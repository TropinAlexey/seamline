import { expect, test } from "@playwright/test";
import { api, loginAs, unique } from "./helpers";

// Spec 01's worked example through the UI (ADR-0034): back office nominates,
// loads and certifies; the trade is delivered by the B/L and the final
// invoice settles the provisional one, to the cent.
test("Coal cargo: nomination, loading, certificate, provisional and final invoices", async ({
  page,
}) => {
  const trader = await api("A", "trader");
  const counterparty = await trader.counterparty(unique("E2E Bulk"), 100_000_000);
  const tradeId = await trader.trade({
    commodityCode: "COAL",
    deliveryPeriod: "2027-03",
    direction: "Buy",
    volume: 75_000,
    price: 110,
    counterpartyId: counterparty.id,
    cargoTerms: { tolerancePct: 10, laycanFrom: "2027-03-10", laycanTo: "2027-03-20" },
  });
  await trader.action(tradeId, "submit");

  await loginAs(page, "A", "backoffice");
  await page.goto(`/trades/${tradeId}`);
  const panel = page.getByTestId("shipment-panel");
  await expect(panel).toContainText("67,500.000 – 82,500.000 t");
  await expect(page.getByTestId("shipment-state")).toHaveText("Awaiting nomination");

  await panel.getByLabel("Vessel").fill("MV Demo Carrier");
  await panel.getByLabel("IMO number").fill("9876543");
  await panel.getByLabel("ETA").fill("2027-03-12");
  await panel.getByRole("button", { name: "Nominate vessel" }).click();
  await expect(page.getByTestId("shipment-state")).toHaveText("Vessel nominated");

  await panel.getByLabel("B/L quantity (t)").fill("78500");
  await panel.getByLabel("B/L date").fill("2027-03-14");
  await panel.getByRole("button", { name: "Record loading" }).click();
  await expect(page.getByTestId("shipment-state")).toHaveText("Loaded");
  await expect(page.getByTestId("state-badge").first()).toHaveText("Delivered");
  await expect(panel.locator('[data-kind="Provisional"]')).toContainText("8,635,000.00");

  await panel.getByLabel("NAR (kcal/kg)").fill("5920");
  await panel.getByLabel("Sulphur (%)").fill("0.85");
  await panel.getByLabel("Ash (%)").fill("14.2");
  await panel.getByRole("button", { name: "Record certificate" }).click();
  await expect(page.getByTestId("shipment-state")).toHaveText("Quality certified");

  const final = panel.locator('[data-kind="Final"]');
  await expect(final).toContainText("108.5333", { timeout: 15_000 });
  await expect(final.getByTestId("invoice-amount")).toHaveText("8,519,864.05");
  await expect(final.getByTestId("invoice-balance")).toHaveText("-115,135.95");
});

test("A cargo trade offers no Deliver button — loading delivers it", async ({ page }) => {
  const trader = await api("A", "trader");
  const counterparty = await trader.counterparty(unique("E2E Bulk"), 100_000_000);
  const tradeId = await trader.trade({
    commodityCode: "COAL",
    deliveryPeriod: "2027-04",
    direction: "Sell",
    volume: 50_000,
    price: 112,
    counterpartyId: counterparty.id,
    cargoTerms: { tolerancePct: 5, laycanFrom: "2027-04-05", laycanTo: "2027-04-15" },
  });
  await trader.action(tradeId, "submit");

  await loginAs(page, "A", "trader");
  await page.goto(`/trades/${tradeId}`);
  await expect(page.getByTestId("state-badge").first()).toHaveText("Active");
  const actions = page.getByTestId("trade-actions");
  await expect(actions.getByRole("button", { name: "Amend" })).toBeVisible();
  await expect(actions.getByRole("button", { name: "Deliver" })).toHaveCount(0);
  await expect(page.getByText("Not reportable — REMIT covers electricity")).toBeVisible();
});
