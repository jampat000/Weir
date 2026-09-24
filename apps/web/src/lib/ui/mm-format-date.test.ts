import { describe, expect, it } from "vitest";

import { parseAppDate, parseAppTime } from "./mm-format-date";

const TEN_AM_UTC = Date.UTC(2026, 7, 22, 10, 0, 0);

describe("parseAppDate", () => {
  it("reads a timestamp without a zone as UTC", () => {
    expect(parseAppDate("2026-08-22T10:00:00").getTime()).toBe(TEN_AM_UTC);
  });

  it("keeps a trailing Z as it is", () => {
    expect(parseAppDate("2026-08-22T10:00:00Z").getTime()).toBe(TEN_AM_UTC);
  });

  it("keeps a positive offset", () => {
    expect(parseAppDate("2026-08-22T20:00:00+10:00").getTime()).toBe(
      TEN_AM_UTC,
    );
  });

  it("keeps a negative offset rather than appending a second zone", () => {
    expect(parseAppDate("2026-08-22T05:00:00-05:00").getTime()).toBe(
      TEN_AM_UTC,
    );
  });
});

describe("parseAppTime", () => {
  it("gives null for a missing or unreadable timestamp", () => {
    expect(parseAppTime(null)).toBeNull();
    expect(parseAppTime("")).toBeNull();
    expect(parseAppTime("not a date")).toBeNull();
  });

  it("gives the epoch ms of a readable one", () => {
    expect(parseAppTime("2026-08-22T10:00:00")).toBe(TEN_AM_UTC);
  });
});
