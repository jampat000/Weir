// @vitest-environment node
import { describe, expect, it } from "vitest";

import { operations, successResponse } from "../openapi/spec.mjs";
import { violations } from "../openapi/validate.mjs";
import { ask, createTestSim } from "../test-support.mjs";

const WHOLE_LIST = 1000;
const SMALL_PAGE = 7;
const ATTENTION = 3;
const MEANING_RANK = {
  processed: 0,
  unprocessed: 1,
  out_of_schedule: 1,
  processing: 2,
  on_hold: ATTENTION,
  blocked_upstream: 1,
  passed_through: ATTENTION,
  rejected: ATTENTION,
  processing_failed: 4,
  skipped: 5,
  disabled: 5,
  cancelled: 5,
};

const sim = () => createTestSim({ withHistory: true }).sim;
const files = (simulation, query = "") =>
  ask(simulation, "GET", `/api/v1/processing/files?limit=${WHOLE_LIST}${query}`)
    .body;
const ids = (body) => body.files.map((file) => file.id);

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

    const ranks = body.files.map((file) => MEANING_RANK[file.status]);
    expect(ranks).toEqual([...ranks].sort((a, b) => a - b));
    expect(new Set(ranks).size).toBeGreaterThan(2);
    const needingAttention = body.files
      .filter((file) => MEANING_RANK[file.status] === ATTENTION)
      .map((file) => file.status);
    expect(needingAttention).toEqual([...needingAttention].sort());
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
