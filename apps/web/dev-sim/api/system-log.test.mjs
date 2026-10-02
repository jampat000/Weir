// @vitest-environment node
import { describe, expect, it } from "vitest";

import { JOB_KIND, JOB_STATUS } from "../engine/jobs.mjs";
import { ask, createTestSim } from "../test-support.mjs";
import { HOUR_MS, MINUTE_MS } from "../wire-time.mjs";

/** A session with one thing of each kind to read: a sign-in, a failed job, a pass that failed, and the seeded server lines. */
function simWithEachSource() {
  const test = createTestSim();
  const { sim, clock } = test;
  const now = clock.now();
  sim.engine.activity.record(
    { type: "auth.login_succeeded", title: "Sign-in finished", trigger: "manual" },
    now - 2 * MINUTE_MS,
  );
  sim.engine.activity.record(
    {
      type: "processing.file_remux_pass_completed",
      title: "Heat processed",
      result: "failed",
      libraryId: 1,
      relativePath: "Heat (1995)/heat.mkv",
    },
    now - 3 * MINUTE_MS,
  );
  const job = sim.engine.jobs.create(
    JOB_KIND.FILE_PASS,
    { relative_media_path: "Heat (1995)/heat.mkv", library_id: 1 },
    now - 5 * MINUTE_MS,
  );
  sim.engine.jobs.setStatus(job.id, JOB_STATUS.FAILED, now - 4 * MINUTE_MS, "ffmpeg stopped");
  return { ...test, job };
}

const log = (sim, query = "") => ask(sim, "GET", `/api/v1/system/log${query}`).body;
const ids = (body) => body.items.map((item) => item.id);
const sourcesOf = (body) => body.items.map((item) => item.source);

describe("the simulated log is one list", () => {
  it("holds events, jobs and server lines, newest first", () => {
    const { sim } = simWithEachSource();

    const body = log(sim);

    expect(new Set(sourcesOf(body))).toEqual(new Set(["event", "job", "server"]));
    const times = body.items.map((item) => Date.parse(item.at));
    expect(times).toEqual([...times].sort((a, b) => b - a));
  });

  it("leaves an event about one file to Activity, but not the server line about its failure", () => {
    const { sim } = simWithEachSource();

    const body = log(sim);

    expect(body.items.some((item) => item.title === "Heat processed" && item.source === "event")).toBe(false);
    const line = body.items.find((item) => item.source === "server" && item.level === "error");
    expect(line.server.traceback).toContain("ffmpeg exited");
  });

  it("gives each row the record of its source and the other two as null", () => {
    const { sim, job } = simWithEachSource();

    const body = log(sim);

    const jobRow = body.items.find((item) => item.id === `job:${job.id}`);
    expect(jobRow.job.last_error).toBe("ffmpeg stopped");
    expect(jobRow.event).toBeNull();
    expect(jobRow.server).toBeNull();
    expect(jobRow.level).toBe("error");
    expect(jobRow.category).toBe("processing");
    const eventRow = body.items.find((item) => item.source === "event");
    expect(eventRow.event.event_type).toBe("auth.login_succeeded");
    expect(eventRow.category).toBe("sign_in");
    expect(eventRow.level).toBe("success");
  });

  it("names a row's workflow", () => {
    const { sim, job } = simWithEachSource();

    const row = log(sim).items.find((item) => item.id === `job:${job.id}`);

    expect(row.workflow.id).toBe(1);
    expect(row.workflow.name).toBe(sim.store.libraries.find((l) => l.id === 1).name);
  });
});

