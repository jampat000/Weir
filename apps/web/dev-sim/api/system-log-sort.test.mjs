// @vitest-environment node
import { describe, expect, it } from "vitest";

import { JOB_KIND, JOB_STATUS } from "../engine/jobs.mjs";
import { ask, createTestSim } from "../test-support.mjs";
import { MINUTE_MS } from "../wire-time.mjs";

const WHOLE_LOG = 100;
const SMALL_PAGE = 5;
const LEVEL_ORDER = ["error", "warning", "info", "success"];
const RESULTS = ["failed", "success", "warning", "running"];
const EVENT_TYPES = [
  "auth.login_succeeded",
  "library.scan_completed",
  "backups.created",
];

/** Events, jobs and the seeded server lines sharing levels, categories, workflows and instants, so every sort has ties to break. */
function simWithTies() {
  const test = createTestSim();
  const { sim, clock } = test;
  sim.store.libraries[0].name = "bravo";
  sim.store.libraries[1].name = "Alpha";
  const now = clock.now();
  for (let index = 0; index < 9; index += 1) {
    const at = now - (10 + Math.floor(index / 3)) * MINUTE_MS;
    sim.engine.activity.record(
      {
        type: EVENT_TYPES[index % 3],
        title: `Event ${index}`,
        result: RESULTS[index % 4],
        libraryId: index % 4 === 3 ? undefined : (index % 2) + 1,
      },
      at,
    );
    const job = sim.engine.jobs.create(
      JOB_KIND.FILE_PASS,
      {
        relative_media_path: `Movie ${index}.mkv`,
        library_id: (index % 2) + 1,
      },
      at,
    );
    sim.engine.jobs.setStatus(
      job.id,
      index % 2 ? JOB_STATUS.FAILED : JOB_STATUS.COMPLETED,
      at,
      index % 2 ? "ffmpeg stopped" : null,
    );
  }
  return test;
}

const log = (sim, query = "") =>
  ask(sim, "GET", `/api/v1/system/log?limit=${WHOLE_LOG}${query}`).body;
const ids = (body) => body.items.map((item) => item.id);

const EVERY_ORDER = ["time", "level", "source", "category", "workflow"].flatMap(
  (sort) => ["asc", "desc"].map((direction) => [sort, direction]),
);

