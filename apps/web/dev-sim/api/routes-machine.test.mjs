// @vitest-environment node
import { describe, expect, it } from "vitest";

import { findOperation, successResponse } from "../openapi/spec.mjs";
import { violations } from "../openapi/validate.mjs";
import { SCENARIO, SCENARIOS } from "../scenarios.mjs";
import { ask, createTestSim } from "../test-support.mjs";
import { fromWire, SECOND_MS } from "../wire-time.mjs";

const sessionOf = (name) =>
  createTestSim({ withHistory: true, scenario: SCENARIOS[name] });

const NOW_FIELDS = [
  "at",
  "cpu_percent",
  "cores",
  "memory_used_bytes",
  "memory_total_bytes",
  "disk_read_bytes_per_sec",
  "disk_write_bytes_per_sec",
  "disk_busy_percent",
  "weir_cpu_percent",
  "weir_memory_bytes",
  "tools_cpu_percent",
  "processing_read_bytes_per_sec",
  "processing_write_bytes_per_sec",
  "processing_speed",
  "running",
  "slots",
];
const POINT_FIELDS = [
  "at",
  "cpu_percent",
  "memory_percent",
  "disk_read_bytes_per_sec",
  "disk_write_bytes_per_sec",
  "processing_read_bytes_per_sec",
  "processing_write_bytes_per_sec",
  "processing_speed",
];
const DRIVE_FIELDS = [
  "name",
  "path",
  "total_bytes",
  "free_bytes",
  "weir_bytes",
  "keep_free_bytes",
  "full_in_days",
  "read_bytes_per_sec",
  "write_bytes_per_sec",
  "busy_percent",
  "workflows",
];
const TASK_FIELDS = [
  "key",
  "label",
  "running",
  "last_run_at",
  "last_ok",
  "last_error",
  "next_run_at",
  "interval_seconds",
];

describe("GET /api/v1/system/stats", () => {
  it.each(Object.values(SCENARIO))(
    "answers in the shape the contract gives in the %s scenario",
    (name) => {
      const { sim } = sessionOf(name);
      const path = "/api/v1/system/stats";

      const { body } = ask(sim, "GET", path);

      expect(
        violations(successResponse(findOperation("GET", path)).schema, body),
      ).toEqual([]);
    },
  );

  it("answers the newest reading, ten minutes of history, the machine and its drives", () => {
    const { sim } = sessionOf(SCENARIO.BUSY);

    const { status, body } = ask(sim, "GET", "/api/v1/system/stats");

    expect(status).toBe(200);
    expect(body).toMatchObject({ interval_ms: 1000, window_s: 600 });
    expect(Object.keys(body.now).sort()).toEqual([...NOW_FIELDS].sort());
    expect(body.machine).toEqual({
      os: "Windows 11 Pro",
      uptime_seconds: expect.any(Number),
      reboot_pending: false,
    });
    expect(body.drives.length).toBeGreaterThan(0);
    for (const drive of body.drives)
      expect(Object.keys(drive).sort()).toEqual([...DRIVE_FIELDS].sort());
  });

  it("gives each history point its own time, a second apart, ending at the newest reading", () => {
    const { sim } = sessionOf(SCENARIO.BUSY);

    const { history, now } = ask(sim, "GET", "/api/v1/system/stats").body;

    expect(history).toHaveLength(600);
    expect(Object.keys(history[0]).sort()).toEqual([...POINT_FIELDS].sort());
    expect(history.at(-1).at).toBe(now.at);
    expect(fromWire(history[1].at) - fromWire(history[0].at)).toBe(SECOND_MS);
  });

  it("has the numbers rounded the way the screen shows them, in range", () => {
    const { sim } = sessionOf(SCENARIO.TROUBLE);

    const { history } = ask(sim, "GET", "/api/v1/system/stats").body;

    for (const point of history) {
      expect(point.cpu_percent).toBeGreaterThanOrEqual(0);
      expect(point.cpu_percent).toBeLessThanOrEqual(100);
      expect(point.memory_percent).toBeGreaterThan(0);
      expect(point.memory_percent).toBeLessThanOrEqual(100);
      expect(Number.isInteger(point.disk_write_bytes_per_sec)).toBe(true);
    }
  });

  it("leaves a network share's disk readings empty and gives it free space", () => {
    const { sim } = sessionOf(SCENARIO.BUSY);
    sim.store.libraries[2].output_folder = "\\\\nas\\media\\Kids";

    const share = ask(sim, "GET", "/api/v1/system/stats").body.drives.find(
      (drive) => drive.name === "\\\\nas\\media",
    );

    expect(share).toMatchObject({
      read_bytes_per_sec: null,
      write_bytes_per_sec: null,
      busy_percent: null,
      workflows: [{ id: 3, name: "Kids", roles: ["output"] }],
    });
    expect(share.free_bytes).toBeGreaterThan(0);
  });

  it("marks the drive that is short of space in the trouble scenario", () => {
    const { sim } = sessionOf(SCENARIO.TROUBLE);

    const drives = ask(sim, "GET", "/api/v1/system/stats").body.drives;

    expect(
      drives.filter((drive) => drive.free_bytes < drive.keep_free_bytes),
    ).toHaveLength(1);
  });
});

