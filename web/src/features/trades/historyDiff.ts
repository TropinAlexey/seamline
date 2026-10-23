import type { TradeHistoryEntry } from "../../api/queries/trading";

// Fields a version can change. Compared by value only — no arithmetic.
const diffedFields = [
  "state",
  "volume",
  "actualVolume",
  "price",
  "notional",
  "commodityCode",
  "deliveryPeriod",
  "direction",
  "counterpartyId",
] as const;

export type DiffedField = (typeof diffedFields)[number];

export interface FieldChange {
  field: DiffedField;
  from: TradeHistoryEntry[DiffedField];
  to: TradeHistoryEntry[DiffedField];
}

export interface HistoryStep {
  entry: TradeHistoryEntry;
  changes: FieldChange[];
}

// Pairs each version with what changed since the previous one. The first
// version has no predecessor, so it carries no changes.
export function diffHistory(history: readonly TradeHistoryEntry[]): HistoryStep[] {
  const ordered = [...history].sort((a, b) => a.version - b.version);
  return ordered.map((entry, index) => {
    const previous = index > 0 ? ordered[index - 1] : undefined;
    const changes = previous
      ? diffedFields
          .filter((field) => previous[field] !== entry[field])
          .map((field) => ({ field, from: previous[field], to: entry[field] }))
      : [];
    return { entry, changes };
  });
}

// When the trade entered CreditPending most recently — the start of the
// saga's 5-minute approval window (spec §4.4). Informational only.
export function creditPendingSince(history: readonly TradeHistoryEntry[]): string | null {
  const pending = [...history]
    .sort((a, b) => b.version - a.version)
    .find((entry) => entry.state === "CreditPending");
  return pending?.validFrom ?? null;
}
