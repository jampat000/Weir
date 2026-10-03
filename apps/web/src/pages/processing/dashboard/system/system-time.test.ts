import { describe, expect, it } from "vitest";

import { dayKey, spanWords, whenWords } from "./system-time";

const NOW = Date.parse("2026-10-02T10:00:00Z");
const clock = (ms: number) => `clock:${ms - NOW}`;

describe("dayKey", () => {
  it("is the calendar day in the timezone given", () => {
    const lateUtc = Date.parse("2026-10-01T15:00:00Z");
    expect(dayKey(lateUtc, "UTC")).toBe("2026-10-01");
    expect(dayKey(lateUtc, "Australia/Sydney")).toBe("2026-10-02");
  });

  it("falls back to the browser's day for a timezone it does not know", () => {
    expect(dayKey(NOW, "Not/AZone")).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });
});

describe("spanWords", () => {
  it.each([
    [10_000, "1 min"],
    [12 * 60_000, "12 min"],
    [5 * 3_600_000, "5 h"],
    [47 * 3_600_000, "47 h"],
    [3 * 86_400_000, "3 days"],
  ])("writes %i ms as %s", (ms, words) => {
    expect(spanWords(ms)).toBe(words);
  });
});

describe("whenWords", () => {
  const format = { timeZone: "UTC", clock };

  it("says Today and Yesterday with the clock time", () => {
    const earlier = Date.parse("2026-10-02T03:00:00Z");
    const yesterday = Date.parse("2026-10-01T03:00:00Z");
    expect(whenWords(earlier, NOW, format)).toBe(`Today ${clock(earlier)}`);
    expect(whenWords(yesterday, NOW, format)).toBe(
      `Yesterday ${clock(yesterday)}`,
    );
  });

  it("gives the date for an earlier day", () => {
    const old = Date.parse("2026-09-25T03:00:00Z");
    expect(whenWords(old, NOW, format)).toMatch(/25/);
    expect(whenWords(old, NOW, format)).toContain(clock(old));
  });
});
