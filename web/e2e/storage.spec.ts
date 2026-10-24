import { expect, test } from "@playwright/test";
import { api, loginAs, unique } from "./helpers";

// Spec 02's worked example (ADR-0036): 40,000 MWh at 28 and 20,000 MWh at
// 31 blend to a WACOG of 29.0000, and 15,000 MWh withdrawn leaves 45,000 MWh
// there at a cost of stock of 1,305,000.00. Most movements go through the
// API; the last injection and the first withdrawal go through the Storage
// page as back office would record them.
test("Gas storage: injections blend the WACOG, a withdrawal keeps it", async ({ page }) => {
  const backoffice = await api("A", "backoffice");
  const facility = unique("E2E Storage");
  const contract = await backoffice.post<{ id: string }>("storage-contracts", {
    facility,
    commodityCode: "GAS",
    capacity: 100_000,
    maxInjectionPerDay: 2_000,
    maxWithdrawalPerDay: 3_000,
    validFrom: "2027-04-01",
    validTo: "2028-03-31",
  });
  const day = (offset: number) =>
    new Date(Date.UTC(2027, 3, 1 + offset)).toISOString().slice(0, 10);
  for (let d = 0; d < 20; d++)
    await backoffice.post(`storage-contracts/${contract.id}/injections`, {
      gasDay: day(d),
      quantity: 2_000,
      unitCost: 28,
    });
  for (let d = 20; d < 29; d++)
    await backoffice.post(`storage-contracts/${contract.id}/injections`, {
      gasDay: day(d),
      quantity: 2_000,
      unitCost: 31,
    });

  await loginAs(page, "A", "backoffice");
  await page.goto("/storage");
  const card = page.getByTestId("storage-card").filter({ hasText: facility });
  await expect(card.getByTestId("storage-fill")).toHaveText("58,000.000 of 100,000.000 MWh");

  await card.getByLabel("Gas day").fill(day(29));
  await card.getByLabel("Quantity (MWh)").fill("2000");
  await card.getByLabel("Unit cost (EUR/MWh)").fill("31");
  await card.getByRole("button", { name: "Record" }).click();
  await expect(card.getByTestId("storage-wacog")).toHaveText("29.0000");
  await expect(card.getByTestId("storage-fill")).toHaveText("60,000.000 of 100,000.000 MWh");

  await card.getByText("Withdraw", { exact: true }).click();
  await card.getByLabel("Gas day").fill(day(30));
  await card.getByLabel("Quantity (MWh)").fill("3000");
  await card.getByRole("button", { name: "Record" }).click();
  await expect(card.getByTestId("storage-fill")).toHaveText("57,000.000 of 100,000.000 MWh");
  await expect(card.getByTestId("storage-wacog")).toHaveText("29.0000");

  // Above the 3,000 MWh/day rate on the same gas day: refused by the server.
  await card.getByLabel("Quantity (MWh)").fill("1");
  await card.getByRole("button", { name: "Record" }).click();
  await expect(card.getByRole("alert")).toContainText("withdrawal rate");

  // The other four withdrawal days, 3,000 MWh each.
  for (let d = 31; d < 35; d++)
    await backoffice.post(`storage-contracts/${contract.id}/withdrawals`, {
      gasDay: day(d),
      quantity: 3_000,
    });
  await page.reload();
  await expect(card.getByTestId("storage-fill")).toHaveText("45,000.000 of 100,000.000 MWh");
  await expect(card.getByTestId("storage-wacog")).toHaveText("29.0000");
  await expect(card).toContainText("1,305,000.00");

  // Each withdrawal day's cost, 3,000 × 29.0000, in the ledger.
  await card.getByRole("button", { name: "Show ledger" }).click();
  const ledger = card.getByTestId("storage-ledger").locator("tbody tr");
  await expect(ledger).toHaveCount(35);
  await expect(
    ledger.filter({ hasText: "Withdrawal" }).filter({ hasText: "87,000.00" }),
  ).toHaveCount(5);
});
