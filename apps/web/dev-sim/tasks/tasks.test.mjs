// @vitest-environment node
import { describe, expect, it } from "vitest";

import { JOB_KIND } from "../engine/jobs.mjs";
import { SCENARIO, SCENARIOS } from "../scenarios.mjs";
import { createTestSim } from "../test-support.mjs";
import { fromWire, MINUTE_MS, SECOND_MS } from "../wire-time.mjs";
import { TASK_KEY } from "./weir-tasks.mjs";

const sessionOf = (name, options = {}) =>
  createTestSim({ withHistory: true, scenario: SCENARIOS[name], ...options });

/** Runs the simulation fast enough that a day passes in about a minute. */
const DAY_SPEEDUP = 1500;

const row = (sim, key) => sim.tasks.rows().find((entry) => entry.key === key);
/** The task that is due first. */
const soonestDue = (sim) =>
  sim.tasks
    .rows()
    .reduce((best, entry) =>
      fromWire(entry.next_run_at) < fromWire(best.next_run_at) ? entry : best,
    );
const keysOf = (sim) => sim.tasks.rows().map((entry) => entry.key);

/** Moves a second at a time and keeps what `look` says at each one. */
function watch(advance, seconds, look) {
  const seen = [];
  for (let second = 0; second < seconds; second += 1) {
    advance(SECOND_MS);
    seen.push(look());
  }
  return seen;
}

describe("the list of periodic tasks", () => {
  it("has a scan and a library clean for each workflow, named after it, and every task Weir runs on its own", () => {
    const { sim } = sessionOf(SCENARIO.BUSY);

    const labels = sim.tasks.rows().map((entry) => entry.label);

    expect(labels).toEqual(
      expect.arrayContaining([
        "Scan Movies",
        "Scan 4K Movies",
        "Clean TV library",
        "Clean Kids library",
        "Clear leftover files",
        "Clear unclaimed copies",
        "Configuration backup check",
        "Check media managers",
        "Prune file history",
        "Look up artwork",
        "Check for updates",
      ]),
    );
    expect(keysOf(sim)).toHaveLength(19);
  });

  it("says when each task last ran and when it next runs, with the next run still to come", () => {
    const { sim, clock } = sessionOf(SCENARIO.BUSY);

    for (const task of sim.tasks.rows()) {
      expect(fromWire(task.next_run_at)).toBeGreaterThanOrEqual(
        clock.now() - SECOND_MS,
      );
      expect(fromWire(task.last_run_at)).toBeLessThanOrEqual(clock.now());
      expect(task.running).toBe(false);
    }
    expect(row(sim, TASK_KEY.BACKUP)).toMatchObject({
      last_ok: true,
      last_error: null,
      interval_seconds: 60,
    });
  });

  it("follows Settings: a task that is switched off goes, and a workflow that is added gets its tasks", () => {
    const { sim, advance } = sessionOf(SCENARIO.BUSY);

    sim.store.suite.configuration_backup_enabled = false;
    sim.store.libraries.push({
      ...sim.store.libraries[0],
      id: 9,
      name: "Anime",
    });
    advance(SECOND_MS);

    expect(keysOf(sim)).not.toContain(TASK_KEY.BACKUP);
    expect(row(sim, "scan-9").label).toBe("Scan Anime");
    expect(keysOf(sim)).toContain("library-clean-9");
  });

  it("runs a scan on the workflow's own interval, showing it running while it does", () => {
    const { sim, advance } = sessionOf(SCENARIO.QUIET);
    const dueAt = fromWire(row(sim, "scan-1").next_run_at);

    advance(dueAt - sim.now() - SECOND_MS);
    const before = row(sim, "scan-1");
    const during = watch(advance, 6, () => row(sim, "scan-1").running);
    const after = row(sim, "scan-1");

    expect(before.running).toBe(false);
    expect(during).toContain(true);
    expect(after.running).toBe(false);
    expect(fromWire(after.last_run_at)).toBeGreaterThanOrEqual(dueAt);
    expect(fromWire(after.next_run_at) - fromWire(after.last_run_at)).toBe(
      300 * SECOND_MS,
    );
  });

  it("changes the next scan when the workflow's scan interval is changed in Settings", () => {
    const { sim, advance } = sessionOf(SCENARIO.QUIET);
    sim.store.libraries[0].scan_interval_seconds = 60;
    advance(fromWire(row(sim, "scan-1").next_run_at) - sim.now());
    advance(10 * SECOND_MS);

    const task = row(sim, "scan-1");

    expect(task.interval_seconds).toBe(60);
    expect(fromWire(task.next_run_at) - fromWire(task.last_run_at)).toBe(
      60 * SECOND_MS,
    );
  });

  it("does not scan while Weir is paused and set not to scan", () => {
    const { sim, advance } = sessionOf(SCENARIO.QUIET);
    sim.engine.setPause({ paused: true, scanWhilePaused: false }, sim.now());
    advance(fromWire(row(sim, "scan-1").next_run_at) - sim.now());

    const during = watch(advance, 10, () => row(sim, "scan-1").running);

    expect(during).not.toContain(true);
  });

  it("runs the clean-up tasks and writes each run in the Activity record", () => {
    const { sim, advance } = sessionOf(SCENARIO.QUIET);
    const dueAt = fromWire(row(sim, TASK_KEY.LEFTOVER_FILES).next_run_at);

    advance(dueAt - sim.now());
    advance(10 * SECOND_MS);

    expect(row(sim, TASK_KEY.LEFTOVER_FILES).last_ok).toBe(true);
    expect(
      sim.engine.activity
        .all()
        .filter(
          (event) =>
            event.type === "processing.work_temp_stale_sweep_completed",
        ).length,
    ).toBe(2);
  });

  it("writes a backup when the configuration backup runs", () => {
    const { sim, advance } = sessionOf(SCENARIO.QUIET, { speed: DAY_SPEEDUP });
    const backups = sim.store.backups.length;
    advance(MINUTE_MS);

    expect(sim.store.backups.length).toBe(backups + 1);
    expect(row(sim, TASK_KEY.BACKUP).last_ok).toBe(true);
  });
});

