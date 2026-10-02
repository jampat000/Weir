import { describe, expect, it } from "vitest";

import type {
  SystemNow,
  SystemPoint,
  SystemStats,
} from "../../../../lib/system/system-stats-types";
import {
  computerColumns,
  diskMegabytes,
  machineTags,
} from "./this-computer-model";

const MB = 1024 * 1024;
const GB = 1024 * MB;

const now: SystemNow = {
  at: "2026-10-02T12:00:00Z",
  cpu_percent: 37.4,
  cores: 16,
  memory_used_bytes: 11 * GB,
  memory_total_bytes: 32 * GB,
  disk_read_bytes_per_sec: 4 * MB,
  disk_write_bytes_per_sec: 12 * MB,
  disk_busy_percent: 18,
  weir_cpu_percent: 3.1,
  weir_memory_bytes: 255 * MB,
  tools_cpu_percent: 41,
  processing_read_bytes_per_sec: 0,
  processing_write_bytes_per_sec: 0,
  processing_speed: 0,
  running: 2,
  slots: 4,
};

const point = (
  at: string,
  changes: Partial<SystemPoint> = {},
): SystemPoint => ({
  at,
  cpu_percent: 10,
  memory_percent: 30,
  disk_read_bytes_per_sec: MB,
  disk_write_bytes_per_sec: MB,
  processing_read_bytes_per_sec: 0,
  processing_write_bytes_per_sec: 0,
  processing_speed: 0,
  ...changes,
});

const stats = (changes: Partial<SystemNow> = {}): SystemStats => ({
  interval_ms: 1000,
  window_s: 600,
  now: { ...now, ...changes },
  history: [
    point("2026-10-02T11:59:58Z"),
    point("2026-10-02T11:59:59Z", { cpu_percent: 20 }),
  ],
  machine: {
    os: "Windows 11 Pro",
    uptime_seconds: 3 * 86_400,
    reboot_pending: false,
  },
  drives: [],
});

const column = (key: string, changes: Partial<SystemNow> = {}) =>
  computerColumns(stats(changes)).find((c) => c.key === key)!;

describe("the CPU column", () => {
  it("shows the load now with the cores and the shares of Weir and ffmpeg", () => {
    expect(column("cpu")).toMatchObject({
      value: 37.4,
      unit: "%",
      sub: "16 cores · Weir 3% · ffmpeg 41%",
      scale: { kind: "percent" },
    });
  });

  it("leaves out a share the machine cannot read", () => {
    expect(
      column("cpu", { weir_cpu_percent: null, tools_cpu_percent: null }).sub,
    ).toBe("16 cores");
  });

  it("traces the history at each point's own time", () => {
    const samples = column("cpu").samples;
    expect(samples.map((sample) => sample.value)).toEqual([10, 20]);
    expect(samples[1].at - samples[0].at).toBe(1000);
  });
});

describe("the memory column", () => {
  it("shows gigabytes used, of the total, with the share Weir holds", () => {
    expect(column("memory")).toMatchObject({
      value: 11,
      unit: " GB",
      sub: "of 32.0 GB · Weir 255 MB",
    });
  });
});

describe("the disk column", () => {
  it("adds reads and writes into one figure, with busy time in the note", () => {
    expect(column("disk")).toMatchObject({
      value: 16,
      unit: " MB/s",
      sub: "read 4.0 · write 12 · 18% busy",
      scale: { kind: "rate" },
    });
  });

  it("says it is not available where the machine gives no disk figures", () => {
    const unreadable = column("disk", {
      disk_read_bytes_per_sec: null,
      disk_write_bytes_per_sec: null,
      disk_busy_percent: null,
    });
    expect(unreadable.value).toBeNull();
    expect(unreadable.sub).toBe("not available");
  });

  it("is a gap, not a made-up zero, when either of the two cannot be read", () => {
    expect(diskMegabytes(null, 2 * MB)).toBeNull();
    expect(diskMegabytes(2 * MB, null)).toBeNull();
    expect(diskMegabytes(null, null)).toBeNull();
  });
});

describe("the machine's tags", () => {
  it("names the system and how long it has been up", () => {
    expect(machineTags(stats().machine)).toEqual([
      { text: "Windows 11 Pro" },
      { text: "up 3 days" },
    ]);
  });

  it("warns when a reboot is waiting", () => {
    const tags = machineTags({ ...stats().machine, reboot_pending: true });
    expect(tags.at(-1)).toEqual({ text: "reboot pending", tone: "warning" });
  });
});
