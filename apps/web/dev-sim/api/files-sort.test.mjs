// @vitest-environment node
import { describe, expect, it } from "vitest";

import { operations, successResponse } from "../openapi/spec.mjs";
import { violations } from "../openapi/validate.mjs";
import { VERDICT } from "../engine/plan.mjs";
import { release } from "../engine/seed-support.mjs";
import { ask, createTestSim } from "../test-support.mjs";

const WHOLE_LIST = 1000;
const SMALL_PAGE = 7;
const DONE = 0;
const TODO = 1;
const ATTENTION = 3;
const MEANING_RANK = {
  processed: DONE,
  unprocessed: TODO,
  out_of_schedule: TODO,
  processing: 2,
  on_hold: ATTENTION,
  blocked_upstream: TODO,
  passed_through: ATTENTION,
  rejected: ATTENTION,
  processing_failed: 4,
  skipped: 5,
  disabled: 5,
  cancelled: 5,
};

const sim = () => createTestSim({ withHistory: true }).sim;

/** The history, plus a file cleaned just now in the first, linked workflow: its copy waits for the media manager. */
function simWithACopyWaiting() {
  const { sim: simulation } = createTestSim({ withHistory: true });
  const file = release(
    simulation.engine.admit(simulation.store.libraries[0], simulation.now(), {
      verdict: VERDICT.CLEAN,
    }),
  );
  simulation.engine.conclude(file, simulation.now());
  return simulation;
}
const files = (simulation, query = "") =>
  ask(simulation, "GET", `/api/v1/processing/files?limit=${WHOLE_LIST}${query}`)
    .body;
const ids = (body) => body.files.map((file) => file.id);

const linkedLibraryIds = (simulation) =>
  new Set(
    ask(simulation, "GET", "/api/v1/processing/libraries")
      .body.filter((library) => library.manager_connection_ids.length > 0)
      .map((library) => library.id),
  );

/** A copy no media manager has answered about and Weir has not settled, in a workflow linked to one. */
const waitsForManager = (file, linked) =>
  linked.has(file.library_id) &&
  file.handback !== null &&
  !file.handback.outcome &&
  !(file.handback.settled_at && file.handback.release_note);

/** A copy waiting for its media manager is to do, whatever the file's status. */
const rankOf = (file, linked) =>
  waitsForManager(file, linked) ? TODO : MEANING_RANK[file.status];

const EVERY_ORDER = ["file", "status", "when"].flatMap((sort) =>
  ["asc", "desc"].map((direction) => [sort, direction]),
);

