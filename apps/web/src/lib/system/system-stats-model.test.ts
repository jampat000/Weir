import { describe, expect, it } from "vitest";

import { drivesUsedBy, seriesOf, withFrame } from "./system-stats-model";
import type {
  SystemDrive,
  SystemNow,
  SystemPoint,
  SystemStats,
} from "./system-stats-types";

const now = (cpu: number): SystemNow => ({
  at: "2026-10-02T12:00:00Z",
  cpu_percent: cpu,
  cores: 4,
  memory_used_bytes: 1,
  memory_total_bytes: 2,
  disk_read_bytes_per_sec: null,
  disk_write_bytes_per_sec: null,
  disk_busy_percent: null,
  weir_cpu_percent: null,
  weir_memory_bytes: 5,
  tools_cpu_percent: null,
  processing_read_bytes_per_sec: 0,
  processing_write_bytes_per_sec: 0,
  processing_speed: 0,
  running: 0,
  slots: 2,
});

const point = (second: number, cpu = 0): SystemPoint => ({
  at: `2026-10-02T12:00:${String(second).padStart(2, "0")}Z`,
  cpu_percent: cpu,
  memory_percent: 0,
  disk_read_bytes_per_sec: null,
  disk_write_bytes_per_sec: null,
  processing_read_bytes_per_sec: 0,
  processing_write_bytes_per_sec: 0,
  processing_speed: 0,
});

const stats = (history: SystemPoint[], windowS = 600): SystemStats => ({
  interval_ms: 1000,
  window_s: windowS,
  now: now(1),
  history,
  machine: { os: "", uptime_seconds: 0, reboot_pending: null },
  drives: [],
});

describe("adding a frame", () => {
  it("adds its point and takes its reading", () => {
    const next = withFrame(stats([point(1)]), {
      now: now(55),
      point: point(2, 55),
    });
    expect(next.history.map((p) => p.cpu_percent)).toEqual([0, 55]);
    expect(next.now.cpu_percent).toBe(55);
  });

  it("drops the points older than the window, by their own times", () => {
    const next = withFrame(stats([point(1), point(2), point(3)], 2), {
      now: now(1),
      point: point(4),
    });
    expect(next.history.map((p) => p.at.slice(-3))).toEqual([
      "02Z",
      "03Z",
      "04Z",
    ]);
  });

  it("keeps the history when the point is one it already has, and still takes the reading", () => {
    const before = stats([point(1), point(2)]);
    const next = withFrame(before, { now: now(77), point: point(2) });
    expect(next.history).toBe(before.history);
    expect(next.now.cpu_percent).toBe(77);
  });

  it("starts the history from a first point", () => {
    expect(
      withFrame(stats([]), { now: now(1), point: point(1) }).history,
    ).toHaveLength(1);
  });
});

describe("a line from the history", () => {
  it("takes each point's pick at the point's own time", () => {
    const samples = seriesOf(
      [point(1, 10), point(2, 20)],
      (p) => p.cpu_percent,
    );
    expect(samples).toEqual([
      { at: Date.parse("2026-10-02T12:00:01Z"), value: 10 },
      { at: Date.parse("2026-10-02T12:00:02Z"), value: 20 },
    ]);
  });

  it("leaves out a point that cannot be read, rather than drawing it as zero", () => {
    const samples = seriesOf(
      [point(1), point(2)],
      (p) => p.disk_read_bytes_per_sec,
    );
    expect(samples).toEqual([]);
  });
});

describe("the drives a workflow uses", () => {
  const drive = (name: string, ids: number[]): SystemDrive => ({
    name,
    path: `${name}\\`,
    total_bytes: 1,
    free_bytes: 1,
    weir_bytes: 0,
    keep_free_bytes: 0,
    full_in_days: null,
    read_bytes_per_sec: null,
    write_bytes_per_sec: null,
    busy_percent: null,
    workflows: ids.map((id) => ({ id, name: `W${id}`, roles: ["output"] })),
  });
  const drives = [drive("D:", [1, 2]), drive("E:", [3])];

  it("is every drive when no workflow is picked", () => {
    expect(drivesUsedBy(drives, null)).toHaveLength(2);
  });

  it("is only the drives that workflow reads from or writes to", () => {
    expect(drivesUsedBy(drives, 3).map((d) => d.name)).toEqual(["E:"]);
    expect(drivesUsedBy(drives, 9)).toEqual([]);
  });
});
