import type { Role } from "../../auth/roles";
import type { TradeState } from "../../api/queries/trading";

export type UiTradeAction = "submit" | "cancel" | "amend" | "deliver" | "approve" | "reject";

// Mirrors Trade.cs's RequireState guards and the endpoints' role policies
// (spec §3.3). Only decides what to *show*; the server re-checks both and
// answers 409 or 403 if this table ever drifts.
const transitions: Record<UiTradeAction, { from: readonly TradeState[]; role: Role }> = {
  submit: { from: ["Draft"], role: "FO" },
  cancel: { from: ["Draft", "Submitted", "CreditPending"], role: "FO" },
  amend: { from: ["Active"], role: "FO" },
  deliver: { from: ["Active"], role: "FO" },
  approve: { from: ["CreditPending"], role: "MO" },
  reject: { from: ["CreditPending"], role: "MO" },
};

export const actionOrder: readonly UiTradeAction[] = [
  "submit",
  "approve",
  "reject",
  "amend",
  "deliver",
  "cancel",
];

export function allowedActions(state: TradeState, role: Role): UiTradeAction[] {
  return actionOrder.filter((action) => {
    const rule = transitions[action];
    return rule.role === role && rule.from.includes(state);
  });
}

// For the state diagram: every state reachable from `state` in one step,
// regardless of role. Submit fans out to Active or CreditPending inside the
// same request; Submitted itself is transient (spec §3.3).
export const nextStates: Record<TradeState, readonly TradeState[]> = {
  Draft: ["Submitted", "Cancelled"],
  Submitted: ["Active", "CreditPending", "Cancelled"],
  CreditPending: ["Active", "Rejected", "Cancelled"],
  Active: ["Active", "Delivered"],
  Rejected: [],
  Cancelled: [],
  Delivered: [],
};

export function isTerminal(state: TradeState): boolean {
  return nextStates[state].length === 0;
}
