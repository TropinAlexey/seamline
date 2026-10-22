import { formatCountdown, shortId } from "./date";

describe("date helpers", () => {
  it("formats a countdown as m:ss and clamps at zero", () => {
    expect(formatCountdown(299_500)).toBe("4:59");
    expect(formatCountdown(61_000)).toBe("1:01");
    expect(formatCountdown(-5_000)).toBe("0:00");
  });

  it("shortens ids to the first eight characters", () => {
    expect(shortId("0f8fad5b-d9cb-469f-a165-70867728950e")).toBe("0f8fad5b");
  });
});
