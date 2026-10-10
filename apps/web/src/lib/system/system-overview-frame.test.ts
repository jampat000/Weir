import { describe, expect, it } from "vitest";

import {
  SYSTEM_OVERVIEW_EVENT,
  parseSystemOverviewFrame,
} from "./system-overview-frame";

const overview = {
  version: "1.0.0",
  update: { status: "update_available", latest_version: "1.0.1" },
  uptime_seconds: 3600,
  started_at: "2026-10-02T11:00:00Z",
  runs_as: "service",
  address: "http://pc:8484",
  data_bytes: 123_456,
  browsers_live: 2,
  requests: { median_ms: 12, p95_ms: 80, errors_today: 1 },
  jobs_today: { run: 40, failed: 2 },
  restarts_this_week: 1,
  checks: { passing: 8, total: 9 },
  last_update_backup: {
    path: "C:\\ProgramData\\Weir\\backups\\pre-update\\weir-0076-to-1.0.0-20261010T090000Z.db",
    taken_at: "2026-10-10T09:00:00Z",
    from_version: "1.0.0-rc.12",
    to_version: "1.0.0-rc.13",
    in_data_folder: true,
  },
};

describe("the system.overview frame", () => {
  it("is named as the server names it", () => {
    expect(SYSTEM_OVERVIEW_EVENT).toBe("system.overview");
  });

  it("reads the overview the server sends", () => {
    const frame = parseSystemOverviewFrame(JSON.stringify(overview));
    expect(frame?.checks).toEqual({ passing: 8, total: 9 });
    expect(frame?.jobs_today.failed).toBe(2);
    expect(frame?.update.latest_version).toBe("1.0.1");
    expect(frame?.last_update_backup?.taken_at).toBe("2026-10-10T09:00:00Z");
  });

  it("reads an overview of a Weir that has not updated, with no copy to point to", () => {
    const frame = parseSystemOverviewFrame(
      JSON.stringify({ ...overview, last_update_backup: null }),
    );
    expect(frame?.last_update_backup).toBeNull();
  });

  it("is nothing for text that is not JSON, or JSON that is not an overview", () => {
    expect(parseSystemOverviewFrame("not json")).toBeNull();
    expect(parseSystemOverviewFrame("[]")).toBeNull();
    expect(parseSystemOverviewFrame("{}")).toBeNull();
  });

  it("is nothing when a part the cards read is missing or the wrong kind", () => {
    const without = (key: string) =>
      JSON.stringify({ ...overview, [key]: undefined });
    for (const key of [
      "version",
      "started_at",
      "update",
      "requests",
      "checks",
      "last_update_backup",
    ]) {
      expect(parseSystemOverviewFrame(without(key))).toBeNull();
    }
    expect(
      parseSystemOverviewFrame(
        JSON.stringify({ ...overview, jobs_today: { run: "40", failed: 2 } }),
      ),
    ).toBeNull();
  });
});
