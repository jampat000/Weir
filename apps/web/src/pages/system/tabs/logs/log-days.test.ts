import { describe, expect, it } from "vitest";

import type { SystemLogRow } from "../../../../lib/system/system-log-api";
import { dayHeading, groupByDay } from "./log-days";

const row = (id: string, at: string): SystemLogRow => ({
  id,
  source: "event",
  at,
  level: "info",
  category: "weir",
  workflow: null,
  title: id,
  detail: null,
  event: null,
  job: null,
  server: null,
});

describe("groupByDay", () => {
  it("splits the rows by the day they fall on in the time zone, keeping their order", () => {
    const rows = [
      row("a", "2026-10-02T23:30:00Z"),
      row("b", "2026-10-02T03:00:00Z"),
      row("c", "2026-10-01T20:00:00Z"),
    ];

    expect(
      groupByDay(rows, "UTC").map((d) => [d.key, d.rows.map((r) => r.id)]),
    ).toEqual([
      ["2026-10-02", ["a", "b"]],
      ["2026-10-01", ["c"]],
    ]);
    // In Sydney (UTC+10) the first row is already the next day, and the last is on the 2nd.
    expect(
      groupByDay(rows, "Australia/Sydney").map((d) => [
        d.key,
        d.rows.map((r) => r.id),
      ]),
    ).toEqual([
      ["2026-10-03", ["a"]],
      ["2026-10-02", ["b", "c"]],
    ]);
  });

  it("has no days for no rows", () => {
    expect(groupByDay([], "UTC")).toEqual([]);
  });
});

describe("dayHeading", () => {
  const now = Date.UTC(2026, 9, 2, 12, 0);

  it("names today and yesterday", () => {
    expect(dayHeading("2026-10-02", now, "UTC")).toBe("Today");
    expect(dayHeading("2026-10-01", now, "UTC")).toBe("Yesterday");
  });

  it("names any other day by its weekday and date", () => {
    expect(dayHeading("2026-09-25", now, "UTC")).toMatch(/Friday/);
    expect(dayHeading("2026-09-25", now, "UTC")).toMatch(/25/);
    expect(dayHeading("2026-09-25", now, "UTC")).not.toMatch(/2026/);
  });

  it("adds the year to a day of another year", () => {
    expect(dayHeading("2025-12-31", now, "UTC")).toMatch(/2025/);
  });
});
