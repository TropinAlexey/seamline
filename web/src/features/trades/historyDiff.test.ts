import type { TradeHistoryEntry } from "../../api/queries/trading";
import { creditPendingSince, diffHistory } from "./historyDiff";

function entry(overrides: Partial<TradeHistoryEntry>): TradeHistoryEntry {
  return {
    version: 1,
    validFrom: "2027-01-01T10:00:00Z",
    changedBy: "trader",
    changeReason: "Created",
    state: "Draft",
    commodityCode: "POWER",
    deliveryPeriod: "2027-08",
    direction: "Buy",
    volume: 100,
    price: 45.5,
    notional: 4550,
    counterpartyId: "cp-1",
    ...overrides,
  };
}

describe("diffHistory", () => {
  it("reports no changes for the first version and only changed fields after", () => {
    const steps = diffHistory([
      entry({ version: 2, state: "Active", changedBy: "system" }),
      entry({ version: 1 }),
      entry({ version: 3, state: "Active", volume: 150, notional: 6825 }),
    ]);

    expect(steps.map((s) => s.entry.version)).toEqual([1, 2, 3]);
    expect(steps[0]!.changes).toEqual([]);
    expect(steps[1]!.changes).toEqual([{ field: "state", from: "Draft", to: "Active" }]);
    expect(steps[2]!.changes).toEqual([
      { field: "volume", from: 100, to: 150 },
      { field: "notional", from: 4550, to: 6825 },
    ]);
  });
});

describe("creditPendingSince", () => {
  it("returns when the trade most recently entered CreditPending", () => {
    const history = [
      entry({ version: 1 }),
      entry({ version: 2, state: "Submitted" }),
      entry({ version: 3, state: "CreditPending", validFrom: "2027-01-01T10:00:05Z" }),
    ];
    expect(creditPendingSince(history)).toBe("2027-01-01T10:00:05Z");
  });

  it("is null when the trade never waited for approval", () => {
    expect(creditPendingSince([entry({ version: 1 })])).toBeNull();
  });
});
