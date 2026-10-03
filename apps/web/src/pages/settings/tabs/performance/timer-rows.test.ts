import { describe, expect, it } from "vitest";

import type { MaintenanceFamilyState } from "../../../../lib/processing/maintenance-api";
import type { AppSettings } from "../../../../lib/settings/types";
import { timerRows } from "./timer-rows";

const SETTINGS = {
  configuration_backup_enabled: true,
  configuration_backup_interval_hours: 24,
  configuration_backup_preferred_time: "02:00",
  configuration_backup_last_run_at: "2026-10-02T02:00:00Z",
} as AppSettings;

function family(over: Partial<MaintenanceFamilyState>) {
  return {
    family: "work_temp_stale_sweep",
    enabled: true,
    description: "",
    pending: 0,
    running: 0,
    last_completed_at: null,
    last_failed_at: null,
    last_error: null,
    interval_seconds: 7200,
    next_run_at: "2026-10-03T10:00:00Z",
    ...over,
  } satisfies MaintenanceFamilyState;
}

describe("the timers' rows", () => {
  it("lists the cleanup jobs and then the settings backup", () => {
    const rows = timerRows([family({})], SETTINGS);

    expect(rows.map((row) => row.name)).toEqual([
      "Leftover work files",
      "Cleaned copies nobody picked up",
      "Settings backup",
    ]);
  });

  it("says how often a job runs in words and in seconds, for sorting", () => {
    const [job] = timerRows([family({})], SETTINGS);

    expect(job.when).toBe("Every 2 hours");
    expect(job.everySeconds).toBe(7200);
  });

  it("has no time to sort a switched-off job by, and no next run", () => {
    const [job] = timerRows([family({ enabled: false })], SETTINGS);

    expect(job.when).toBe("Off");
    expect(job.everySeconds).toBeNull();
    expect(job.nextRun).toBeNull();
  });

  it("counts the settings backup in hours as seconds", () => {
    const backup = timerRows([], SETTINGS)[2];

    expect(backup.everySeconds).toBe(86400);
    expect(backup.lastRun).toBe("2026-10-02T02:00:00Z");
  });
});
