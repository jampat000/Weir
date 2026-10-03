// @vitest-environment node
import { describe, expect, it } from "vitest";

import { ask, createTestSim } from "../test-support.mjs";

const WHOLE_LIBRARY = 500;
const SMALL_PAGE = 7;
const LEFT_ALONE_RANK = 5;
const STATUS_RANK = {
  matches: 0,
  needs_cleaning: 1,
  cleaning: 2,
  cant_clean_yet: 3,
  left_alone: LEFT_ALONE_RANK,
};

const filesOf = (sim, libraryId, params = {}) =>
  ask(
    sim,
    "GET",
    `/api/v1/processing/libraries/${libraryId}/library-files?${new URLSearchParams({ page_size: String(WHOLE_LIBRARY), ...params })}`,
  ).body;

const rankOf = (file) =>
  file.status === "cant_clean_yet" &&
  ["unreadable", "no_permission"].includes(file.problem_kind)
    ? 4
    : STATUS_RANK[file.status];

/** A library with a file in each status: one queued to clean and one set aside, beside what the sim starts with. */
function libraryWithEveryStatus() {
  const { sim } = createTestSim({ withHistory: true });
  const library = sim.store.libraries[0];
  const [first, second] = filesOf(sim, library.id, {
    status: "needs_cleaning",
  }).files;
  ask(
    sim,
    "POST",
    `/api/v1/processing/libraries/${library.id}/library-files/clean`,
    { paths: [first.path], confirm_final_removal: true },
  );
  ask(
    sim,
    "POST",
    `/api/v1/processing/libraries/${library.id}/library-files/leave-alone`,
    { path: second.path, leave_alone: true },
  );
  return { sim, library };
}

describe("the simulated library files sorted by status", () => {
  it("run from matches down through left alone, with files that only wait before files Weir cannot open", () => {
    const { sim, library } = libraryWithEveryStatus();

    const body = filesOf(sim, library.id, { sort: "status", direction: "asc" });

    expect(body.sort).toBe("status");
    const ranks = body.files.map(rankOf);
    expect(ranks).toEqual([...ranks].sort((a, b) => a - b));
    expect(new Set(body.files.map((file) => file.status)).size).toBe(5);
    expect(new Set(ranks)).toContain(4);
  });

  it("reverse the statuses when descending and keep files of one rank in path order", () => {
    const { sim, library } = libraryWithEveryStatus();

    const body = filesOf(sim, library.id, {
      sort: "status",
      direction: "desc",
    });

    const ranks = body.files.map(rankOf);
    expect(ranks).toEqual([...ranks].sort((a, b) => b - a));
    for (const rank of new Set(ranks)) {
      const paths = body.files
        .filter((file) => rankOf(file) === rank)
        .map((file) => file.path);
      expect(paths).toEqual([...paths].sort());
    }
  });

  it("page without repeating or skipping a file", () => {
    const { sim, library } = libraryWithEveryStatus();
    const whole = filesOf(sim, library.id, { sort: "status" });

    const walked = [];
    for (let page = 1; walked.length < whole.total; page += 1) {
      const body = filesOf(sim, library.id, {
        sort: "status",
        page_size: String(SMALL_PAGE),
        page: String(page),
      });
      walked.push(...body.files.map((file) => file.path));
    }

    expect(walked).toEqual(whole.files.map((file) => file.path));
    expect(new Set(walked).size).toBe(whole.total);
  });

  it("still fall back to the path for a sort that is not listed", () => {
    const { sim, library } = libraryWithEveryStatus();

    const body = filesOf(sim, library.id, { sort: "statuses" });

    expect(body.sort).toBe("path");
  });
});