describe("the simulated file list in each order", () => {
  it("lists files by path ignoring case, and the other way round", () => {
    const simulation = sim();

    const ascending = files(simulation, "&sort=file&direction=asc");
    const descending = files(simulation, "&sort=file&direction=desc");

    const lowered = ascending.files.map((file) =>
      file.relative_path.toLowerCase(),
    );
    expect(lowered).toEqual([...lowered].sort());
    expect(ids(descending)).toEqual([...ids(ascending)].reverse());
  });

  it("lists files by what their status means, then by the status word", () => {
    const simulation = sim();

    const body = files(simulation, "&sort=status&direction=asc");

    const linked = linkedLibraryIds(simulation);
    const ranks = body.files.map((file) => rankOf(file, linked));
    expect(ranks).toEqual([...ranks].sort((a, b) => a - b));
    expect(new Set(ranks).size).toBeGreaterThan(2);
    const needingAttention = body.files
      .filter((file) => rankOf(file, linked) === ATTENTION)
      .map((file) => file.status);
    expect(needingAttention).toEqual([...needingAttention].sort());
  });

  it("sorts a cleaned copy waiting for its media manager with the to-do files, though its status still reads processed", () => {
    const simulation = simWithACopyWaiting();

    const body = files(simulation, "&sort=status&direction=asc");

    const linked = linkedLibraryIds(simulation);
    const waiting = body.files.filter((file) => waitsForManager(file, linked));
    const finished = body.files.filter(
      (file) => file.status === "processed" && !waitsForManager(file, linked),
    );
    const position = (file) => body.files.indexOf(file);
    expect(waiting.length).toBeGreaterThan(0);
    expect(finished.length).toBeGreaterThan(0);
    expect(waiting.every((file) => file.status === "processed")).toBe(true);
    expect(Math.max(...finished.map(position))).toBeLessThan(
      Math.min(...waiting.map(position)),
    );
    expect(waiting.every((file) => rankOf(file, linked) === TODO)).toBe(true);
  });

  it("keeps the place its status gives a copy a media manager answered about, or one in a Weir only workflow", () => {
    const simulation = simWithACopyWaiting();

    const body = files(simulation, "&sort=status&direction=asc");

    const linked = linkedLibraryIds(simulation);
    const kept = body.files.filter(
      (file) =>
        file.status === "processed" &&
        file.handback !== null &&
        !waitsForManager(file, linked),
    );
    const firstToDo = body.files.findIndex(
      (file) => rankOf(file, linked) === TODO,
    );
    expect(kept.some((file) => file.handback.outcome)).toBe(true);
    expect(kept.some((file) => !linked.has(file.library_id))).toBe(true);
    expect(kept.every((file) => body.files.indexOf(file) < firstToDo)).toBe(
      true,
    );
  });

  it("lists files by when they last changed, newest first unless asked otherwise", () => {
    const simulation = sim();

    const newest = files(simulation, "&sort=when");
    const oldest = files(simulation, "&sort=when&direction=asc");

    const times = newest.files.map((file) => Date.parse(file.updated_at));
    expect(times).toEqual([...times].sort((a, b) => b - a));
    expect(ids(oldest)).toEqual([...ids(newest)].reverse());
  });

  it("lists files newest change first when it is not asked to sort", () => {
    const simulation = sim();

    expect(ids(files(simulation))).toEqual(
      ids(files(simulation, "&sort=when")),
    );
  });

  it.each(EVERY_ORDER)(
    "pages by %s %s through every file once",
    (sort, direction) => {
      const simulation = sim();
      const order = `&sort=${sort}&direction=${direction}`;
      const whole = files(simulation, order);

      const walked = [];
      let cursor = null;
      let pages = 0;
      do {
        const page = ask(
          simulation,
          "GET",
          `/api/v1/processing/files?limit=${SMALL_PAGE}${order}${cursor ? `&cursor=${cursor}` : ""}`,
        ).body;
        walked.push(...ids(page));
        cursor = page.next_cursor;
        pages += 1;
      } while (cursor && pages < WHOLE_LIST);

      expect(whole.next_cursor).toBeNull();
      expect(walked).toEqual(ids(whole));
      expect(new Set(walked).size).toBe(walked.length);
    },
  );

  it("answers in the shape of a ProcessingFilesPageOut, with a cursor while files remain", () => {
    const simulation = sim();
    const op = operations().find(
      (candidate) =>
        candidate.template === "/api/v1/processing/files" &&
        candidate.method === "GET",
    );

    const page = ask(
      simulation,
      "GET",
      "/api/v1/processing/files?limit=3&sort=file",
    ).body;

    expect(violations(successResponse(op).schema, page)).toEqual([]);
    expect(typeof page.next_cursor).toBe("string");
  });
});

describe("the simulated file list refuses what the server refuses", () => {
  it.each([
    ["sort=name", ["query", "sort"]],
    ["sort=last_seen", ["query", "sort"]],
    ["sort=", ["query", "sort"]],
    ["direction=up", ["query", "direction"]],
    ["sort=file&cursor=nonsense", ["query", "cursor"]],
  ])("answers 422 for %s, saying where", (query, loc) => {
    const reply = ask(sim(), "GET", `/api/v1/processing/files?${query}`);

    expect(reply.status).toBe(422);
    expect(reply.body.detail[0].loc).toEqual(loc);
    expect(reply.body.detail[0].input).toBeDefined();
  });

  it("names the choices a bad sort could have been", () => {
    const reply = ask(sim(), "GET", "/api/v1/processing/files?sort=name");

    expect(reply.body.detail[0]).toMatchObject({
      type: "literal_error",
      msg: "Input should be 'file', 'status' or 'when'",
      input: "name",
    });
  });

  it("refuses a cursor made for another sort or direction", () => {
    const simulation = sim();
    const first = ask(
      simulation,
      "GET",
      "/api/v1/processing/files?limit=3&sort=file&direction=asc",
    ).body;

    for (const other of [
      "sort=file&direction=desc",
      "sort=status&direction=asc",
      "direction=asc",
    ]) {
      const reply = ask(
        simulation,
        "GET",
        `/api/v1/processing/files?${other}&cursor=${first.next_cursor}`,
      );
      expect(reply.status).toBe(422);
    }
  });
});
