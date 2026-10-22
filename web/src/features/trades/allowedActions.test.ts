import type { Role } from "../../auth/roles";
import type { TradeState } from "../../api/queries/trading";
import { allowedActions, isTerminal, type UiTradeAction } from "./allowedActions";

describe("allowedActions", () => {
  const cases: [TradeState, Role, UiTradeAction[]][] = [
    ["Draft", "FO", ["submit", "cancel"]],
    ["Draft", "MO", []],
    ["Draft", "BO", []],
    ["Submitted", "FO", ["cancel"]],
    ["CreditPending", "FO", ["cancel"]],
    ["CreditPending", "MO", ["approve", "reject"]],
    ["CreditPending", "BO", []],
    ["Active", "FO", ["amend", "deliver"]],
    ["Active", "MO", []],
    ["Active", "BO", []],
    ["Rejected", "FO", []],
    ["Cancelled", "MO", []],
    ["Delivered", "FO", []],
  ];

  it.each(cases)("%s × %s → %j", (state, role, expected) => {
    expect(allowedActions(state, role)).toEqual(expected);
  });

  it("never offers anything to back office", () => {
    const states: TradeState[] = [
      "Draft",
      "Submitted",
      "CreditPending",
      "Active",
      "Rejected",
      "Cancelled",
      "Delivered",
    ];
    for (const state of states) expect(allowedActions(state, "BO")).toEqual([]);
  });

  it("treats Rejected, Cancelled and Delivered as terminal", () => {
    expect(isTerminal("Rejected")).toBe(true);
    expect(isTerminal("Cancelled")).toBe(true);
    expect(isTerminal("Delivered")).toBe(true);
    expect(isTerminal("Active")).toBe(false);
  });
});
