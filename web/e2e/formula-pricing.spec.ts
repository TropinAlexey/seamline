import { expect, test } from "@playwright/test";
import { api, loginAs, unique } from "./helpers";

// Index pricing (ADR-0035, ADR-0038): a coal trade at the average of
// COAL-IDX plus a premium. Fixings are append-only in the demo database, so
// the spec picks a month whose window no earlier run has fixed.
async function freshWindow(): Promise<{ period: string; days: [string, string] }> {
  const risk = await api("A", "risk");
  const fixed = new Set(
    (await risk.get<{ fixingDate: string }[]>("indices/COAL-IDX/fixings")).map((f) =>
      f.fixingDate.slice(0, 7),
    ),
  );
  for (let year = 2040; year < 2100; year++) {
    for (let month = 1; month <= 12; month++) {
      const period = `${year}-${String(month).padStart(2, "0")}`;
      if (fixed.has(period)) continue;
      const weekdays: string[] = [];
      for (let day = 1; weekdays.length < 2; day++) {
        const date = new Date(Date.UTC(year, month - 1, day));
        if (date.getUTCDay() !== 0 && date.getUTCDay() !== 6)
          weekdays.push(date.toISOString().slice(0, 10));
      }
      const [first, second] = weekdays;
      if (first && second) return { period, days: [first, second] };
    }
  }
  throw new Error("No unfixed month left for COAL-IDX");
}

test("Index-priced coal: fixings fill the window and back office fixes the final price", async ({
  browser,
}) => {
  const { period, days } = await freshWindow();
  const risk = await api("A", "risk");
  const trader = await api("A", "trader");
  await risk.curvePoint("COAL", period, 100);
  const counterparty = await trader.counterparty(unique("E2E Index"), 100_000_000);
  const tradeId = await trader.trade({
    commodityCode: "COAL",
    deliveryPeriod: period,
    direction: "Buy",
    volume: 75_000,
    counterpartyId: counterparty.id,
    cargoTerms: { tolerancePct: 10, laycanFrom: days[0], laycanTo: days[1] },
    indexPricing: { indexCode: "COAL-IDX", windowFrom: days[0], windowTo: days[1], premium: 1.5 },
  });
  await trader.action(tradeId, "submit");
  await risk.fixing("COAL-IDX", days[0], 104);

  // Back office: half the window fixed, priced against the forward.
  const opsContext = await browser.newContext();
  const ops = await opsContext.newPage();
  await loginAs(ops, "A", "backoffice");
  await ops.goto(`/trades/${tradeId}`);
  await expect(ops.getByText("COAL-IDX average")).toBeVisible();
  const panel = ops.getByTestId("pricing-panel");
  await expect(panel.getByTestId("pricing-progress")).toHaveText("1 of 2 pricing days fixed");
  // (104.0000 + 100.0000) / 2 + 1.5000
  await expect(panel.getByTestId("pricing-expected")).toHaveText("103.5000");
  await expect(panel.getByRole("button", { name: /Fix price/ })).toHaveCount(0);

  // Middle office publishes the last fixing on the Market data page.
  const moContext = await browser.newContext();
  const mo = await moContext.newPage();
  await loginAs(mo, "A", "risk");
  await mo.goto("/curves");
  await mo.getByRole("tab", { name: "Index fixings" }).click();
  await mo.getByLabel("Fixing date").fill(days[1]);
  await mo.getByLabel("Value").fill("106");
  await mo.getByRole("button", { name: "Publish fixing" }).click();
  await expect(mo.getByTestId("fixings")).toContainText("106.0000");

  // Fully fixed: (104 + 106) / 2 + 1.5 = 106.5000, confirmed by back office.
  await ops.reload();
  await expect(panel.getByTestId("pricing-progress")).toHaveText("2 of 2 pricing days fixed");
  await panel.getByRole("button", { name: "Fix price at 106.5000" }).click();
  await expect(panel.getByText("Price fixed")).toBeVisible();
  await expect(panel.getByTestId("pricing-final")).toHaveText("106.5000");
  await expect(ops.getByTestId("history-entry").last()).toContainText("Pricing window fixed");

  await opsContext.close();
  await moContext.close();
});
