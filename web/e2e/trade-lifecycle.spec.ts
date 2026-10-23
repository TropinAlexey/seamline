import { expect, test } from "@playwright/test";
import { api, loginAs, unique } from "./helpers";

// Credit exposure is max(0, MtM) against the curve, falling back to full
// notional only when no curve point exists. A month nobody publishes (the
// curve import only covers the current month) makes a breach deterministic
// whatever prices earlier runs left on the curve.
const noCurvePeriod = "2031-12";

test("1. FO creates and submits a trade within the limit → Active", async ({ page }) => {
  const setup = await api("A", "trader");
  const counterparty = await setup.counterparty(unique("E2E Big Limit"), 10_000_000);

  await loginAs(page, "A", "trader");
  await page.goto("/trades");
  await page.getByRole("button", { name: "New trade" }).click();

  const dialog = page.getByRole("dialog", { name: "New trade" });
  await dialog.getByLabel("Commodity").click();
  await page.getByRole("option", { name: "POWER — Power" }).click();
  await dialog.getByLabel("Delivery period").fill("2027-02");
  await dialog.getByLabel("Volume (MWh)").fill("100");
  await dialog.getByLabel("Price").fill("85.5");
  await dialog.getByLabel("Counterparty").click();
  await page.getByRole("option", { name: counterparty.name }).click();
  await dialog.getByRole("button", { name: "Create & submit" }).click();

  await expect(page).toHaveURL(/\/trades\/[0-9a-f-]{36}$/);
  await expect(page.getByTestId("state-badge").first()).toHaveText("Active");
  await expect(page.getByTestId("history-entry")).toHaveCount(3);
});

test("2. A limit breach waits for MO approval; the saga resolves it → Active", async ({
  browser,
}) => {
  const trader = await api("A", "trader");
  const counterparty = await trader.counterparty(unique("E2E Small Limit"), 1_000);
  const tradeId = await trader.trade({
    commodityCode: "GAS",
    deliveryPeriod: noCurvePeriod,
    direction: "Buy",
    volume: 1_000,
    price: 30,
    counterpartyId: counterparty.id,
  });
  const submit = await trader.action(tradeId, "submit");
  expect(submit).toMatchObject({ state: "CreditPending", outcome: "Breached" });

  const page = await browser.newPage();
  await loginAs(page, "A", "risk");
  await page.goto("/approvals");
  await expect(page.locator(`[data-trade-id="${tradeId}"]`)).toBeVisible();

  await page.goto(`/trades/${tradeId}`);
  await expect(page.getByText(/to auto-reject/)).toBeVisible();
  await page.getByRole("button", { name: "Approve" }).click();

  await expect(page.getByTestId("state-badge").first()).toHaveText("Active", { timeout: 30_000 });
  await expect(page.getByTestId("history-changed-by").last()).toHaveText("risk");
  await page.close();
});

test("3. MO rejects → Rejected, with every version in history", async ({ browser }) => {
  const trader = await api("A", "trader");
  const counterparty = await trader.counterparty(unique("E2E Small Limit"), 1_000);
  const tradeId = await trader.trade({
    commodityCode: "POWER",
    deliveryPeriod: noCurvePeriod,
    direction: "Buy",
    volume: 500,
    price: 80,
    counterpartyId: counterparty.id,
  });
  await trader.action(tradeId, "submit");

  const page = await browser.newPage();
  await loginAs(page, "A", "risk");
  await page.goto(`/trades/${tradeId}`);
  await page.getByRole("button", { name: "Reject" }).click();

  await expect(page.getByTestId("state-badge").first()).toHaveText("Rejected", { timeout: 30_000 });
  // Draft, Submitted, CreditPending, Rejected — submit writes two versions.
  await expect(page.getByTestId("history-entry")).toHaveCount(4);
  await expect(page.getByTestId("history-changed-by").last()).toHaveText("risk");
  await page.close();
});

test("5. FO delivers → BO sees the invoice", async ({ page, browser }) => {
  const trader = await api("A", "trader");
  const counterparty = await trader.counterparty(unique("E2E Big Limit"), 10_000_000);
  const tradeId = await trader.trade({
    commodityCode: "POWER",
    deliveryPeriod: "2027-01",
    direction: "Buy",
    volume: 10,
    price: 90,
    counterpartyId: counterparty.id,
  });
  await trader.action(tradeId, "submit");

  await loginAs(page, "A", "trader");
  await page.goto(`/trades/${tradeId}`);
  await page.getByRole("button", { name: "Deliver" }).click();
  await expect(page.getByTestId("state-badge").first()).toHaveText("Delivered");

  const backOffice = await browser.newPage();
  await loginAs(backOffice, "A", "backoffice");
  await expect(async () => {
    await backOffice.goto("/invoices");
    await expect(
      backOffice.locator(`[data-testid="invoice-row"][data-trade-id="${tradeId}"]`),
    ).toBeVisible({
      timeout: 2_000,
    });
  }).toPass({ timeout: 30_000 });
  await backOffice.close();
});
