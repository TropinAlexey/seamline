import { formatAmount, formatPrice, formatVolume } from "./money";

describe("money formatting", () => {
  it("uses a fixed number of fraction digits per field type", () => {
    expect(formatPrice(45.5)).toBe("45.5000");
    expect(formatVolume(1000)).toBe("1,000.000");
    expect(formatAmount(378.125)).toBe("378.13");
  });

  it("renders a dash for missing values", () => {
    expect(formatAmount(null)).toBe("—");
    expect(formatPrice(undefined)).toBe("—");
  });
});

describe("compact axis formatting", () => {
  it("abbreviates large amounts", async () => {
    const { formatAmountCompact } = await import("./money");
    expect(formatAmountCompact(-7_274_805.12)).toBe("-7.3m");
    expect(formatAmountCompact(0)).toBe("0");
  });
});
