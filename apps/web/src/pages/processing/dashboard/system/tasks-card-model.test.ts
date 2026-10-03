import { describe, expect, it } from "vitest";

import type { SystemTask } from "../../../../lib/system/system-tasks-frame";
import {
  FAILED_WITHOUT_REASON,
  countdownWords,
  lastResultWords,
  nextWords,
  taskRows,
  tasksSummary,
} from "./tasks-card-model";

const NOW = Date.parse("2026-10-02T10:00:00Z");

function task(overrides: Partial<SystemTask> & { key: string }): SystemTask {
  return {
    label: overrides.key,
    running: false,
    last_run_at: null,
    last_ok: null,
    last_error: null,
    next_run_at: null,
    interval_seconds: null,
    ...overrides,
  };
}

describe("taskRows", () => {
  it("lists a running task first, then the rest by what is due soonest", () => {
    const rows = taskRows([
      task({ key: "late", next_run_at: "2026-10-02T11:00:00Z", last_ok: true }),
      task({ key: "soon", next_run_at: "2026-10-02T10:05:00Z", last_ok: true }),
      task({ key: "busy", running: true, next_run_at: "2026-10-02T12:00:00Z" }),
    ]);
    expect(rows.map((row) => row.key)).toEqual(["busy", "soon", "late"]);
  });

  it("lists a failed task after the running ones and before those that are fine, so it is not left below the fold", () => {
    const rows = taskRows([
      task({ key: "soon", next_run_at: "2026-10-02T10:01:00Z", last_ok: true }),
      task({
        key: "broken",
        next_run_at: "2026-10-02T11:00:00Z",
        last_ok: false,
      }),
      task({ key: "busy", running: true }),
    ]);
    expect(rows.map((row) => row.key)).toEqual(["busy", "broken", "soon"]);
  });

  it("puts a task with no next time after every task that has one", () => {
    const rows = taskRows([
      task({ key: "none" }),
      task({ key: "dated", next_run_at: "2026-10-02T12:00:00Z" }),
    ]);
    expect(rows.map((row) => row.key)).toEqual(["dated", "none"]);
  });

  it("gives a failed task its reason, or says where to look when it has none", () => {
    const [withReason, without] = taskRows([
      task({ key: "a", last_ok: false, last_error: " Disk full. " }),
      task({ key: "b", last_ok: false }),
    ]);
    expect(withReason).toMatchObject({ state: "failed", why: "Disk full." });
    expect(without.why).toBe(FAILED_WITHOUT_REASON);
  });

  it("reads a task that has not finished a run as never run", () => {
    const [row] = taskRows([task({ key: "new" })]);
    expect(row).toMatchObject({ state: "never", why: null, lastAt: null });
  });
});

describe("tasksSummary", () => {
  it("counts what is running and what failed", () => {
    const rows = taskRows([
      task({ key: "a", running: true }),
      task({ key: "b", last_ok: false }),
      task({ key: "c", last_ok: true }),
    ]);
    expect(tasksSummary(rows)).toBe("1 running · 1 failed");
  });

  it("says how many tasks there are when none is running or failed", () => {
    expect(tasksSummary(taskRows([task({ key: "a", last_ok: true })]))).toBe(
      "1 task",
    );
  });

  it("says nothing when there are no tasks", () => {
    expect(tasksSummary([])).toBe("");
  });
});

describe("countdownWords", () => {
  it.each([
    [42_000, "0:42"],
    [61_000, "1:01"],
    [59 * 60_000 + 59_000, "59:59"],
    [5 * 3_600_000, "5 h"],
    [5 * 3_600_000 + 10 * 60_000, "5 h 10 min"],
    [2 * 86_400_000, "2 d"],
    [0, "due"],
    [-5_000, "due"],
  ])("writes %i ms as %s", (ms, words) => {
    expect(countdownWords(ms)).toBe(words);
  });
});

describe("lastResultWords and nextWords", () => {
  const [running, ok, never] = taskRows([
    task({ key: "r", running: true, next_run_at: "2026-10-02T10:10:00Z" }),
    task({
      key: "o",
      last_ok: true,
      last_run_at: "2026-10-02T09:58:00Z",
      next_run_at: "2026-10-02T10:00:42Z",
    }),
    task({ key: "n" }),
  ]);

  it("says running while a task runs, and has no next time for it", () => {
    expect(lastResultWords(running, NOW)).toBe("running");
    expect(nextWords(running, NOW)).toBe("—");
  });

  it("says how long ago a finished task ran and counts down to its next run", () => {
    expect(lastResultWords(ok, NOW)).toBe("2 min ago");
    expect(nextWords(ok, NOW)).toBe("0:42");
  });

  it("shows a dash for a task that never ran and has no next time", () => {
    expect(lastResultWords(never, NOW)).toBe("—");
    expect(nextWords(never, NOW)).toBe("—");
  });
});