describe("the simulated log in each order", () => {
  it("lists levels from errors to successes when ascending, and reverses when descending", () => {
    const { sim } = simWithTies();

    const ascending = log(sim, "&sort=level&direction=asc");
    const descending = log(sim, "&sort=level&direction=desc");

    const ranks = ascending.items.map((item) =>
      LEVEL_ORDER.indexOf(item.level),
    );
    expect(ranks).toEqual([...ranks].sort((a, b) => a - b));
    expect(new Set(ranks).size).toBeGreaterThan(1);
    expect(ids(descending)).toEqual([...ids(ascending)].reverse());
  });

  it("breaks ties by time, newest first when descending and oldest first when ascending", () => {
    const { sim } = simWithTies();

    const descending = log(sim, "&sort=category&direction=desc").items;
    const ascending = log(sim, "&sort=category&direction=asc").items;

    const within = (items) =>
      items.filter((item) => item.category === descending[0].category);
    const newestFirst = within(descending).map((item) => Date.parse(item.at));
    const oldestFirst = within(ascending).map((item) => Date.parse(item.at));
    expect(newestFirst).toEqual([...newestFirst].sort((a, b) => b - a));
    expect(oldestFirst).toEqual([...oldestFirst].sort((a, b) => a - b));
  });

  it("lists sources and categories by their words", () => {
    const { sim } = simWithTies();

    const sources = log(sim, "&sort=source&direction=asc").items.map(
      (item) => item.source,
    );
    const categories = log(sim, "&sort=category&direction=asc").items.map(
      (item) => item.category,
    );

    expect(sources).toEqual([...sources].sort());
    expect(categories).toEqual([...categories].sort());
  });

  it("lists workflows by name ignoring case, and rows with no workflow last whichever way it runs", () => {
    const { sim } = simWithTies();

    for (const direction of ["asc", "desc"]) {
      const items = log(sim, `&sort=workflow&direction=${direction}`).items;
      const named = items.filter((item) => item.workflow?.name);
      const lastNamed = items.findLastIndex((item) => item.workflow?.name);
      const firstUnnamed = items.findIndex((item) => !item.workflow?.name);

      expect(named.length).toBeGreaterThan(0);
      expect(firstUnnamed).toBeGreaterThan(lastNamed);
      const names = named.map((item) => item.workflow.name.toLowerCase());
      expect(names).toEqual(
        [...names].sort((a, b) =>
          direction === "asc" ? a.localeCompare(b) : b.localeCompare(a),
        ),
      );
    }
  });

  it.each(EVERY_ORDER)(
    "pages by %s %s through every row once",
    (sort, direction) => {
      const { sim } = simWithTies();
      const order = `&sort=${sort}&direction=${direction}`;
      const whole = log(sim, order);

      const walked = [];
      let cursor = null;
      let pages = 0;
      do {
        const page = ask(
          sim,
          "GET",
          `/api/v1/system/log?limit=${SMALL_PAGE}${order}${cursor ? `&cursor=${cursor}` : ""}`,
        ).body;
        walked.push(...ids(page));
        cursor = page.next_cursor;
        pages += 1;
      } while (cursor && pages < WHOLE_LOG);

      expect(whole.next_cursor).toBeNull();
      expect(walked).toEqual(ids(whole));
      expect(new Set(walked).size).toBe(walked.length);
    },
  );

  it("lists newest first when it is not asked to sort", () => {
    const { sim } = simWithTies();

    expect(ids(log(sim))).toEqual(ids(log(sim, "&sort=time&direction=desc")));
  });

  it("exports the rows in the order asked for", () => {
    const { sim } = simWithTies();

    const exported = ask(
      sim,
      "GET",
      "/api/v1/system/log/export?format=json&sort=level&direction=asc",
    );

    const levels = JSON.parse(exported.body).map((row) =>
      LEVEL_ORDER.indexOf(row.level),
    );
    expect(levels).toEqual([...levels].sort((a, b) => a - b));
  });
});

describe("the simulated log refuses what the server refuses", () => {
  it.each([
    ["sort=message", ["query", "sort"]],
    ["sort=", ["query", "sort"]],
    ["direction=sideways", ["query", "direction"]],
    ["cursor=nonsense", ["query", "cursor"]],
  ])("answers 422 for %s, saying where", (query, loc) => {
    const { sim } = createTestSim();

    const reply = ask(sim, "GET", `/api/v1/system/log?${query}`);

    expect(reply.status).toBe(422);
    expect(reply.body.detail[0].loc).toEqual(loc);
  });

  it("names the choices a bad sort could have been", () => {
    const { sim } = createTestSim();

    const reply = ask(sim, "GET", "/api/v1/system/log?sort=message");

    expect(reply.body.detail[0].msg).toBe(
      "Input should be 'time', 'level', 'source', 'category' or 'workflow'",
    );
  });

  it("refuses a cursor made for another sort or direction, and an export with a bad sort", () => {
    const { sim } = simWithTies();
    const first = ask(
      sim,
      "GET",
      "/api/v1/system/log?limit=3&sort=level&direction=asc",
    ).body;

    for (const other of [
      "sort=level&direction=desc",
      "sort=category&direction=asc",
      "sort=time&direction=desc",
    ]) {
      expect(
        ask(
          sim,
          "GET",
          `/api/v1/system/log?${other}&cursor=${first.next_cursor}`,
        ).status,
      ).toBe(422);
    }
    expect(
      ask(sim, "GET", "/api/v1/system/log/export?sort=message").status,
    ).toBe(422);
  });
});
