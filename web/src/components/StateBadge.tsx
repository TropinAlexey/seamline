import { Badge } from "@mantine/core";
import type { TradeState } from "../api/queries/trading";

const colors: Record<TradeState, string> = {
  Draft: "gray",
  Submitted: "indigo",
  CreditPending: "yellow",
  Active: "green",
  Rejected: "red",
  Cancelled: "dark",
  Delivered: "blue",
};

// Colour is never the only signal — the badge always carries the state name.
export function StateBadge({ state }: { state: TradeState }) {
  return (
    <Badge color={colors[state]} variant="light" data-testid="state-badge">
      {state}
    </Badge>
  );
}