describe("the simulated log's filters", () => {
  it("narrow by source, level and category together", () => {
    const { sim } = simWithEachSource();

    const body = log(sim, "?source=job,server&level=error&category=processing");

    expect(body.items.length).toBeGreaterThan(0);
    expect(body.items.every((item) => item.level === "error" && item.category === "processing")).toBe(true);
    expect(body.items.every((item) => item.source !== "event")).toBe(true);
  });

  it("count what each choice would show with every other filter applied but its own", () => {
    const { sim } = simWithEachSource();

    const body = log(sim, "?source=event&level=error");

    expect(body.total).toBe(0);
    expect(body.counts.source.job).toBeGreaterThan(0);
    expect(body.counts.source.event).toBe(0);
    expect(body.counts.level.success).toBeGreaterThan(0);
    expect(body.counts.level.error).toBe(0);
    expect(Object.keys(body.counts.category)).toHaveLength(9);
  });

  it("search every source's text", () => {
    const { sim } = simWithEachSource();

    expect(sourcesOf(log(sim, "?q=ffmpeg"))).toContain("job");
    expect(sourcesOf(log(sim, "?q=backup%20folder"))).toEqual(["server"]);
    expect(sourcesOf(log(sim, "?q=sign-in"))).toContain("event");
  });

  it("narrow to a workflow, leaving out the server log", () => {
    const { sim } = simWithEachSource();

    const body = log(sim, "?workflow=1");

    expect(body.items.length).toBeGreaterThan(0);
    expect(body.items.every((item) => item.workflow?.id === 1)).toBe(true);
    expect(body.counts.source.server).toBe(0);
  });

  it("narrow to a time", () => {
    const { sim, clock } = simWithEachSource();
    const from = new Date(clock.now() - 10 * MINUTE_MS).toISOString();

    const body = log(sim, `?from=${from}`);

    expect(body.items.length).toBeGreaterThan(0);
    expect(body.items.every((item) => Date.parse(item.at) >= Date.parse(from))).toBe(true);
    expect(body.items.some((item) => item.server?.logger.includes("backups"))).toBe(false);
  });

  it("keep the filters only some sources have to those sources", () => {
    const { sim } = simWithEachSource();

    expect(new Set(sourcesOf(log(sim, "?trigger=manual")))).toEqual(new Set(["event"]));
    expect(new Set(sourcesOf(log(sim, "?status=failed")))).toEqual(new Set(["job"]));
    expect(new Set(sourcesOf(log(sim, "?has_exception=true")))).toEqual(new Set(["server"]));
  });

  it("gather everything about one job", () => {
    const { sim, job } = simWithEachSource();

    const body = log(sim, `?job=${job.id}`);

    expect(ids(body)).toContain(`job:${job.id}`);
    expect(body.items.every((item) => item.source !== "event")).toBe(true);
  });
});

describe("paging the simulated log", () => {
  it("walks every row once, with the same total on each page", () => {
    const { sim } = simWithEachSource();
    const everything = ids(log(sim, "?limit=100"));
    const walked = [];
    let cursor = null;
    let pages = 0;
    do {
      const page = log(sim, `?limit=2${cursor ? `&cursor=${cursor}` : ""}`);
      walked.push(...ids(page));
      expect(page.total).toBe(everything.length);
      cursor = page.next_cursor;
      pages += 1;
    } while (cursor && pages < 100);

    expect(walked).toEqual(everything);
    expect(pages).toBe(Math.ceil(everything.length / 2));
  });

  it("starts at a row's own position even when other rows share its time", () => {
    const { sim, clock } = createTestSim();
    const at = clock.now() - HOUR_MS;
    for (let index = 0; index < 5; index += 1) {
      sim.engine.activity.record({ type: "auth.login_succeeded", title: `Sign-in ${index}` }, at);
    }

    const first = log(sim, "?source=event&limit=2");
    const second = log(sim, `?source=event&limit=3&cursor=${first.next_cursor}`);

    expect([...ids(first), ...ids(second)]).toHaveLength(5);
    expect(new Set([...ids(first), ...ids(second)]).size).toBe(5);
    expect(second.next_cursor).toBeNull();
  });
});

describe("new rows in the simulated log", () => {
  it("appear as they are recorded, which is when the stream tells the page to read it again", () => {
    const { sim, clock } = createTestSim();
    const before = log(sim, "?source=event").total;

    sim.engine.activity.record({ type: "auth.login_succeeded", title: "Sign-in finished" }, clock.now());

    expect(log(sim, "?source=event").total).toBe(before + 1);
  });
});

describe("exporting the simulated log", () => {
  it("is a spreadsheet or JSON of the rows the filters keep", () => {
    const { sim } = simWithEachSource();

    const csv = ask(sim, "GET", "/api/v1/system/log/export?level=error");
    const json = ask(sim, "GET", "/api/v1/system/log/export?format=json&source=job");

    expect(csv.body.split("\n")[0]).toBe("time,source,level,category,workflow,title,detail");
    expect(csv.body.split("\n").length).toBeGreaterThan(1);
    expect(JSON.parse(json.body).every((row) => row.source === "job")).toBe(true);
  });
});
