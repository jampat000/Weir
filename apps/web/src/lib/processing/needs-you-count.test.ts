import { describe, expect, it } from "vitest";

import { needsYouCount } from "./needs-you-count";

describe("the count of files that need you", () => {
  it("adds the files that failed to the ones a media manager rejected", () => {
    expect(
      needsYouCount({ processing_failed: 2, rejected: 3, processed: 40 }),
    ).toBe(5);
  });

  it("leaves out files that are only waiting or already done", () => {
    expect(
      needsYouCount({
        unprocessed: 7,
        processing: 1,
        processed: 10,
        skipped: 2,
      }),
    ).toBe(0);
  });

  it("is zero before the counts have loaded", () => {
    expect(needsYouCount(undefined)).toBe(0);
  });
});
