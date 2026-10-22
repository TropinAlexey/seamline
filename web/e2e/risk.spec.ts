import { expect, test } from "@playwright/test";
import { api, loginAs, unique } from "./helpers";

// A delivery month nobody else uses in this run, so the position under test
// is exactly the trade this spec books, even against a long-lived database.
function randomFarPeriod(): string {
  const year = 2040 + Math.floor(Math.random() * 60);
  const month = 1 + Math.floor(Math.random() * 12);
  return `${year}-${String(month).padStart(2, "0")}`;
}

test("4. FO amends an active trade → new version with a field diff", async ({ page }) => {
  const trader = await api("A", "trader");
  const counterparty = await trader.counterparty(unique("E2E Amend"), 10_000_000);
  const tradeId = await trader.trade({
    commodityCode: "POWER",
    deliveryPeriod: randomFarPeriod(),
    direction: "Buy",
    volume: 100,
    price: 45.5,
    counterpartyId: counterparty.id,
  });
  await trader.action(tradeId, "submit");

  await loginAs(page, "A", "trader");
  await page.goto(`/trades/${tradeId}`);
  await expect(page.getByTestId("history-entry")).toHaveCount(3);

  await page.getByRole("button", { name: "Amend" }).click();
  const dialog = page.getByRole("dialog", { name: "Amend trade" });
  await dialog.getByLabel("Volume").fill("150");
  await dialog.getByLabel("Reason").fill("Nomination correction");
  await dialog.getByRole("button", { name: "Amend" }).click();

  await expect(page.getByTestId("history-entry")).toHaveCount(4);
  await expect(page.getByTestId("history-change").filter({ hasText: "volume" })).toHaveText(
    "volume: 100.000 → 150.000",
  );
  await expect(page.getByText("v4", { exact: true }).first()).toBeVisible();
});

test("Moving a curve price moves live MTM on Positions", async ({ browser }) => {
  const period = randomFarPeriod();
  const trader = await api("A", "trader");
  const counterparty = await trader.counterparty(unique("E2E MTM"), 10_000_000);
  const tradeId = await trader.trade({
    commodityCode: "GAS",
    deliveryPeriod: period,
    direction: "Buy",
    volume: 100,
    price: 40,
    counterpartyId: counterparty.id,
  });
  await trader.action(tradeId, "submit");

  const risk = await browser.newPage();
  await loginAs(risk, "A", "risk");

  const publish = async (price: string) => {
    await risk.goto("/curves");
    await risk.getByRole("combobox", { name: "Commodity" }).fill("GAS");
    await risk.getByLabel("Delivery period").fill(period);
    await risk.getByLabel("Price").fill(price);
    await risk.getByRole("button", { name: "Publish" }).click();
    await expect(risk.getByText(`GAS ${period} published`)).toBeVisible();
  };

  const mtmCell = risk.locator(`[data-key="GAS-${period}"] [data-testid="position-mtm"]`);

  await publish("46");
  await risk.goto("/positions");
  // The position appears once Risk consumes TradeActivated; the page polls every 10s.
  await expect(mtmCell).toHaveText("600.00", { timeout: 25_000 }); // (46 − 40) × 100, computed by the API

  await publish("50");
  await risk.goto("/positions");
  await expect(mtmCell).toHaveText("1,000.00");
  await risk.close();
});

test("Front office sees positions but not stress or the audit trail", async ({ page }) => {
  await loginAs(page, "A", "trader");
  await page.goto("/positions");
  await expect(page.getByRole("heading", { name: "Positions & MTM" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Stress" })).toHaveCount(0);
  await expect(page.getByRole("link", { name: "Audit trail" })).toHaveCount(0);

  await page.goto("/audit");
  await expect(page).toHaveURL(/\/trades$/);
});
