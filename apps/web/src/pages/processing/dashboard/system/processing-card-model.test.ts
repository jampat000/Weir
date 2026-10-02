import { describe, expect, it } from "vitest";

import type { FinishedFile } from "../../../../lib/activity/processing-outcome";
import type {
  SystemNow,
  SystemStats,
} from "../../../../lib/system/system-stats-types";
import {
  processingColumns,
  speedFigure,
  workPills,
} from "./processing-card-model";
import { recentSince, recentWorkOf } from "./use-recent-work";

const MB = 1024 * 1024;

const now: SystemNow = {
  at: "2026-10-02T12:00:00Z",
  cpu_percent: 10,
  cores: 8,
  memory_used_bytes: 0,
  memory_total_bytes: 1,
  disk_read_bytes_per_sec: null,
  disk_write_bytes_per_sec: null,
  disk_busy_percent: null,
  weir_cpu_percent: null,
  weir_memory_bytes: 5,
  tools_cpu_percent: null,
  processing_read_bytes_per_sec: 20 * MB,
  processing_write_bytes_per_sec: 6 * MB,
  processing_speed: 148,
  running: 2,
  slots: 4,
};

const stats = (changes: Partial<SystemNow> = {}): SystemStats => ({
  interval_ms: 1000,
  window_s: 600,
  now: { ...now, ...changes },
  history: [
    {
      at: "2026-10-02T11:59:59Z",
      cpu_percent: 1,
      memory_percent: 1,
      disk_read_bytes_per_sec: null,
      disk_write_bytes_per_sec: null,
      processing_read_bytes_per_sec: 10 * MB,
      processing_write_bytes_per_sec: 4 * MB,
      processing_speed: 0,
    },
  ],
  machine: { os: "", uptime_seconds: 0, reboot_pending: false },
  drives: [],
});

describe("the disk work column", () => {
  const disk = () => processingColumns(stats())[0];

  it("shows what the running passes write, with what they read", () => {
    expect(disk()).toMatchObject({
      value: 6,
      unit: " MB/s",
      sub: ["writing · reading 20", "reading 20", "read 20"],
    });
  });

  it("draws reads and writes as two lines", () => {
    const lines = disk().lines;
    expect(lines.map((line) => line.key)).toEqual(["read", "write"]);
    expect(lines[0].samples[0].value).toBe(10);
    expect(lines[1].samples[0].value).toBe(4);
  });
});

describe("the speed column", () => {
  it("shows how fast the passes go and how many files that is across", () => {
    expect(processingColumns(stats())[1]).toMatchObject({
      value: 148,
      unit: "×",
      sub: ["across 2 files", "2 files"],
    });
  });

  it("says nothing is running, and has no figure, when nothing is", () => {
    const idle = processingColumns(
      stats({ running: 0, processing_speed: 0 }),
    )[1];
    expect(idle.value).toBeNull();
    expect(idle.sub).toEqual(["nothing running", "idle"]);
  });

  it("draws the speeds the history holds, an idle second as zero", () => {
    expect(
      processingColumns(stats())[1].lines[0].samples.map(
        (sample) => sample.value,
      ),
    ).toEqual([0]);
  });
});

describe("speed figures", () => {
  it("reads to a tenth under ten, dropping a zero tenth, and whole above", () => {
    expect(speedFigure(0.8)).toBe("0.8");
    expect(speedFigure(2)).toBe("2");
    expect(speedFigure(35.4)).toBe("35");
    expect(speedFigure(1260)).toBe("1,260");
  });
});

describe("the pills under the columns", () => {
  it("say how many passes run, and what the last ten minutes came to", () => {
    expect(
      workPills(now, { done: 14, savedBytes: 3 * 1024 ** 3 }).map(
        (pill) => pill.text,
      ),
    ).toEqual(["2 of 4 running", "14 files done", "3.00 GB saved"]);
  });

  it("show only the passes while the finished work is still being read", () => {
    expect(workPills(now, undefined)).toHaveLength(1);
  });
});

describe("the last ten minutes of finished work", () => {
  const finished = (savedBytes: number): FinishedFile => ({
    id: 1,
    source: "download",
    kind: "cleaned",
    relativePath: "A.mkv",
    libraryId: 1,
    savedBytes,
    removedAudio: 0,
    removedSubtitles: 0,
    sentence: null,
    finishedAt: "2026-10-02T10:00:00",
  });

  it("starts ten minutes back, on a minute boundary", () => {
    expect(recentSince(Date.parse("2026-10-02T12:34:56Z"))).toBe(
      "2026-10-02T12:24:00.000+00:00",
    );
  });

  it("counts the server's totals and adds up the space saved", () => {
    expect(recentWorkOf([finished(1000), finished(500)], 120, 30)).toEqual({
      done: 150,
      savedBytes: 1500,
    });
  });
});
