import { describe, expect, it } from "vitest";

import {
  isBelowMinimumSize,
  processingFileLead,
  processingFileStatusLabel,
  type ProcessingFile,
} from "./files-api";

const UNDER_MINIMUM =
  "Skipped because this file is 12.1 MB, under the 50 MB minimum.";

function file(
  status: ProcessingFile["status"],
  reason: string,
  skipKind: string | null = null,
) {
  return {
    status,
    status_reason: reason,
    failure_class: null,
    skip_kind: skipKind,
  } as ProcessingFile;
}

describe("a file under the workflow's minimum size", () => {
  it("is the skip the server gives the code for, whatever its sentence says", () => {
    expect(
      isBelowMinimumSize(file("skipped", UNDER_MINIMUM, "below_minimum_size")),
    ).toBe(true);
    expect(
      isBelowMinimumSize(
        file("skipped", "Small files are left alone.", "below_minimum_size"),
      ),
    ).toBe(true);
  });

  it("is not a skip without the code, nor a file in another state", () => {
    expect(isBelowMinimumSize(file("skipped", UNDER_MINIMUM))).toBe(false);
    expect(
      isBelowMinimumSize(
        file("skipped", "Not a video file.", "something_else"),
      ),
    ).toBe(false);
    expect(
      isBelowMinimumSize(
        file("processed", UNDER_MINIMUM, "below_minimum_size"),
      ),
    ).toBe(false);
  });

  it("reads as left alone, and any other skip as skipped", () => {
    const left = file("skipped", UNDER_MINIMUM, "below_minimum_size");

    expect(processingFileStatusLabel(left)).toBe("Left alone");
    expect(processingFileLead(left)).toBe(`Left alone. ${UNDER_MINIMUM}`);
    expect(
      processingFileStatusLabel(file("skipped", "Not a video file.")),
    ).toBe("Skipped");
  });
});
