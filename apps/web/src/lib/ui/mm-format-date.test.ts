import { renderHook } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import {
  parseAppDate,
  parseAppTime,
  useAppClockFormatter,
} from "./mm-format-date";

const settings = { timezone: "UTC" };
vi.mock("../settings/queries", () => ({
  useAppSettingsQuery: () => ({ data: { app_timezone: settings.timezone } }),
}));

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

describe("useAppClockFormatter", () => {
  const clockIn = (timezone: string) => {
    settings.timezone = timezone;
    return renderHook(() => useAppClockFormatter()).result.current(TEN_AM_UTC);
  };

  it("writes the time in the timezone chosen in Settings", () => {
    expect(clockIn("UTC")).toMatch(/^10:00\sam$/);
    expect(clockIn("Australia/Brisbane")).toMatch(/^8:00\spm$/);
  });

  it("falls back to the browser's clock for a timezone it does not know", () => {
    expect(clockIn("Not/AZone")).toMatch(/\d:\d\d/);
  });
});
