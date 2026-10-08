import { describe, expect, it } from "vitest";

import {
  SYSTEM_STATS_EVENT,
  parseSystemStatsFrame,
} from "./system-stats-frame";

const now = {
  at: "2026-10-02T12:00:00Z",
  cpu_percent: 12.4,
  cores: 16,
  memory_used_bytes: 1,
  memory_total_bytes: 2,
  disk_read_bytes_per_sec: null,
  disk_write_bytes_per_sec: null,
  disk_busy_percent: null,
  weir_cpu_percent: 3,
  weir_memory_bytes: 5,
  tools_cpu_percent: 0,
  processing_read_bytes_per_sec: 0,
  processing_write_bytes_per_sec: 0,
  processing_speed: null,
  running: 1,
  slots: 4,
};
const point = {
  at: "2026-10-02T12:00:00Z",
  cpu_percent: 12.4,
  memory_percent: 40,
  disk_read_bytes_per_sec: null,
  disk_write_bytes_per_sec: 1000,
  processing_read_bytes_per_sec: 0,
  processing_write_bytes_per_sec: 0,
  processing_speed: null,
};

const machine = {
  os: "Windows 11 Pro",
  uptime_seconds: 3600,
  reboot_pending: null,
};
const drive = {
  name: "D:",
  path: "D:\\",
  total_bytes: 2000,
  free_bytes: 500,
  weir_bytes: 30,
  keep_free_bytes: 100,
  full_in_days: null,
  read_bytes_per_sec: null,
  write_bytes_per_sec: null,
  busy_percent: null,
  workflows: [{ id: 1, name: "Movies", roles: ["watched"] }],
};
const frameOf = (parts: Record<string, unknown> = {}) =>
  JSON.stringify({ now, point, machine, drives: [drive], ...parts });

describe("the system.stats frame", () => {
  it("is named as the server names it", () => {
    expect(SYSTEM_STATS_EVENT).toBe("system.stats");
  });

  it("reads the newest reading and its history point", () => {
    const frame = parseSystemStatsFrame(frameOf());
    expect(frame?.now.cpu_percent).toBe(12.4);
    expect(frame?.point.memory_percent).toBe(40);
  });

  it("reads the machine's facts and the drives it carries", () => {
    const frame = parseSystemStatsFrame(frameOf());
    expect(frame?.machine.os).toBe("Windows 11 Pro");
    expect(frame?.drives.map((d) => d.free_bytes)).toEqual([500]);
    expect(parseSystemStatsFrame(frameOf({ drives: [] }))?.drives).toEqual([]);
  });

  it("is nothing for text that is not JSON", () => {
    expect(parseSystemStatsFrame("not json")).toBeNull();
  });

  it("is nothing without the reading, the point, the machine and the drives", () => {
    for (const missing of ["now", "point", "machine", "drives"]) {
      expect(
        parseSystemStatsFrame(frameOf({ [missing]: undefined })),
      ).toBeNull();
    }
    expect(parseSystemStatsFrame("[]")).toBeNull();
  });

  it("is nothing when a drive is not one", () => {
    expect(
      parseSystemStatsFrame(frameOf({ drives: [{ ...drive, name: 4 }] })),
    ).toBeNull();
    expect(parseSystemStatsFrame(frameOf({ drives: "D:" }))).toBeNull();
  });

  it("keeps a frame whose CPU and memory could not be read", () => {
    const unread = frameOf({
      now: { ...now, cpu_percent: null, memory_used_bytes: null },
      point: { ...point, cpu_percent: null, memory_percent: null },
    });
    expect(parseSystemStatsFrame(unread)?.now.cpu_percent).toBeNull();
  });

  it("is nothing when a figure is not a number", () => {
    const broken = frameOf({ now: { ...now, cpu_percent: "high" } });
    expect(parseSystemStatsFrame(broken)).toBeNull();
  });
});