describe("library cleans in the list", () => {
  it("are due when the engine's own timer says, and a clean then starts for that workflow", () => {
    const { sim, advance } = sessionOf(SCENARIO.BUSY);
    const cleans = sim.tasks
      .rows()
      .filter((entry) => entry.key.startsWith("library-clean-"));
    const soonest = cleans.reduce((best, entry) =>
      fromWire(entry.next_run_at) < fromWire(best.next_run_at) ? entry : best,
    );
    const libraryId = Number(soonest.key.split("-")[2]);

    advance(fromWire(soonest.next_run_at) - sim.now() + SECOND_MS);

    expect(
      sim.engine.jobs
        .all()
        .filter(
          (job) =>
            job.kind === JOB_KIND.LIBRARY_CLEAN &&
            job.payload.library_id === libraryId &&
            job.createdAt >= fromWire(soonest.next_run_at) - SECOND_MS,
        ).length,
    ).toBeGreaterThan(0);
  });

  it("show running while a clean is under way, and its end as the last run", () => {
    const { sim, advance } = sessionOf(SCENARIO.BUSY);

    const seen = watch(advance, 4 * 60, () =>
      sim.tasks
        .rows()
        .filter((entry) => entry.key.startsWith("library-clean-"))
        .map((entry) => entry.running),
    );

    expect(seen.some((states) => states.includes(true))).toBe(true);
    const lastRuns = sim.tasks
      .rows()
      .filter((entry) => entry.key.startsWith("library-clean-"))
      .map((entry) => fromWire(entry.last_run_at));
    expect(Math.max(...lastRuns)).toBeGreaterThan(sim.now() - 4 * MINUTE_MS);
  });

  it("are spaced further apart in the quiet scenario than the busy one", () => {
    const quiet = sessionOf(SCENARIO.QUIET).sim;
    const busy = sessionOf(SCENARIO.BUSY).sim;

    expect(row(quiet, "library-clean-1").interval_seconds).toBeGreaterThan(
      row(busy, "library-clean-1").interval_seconds * 5,
    );
  });
});

describe("telling listeners about the tasks", () => {
  it("sends the whole list when a task starts and again when it ends", () => {
    const { sim, advance } = sessionOf(SCENARIO.QUIET);
    const soonest = soonestDue(sim);
    const lists = [];
    sim.tasks.onChange((rows) => lists.push(rows));

    advance(fromWire(soonest.next_run_at) - sim.now());
    advance(10 * SECOND_MS);

    const states = lists.map(
      (rows) => rows.find((entry) => entry.key === soonest.key).running,
    );
    expect(
      states.filter((state, index) => state !== states[index - 1]),
    ).toEqual([true, false]);
    expect(lists[0]).toHaveLength(19);
  });

  it("says nothing while nothing starts or ends", () => {
    const { sim, advance } = sessionOf(SCENARIO.QUIET);
    const lists = [];
    sim.tasks.onChange((rows) => lists.push(rows));

    advance(20 * SECOND_MS);

    expect(lists).toEqual([]);
  });
});

describe("a failing task", () => {
  it("shows the backup as failed with the reason in the trouble scenario, and succeeding in the others", () => {
    const trouble = sessionOf(SCENARIO.TROUBLE).sim;
    const busy = sessionOf(SCENARIO.BUSY).sim;

    expect(row(trouble, TASK_KEY.BACKUP)).toMatchObject({
      last_ok: false,
      last_error: expect.stringContaining("E:\\Backups\\Weir"),
    });
    expect(row(busy, TASK_KEY.BACKUP).last_ok).toBe(true);
  });

  it("fails again at its next try, writes an error in the log, and keeps trying every so often", () => {
    const { sim, advance } = sessionOf(SCENARIO.TROUBLE);
    const retryAt = fromWire(row(sim, TASK_KEY.BACKUP).next_run_at);

    advance(retryAt - sim.now());
    advance(10 * SECOND_MS);

    const task = row(sim, TASK_KEY.BACKUP);
    expect(task.last_ok).toBe(false);
    expect(fromWire(task.last_run_at)).toBeGreaterThanOrEqual(retryAt);
    expect(fromWire(task.next_run_at) - fromWire(task.last_run_at)).toBe(
      5 * MINUTE_MS,
    );
    expect(
      sim.engine.activity
        .all()
        .filter((event) => event.type === "suite.configuration_backup_failed")
        .map((event) => event.result),
    ).toEqual(["failed"]);
  });
});
