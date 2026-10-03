import { describe, expect, it } from "vitest";

import { parseSystemTasks, parseSystemTasksFrame } from "./system-tasks-frame";

const TASK = {
  key: "scan-1",
  label: "Scan Movies",
  running: true,
  last_run_at: "2026-10-02T09:00:00Z",
  last_ok: true,
  last_error: null,
  next_run_at: "2026-10-02T10:00:00Z",
  interval_seconds: 600,
};

describe("parseSystemTasks", () => {
  it("reads a list of tasks", () => {
    expect(parseSystemTasks([TASK])).toEqual([TASK]);
  });

  it("reads a task that has not run, with nothing for what a run would say", () => {
    const fresh = {
      ...TASK,
      running: false,
      last_run_at: null,
      last_ok: null,
      next_run_at: null,
      interval_seconds: null,
    };
    expect(parseSystemTasks([fresh])).toEqual([fresh]);
  });

  it("skips an entry that is not a whole task", () => {
    const noLabel: Record<string, unknown> = { ...TASK };
    delete noLabel.label;
    expect(
      parseSystemTasks([noLabel, { ...TASK, running: "yes" }, 7, null, TASK]),
    ).toEqual([TASK]);
  });

  it("is null for something that is not a list", () => {
    expect(parseSystemTasks({ tasks: [TASK] })).toBeNull();
    expect(parseSystemTasks("tasks")).toBeNull();
  });
});

describe("parseSystemTasksFrame", () => {
  it("reads the tasks a frame carries", () => {
    expect(parseSystemTasksFrame(JSON.stringify([TASK]))).toEqual([TASK]);
  });

  it("is null for a frame that is not JSON", () => {
    expect(parseSystemTasksFrame("{")).toBeNull();
  });
});
