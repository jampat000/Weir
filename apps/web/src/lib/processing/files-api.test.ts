import { describe, expect, it } from "vitest";

import {
  isBelowMinimumSize,
  processingFileLead,
  processingFileStatusLabel,
  wasRemovedBelowMinimumSize,
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

  it("is a skipped file with one label and its plain reason, never two words for it", () => {
    const skipped = file("skipped", UNDER_MINIMUM, "below_minimum_size");

    expect(processingFileStatusLabel(skipped)).toBe("Skipped");
    expect(processingFileLead(skipped)).toBe(UNDER_MINIMUM);
    expect(processingFileLead(file("skipped", "Not a video file."))).toBe(
      "Skipped. Not a video file.",
    );
  });

  it("is removed, and says so, once the workflow deleted the file it skipped", () => {
    const removed = file(
      "skipped",
      UNDER_MINIMUM,
      "below_minimum_size_removed",
    );

    expect(isBelowMinimumSize(removed)).toBe(true);
    expect(wasRemovedBelowMinimumSize(removed)).toBe(true);
    expect(
      wasRemovedBelowMinimumSize(
        file("skipped", UNDER_MINIMUM, "below_minimum_size"),
      ),
    ).toBe(false);
    expect(processingFileStatusLabel(removed)).toBe("Removed");
    expect(processingFileLead(removed)).toBe(
      "Removed: under the workflow's minimum size.",
    );
  });
});
