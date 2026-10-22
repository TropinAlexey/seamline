// Formatting only (ADR-0030). Nothing in web/src adds, multiplies or rounds
// money or volume — every notional, MTM and total arrives computed by the
// server, and this module only decides how many digits to show.
export const fractionDigits = {
  price: 4,
  volume: 3,
  amount: 2,
} as const;

export type NumberKind = keyof typeof fractionDigits;

const formatters = Object.fromEntries(
  Object.entries(fractionDigits).map(([kind, digits]) => [
    kind,
    new Intl.NumberFormat("en-GB", {
      minimumFractionDigits: digits,
      maximumFractionDigits: digits,
    }),
  ]),
) as Record<NumberKind, Intl.NumberFormat>;

export function formatNumber(value: number | null | undefined, kind: NumberKind): string {
  return value === null || value === undefined ? "—" : formatters[kind].format(value);
}

export const formatPrice = (value: number | null | undefined) => formatNumber(value, "price");
export const formatVolume = (value: number | null | undefined) => formatNumber(value, "volume");
export const formatAmount = (value: number | null | undefined) => formatNumber(value, "amount");

const compact = new Intl.NumberFormat("en-GB", { notation: "compact", maximumFractionDigits: 1 });

// Axis ticks only — exact amounts stay in tables and tooltips.
export function formatAmountCompact(value: number): string {
  return compact.format(value);
}
