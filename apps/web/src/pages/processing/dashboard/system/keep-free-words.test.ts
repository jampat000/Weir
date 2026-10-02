import { describe, expect, it } from "vitest";

import { NO_FREE_SPACE_LIMIT, keepFreeWords } from "./keep-free-words";

describe("keepFreeWords", () => {
  it("reads the amount in the units a drive is measured in", () => {
    expect(keepFreeWords({ minimum_free_disk_space_mb: 10_240 })).toBe(
      "10.00 GB",
    );
  });

  it("says there is no limit when the check is off", () => {
    expect(keepFreeWords({ minimum_free_disk_space_mb: 0 })).toBe(
      NO_FREE_SPACE_LIMIT,
    );
  });
});
