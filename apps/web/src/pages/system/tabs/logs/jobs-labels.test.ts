import { describe, expect, it } from "vitest";

import { JOBS_FILTER_OPTIONS, jobKindLabel } from "./jobs-labels";

describe("JOBS_FILTER_OPTIONS", () => {
  it("names the leased and finalize-failed filters in plain words", () => {
    const leased = JOBS_FILTER_OPTIONS.find(
      (option) => option.value === "leased",
    );
    const finalizeFailed = JOBS_FILTER_OPTIONS.find(
      (option) => option.value === "handler_ok_finalize_failed",
    );
    expect(leased?.label).toBe("Running now");
    expect(finalizeFailed?.label).toBe("Needs recovery");
  });
});

describe("jobKindLabel", () => {
  it("never says hand-back for the unclaimed-copy cleanup job", () => {
    expect(jobKindLabel("processing.unclaimed_handback_cleanup.v1")).toBe(
      "Remove copies nobody picked up",
    );
  });
});
