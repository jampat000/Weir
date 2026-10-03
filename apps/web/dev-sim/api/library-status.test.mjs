// @vitest-environment node
import { describe, expect, it } from "vitest";

import { ask, createTestSim } from "../test-support.mjs";

const STATUSES = [
  "needs_cleaning",
  "cleaning",
  "matches",
  "cant_clean_yet",
  "left_alone",
];

const filesOf = (sim, libraryId, query = "") =>
  ask(
    sim,
    "GET",
    `/api/v1/processing/libraries/${libraryId}/library-files${query}`,
  ).body;

describe("the status of each library file", () => {
  it("gives every file one status, and the counts add up to the files", () => {
    const { sim } = createTestSim({ withHistory: true });

    for (const library of sim.store.libraries) {
      const body = filesOf(sim, library.id, "?page_size=200");
      const counts = body.summary.by_status;

      expect(body.files.every((file) => STATUSES.includes(file.status))).toBe(
        true,
      );
      expect(Object.values(counts).reduce((a, b) => a + b, 0)).toBe(
        body.summary.files,
      );
      for (const status of STATUSES)
        expect(counts[status]).toBe(
          body.files.filter((file) => file.status === status).length,
        );
    }
  });

  it("narrows to one status, and ignores one it does not know", () => {
    const { sim } = createTestSim({ withHistory: true });
    const library = sim.store.libraries[0];

    const needing = filesOf(sim, library.id, "?status=needs_cleaning");
    const unknown = filesOf(sim, library.id, "?status=nonsense");

    expect(needing.total).toBe(needing.summary.by_status.needs_cleaning);
    expect(
      needing.files.every((file) => file.status === "needs_cleaning"),
    ).toBe(true);
    expect(unknown.total).toBe(unknown.summary.files);
  });

  it("says why a file needs cleaning only for a file that does", () => {
    const { sim } = createTestSim({ withHistory: true });
    const files = sim.store.libraries.flatMap(
      (library) => filesOf(sim, library.id, "?page_size=200").files,
    );

    expect(
      files
        .filter((file) => file.status !== "needs_cleaning")
        .map((file) => file.status_reason),
    ).toEqual(expect.not.arrayContaining(["new", "replaced", "rules_changed"]));
    expect(
      new Set(
        files
          .filter((file) => file.status === "needs_cleaning")
          .map((file) => file.status_reason),
      ),
    ).toEqual(new Set(["new", "replaced", "rules_changed", null]));
  });

  it("puts a file with a clean queued in cleaning, and a file set aside in left alone", () => {
    const { sim } = createTestSim({ withHistory: true });
    const library = sim.store.libraries[0];
    const needing = filesOf(sim, library.id, "?status=needs_cleaning").files[0];

    ask(
      sim,
      "POST",
      `/api/v1/processing/libraries/${library.id}/library-files/clean`,
      { paths: [needing.path], confirm_final_removal: true },
    );
    expect(
      filesOf(sim, library.id, "?status=cleaning").files.map(
        (file) => file.path,
      ),
    ).toEqual([needing.path]);

    const other = filesOf(sim, library.id, "?status=needs_cleaning").files[0];
    ask(
      sim,
      "POST",
      `/api/v1/processing/libraries/${library.id}/library-files/leave-alone`,
      { path: other.path, leave_alone: true },
    );
    expect(
      filesOf(sim, library.id, "?status=left_alone").files.map(
        (file) => file.path,
      ),
    ).toContain(other.path);
  });
});