describe("GET /api/v1/system/overview", () => {
  it("answers the facts about this Weir", () => {
    const { sim } = sessionOf(SCENARIO.BUSY);

    const { status, body } = ask(sim, "GET", "/api/v1/system/overview");

    expect(status).toBe(200);
    expect(body).toMatchObject({
      version: "3.2.16",
      update: { status: "up_to_date", latest_version: "3.2.16" },
      runs_as: "service",
      address: expect.stringMatching(/^http:\/\/.+:9347$/),
      browsers_live: 0,
      requests: {
        median_ms: expect.any(Number),
        p95_ms: expect.any(Number),
        errors_today: 0,
      },
      restarts_this_week: 0,
    });
    expect(fromWire(body.started_at)).toBe(
      sim.now() - body.uptime_seconds * SECOND_MS,
    );
    expect(body.jobs_today.run).toBeGreaterThan(0);
    expect(body.data_bytes).toBeGreaterThan(0);
  });

  it("counts a browser while it holds the stream open", () => {
    const { sim } = sessionOf(SCENARIO.BUSY);
    sim.machine.browserOpened();
    sim.machine.browserOpened();
    sim.machine.browserClosed();

    expect(ask(sim, "GET", "/api/v1/system/overview").body.browsers_live).toBe(
      1,
    );
  });

  it("counts the jobs that failed today among the ones that ran", () => {
    const { sim } = sessionOf(SCENARIO.TROUBLE);

    const { jobs_today: jobs } = ask(
      sim,
      "GET",
      "/api/v1/system/overview",
    ).body;

    expect(jobs.failed).toBeGreaterThan(0);
    expect(jobs.run).toBeGreaterThan(jobs.failed);
  });

  it("says every check passes in the quiet scenario and which fail in the trouble one", () => {
    const quiet = ask(
      sessionOf(SCENARIO.QUIET).sim,
      "GET",
      "/api/v1/system/overview",
    ).body;
    const trouble = ask(
      sessionOf(SCENARIO.TROUBLE).sim,
      "GET",
      "/api/v1/system/overview",
    ).body;

    expect(quiet.checks.passing).toBe(quiet.checks.total);
    expect(trouble.checks.total).toBe(quiet.checks.total);
    expect(trouble.checks.total - trouble.checks.passing).toBe(3);
    expect(trouble.requests.errors_today).toBeGreaterThan(0);
  });
});

describe("GET /api/v1/system/tasks", () => {
  it("answers every periodic task with the fields the screen reads", () => {
    const { sim } = sessionOf(SCENARIO.BUSY);

    const { status, body } = ask(sim, "GET", "/api/v1/system/tasks");

    expect(status).toBe(200);
    expect(body).toHaveLength(19);
    for (const task of body)
      expect(Object.keys(task).sort()).toEqual([...TASK_FIELDS].sort());
  });
});
