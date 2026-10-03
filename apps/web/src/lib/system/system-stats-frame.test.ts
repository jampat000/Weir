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

describe("the system.stats frame", () => {
  it("is named as the server names it", () => {
    expect(SYSTEM_STATS_EVENT).toBe("system.stats");
  });

  it("reads the newest reading and its history point", () => {
    const frame = parseSystemStatsFrame(JSON.stringify({ now, point }));
    expect(frame?.now.cpu_percent).toBe(12.4);
    expect(frame?.point.memory_percent).toBe(40);
  });

  it("is nothing for text that is not JSON", () => {
    expect(parseSystemStatsFrame("not json")).toBeNull();
  });

  it("is nothing without both the reading and the point", () => {
    expect(parseSystemStatsFrame(JSON.stringify({ now }))).toBeNull();
    expect(parseSystemStatsFrame(JSON.stringify({ point }))).toBeNull();
    expect(parseSystemStatsFrame("[]")).toBeNull();
  });

  it("keeps a frame whose CPU and memory could not be read", () => {
    const unread = {
      now: { ...now, cpu_percent: null, memory_used_bytes: null },
      point: { ...point, cpu_percent: null, memory_percent: null },
    };
    expect(
      parseSystemStatsFrame(JSON.stringify(unread))?.now.cpu_percent,
    ).toBeNull();
  });

  it("is nothing when a figure is not a number", () => {
    const broken = { now: { ...now, cpu_percent: "high" }, point };
    expect(parseSystemStatsFrame(JSON.stringify(broken))).toBeNull();
  });
});
