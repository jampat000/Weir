import { describe, expect, it } from "vitest";

import {
  isBelowMinimumSize,
  processingFileLead,
  processingFileStatusLabel,
  type ProcessingFile,
} from "./files-api";

const UNDER_MINIMUM =
  "Skipped because this file is 12.1 MB, under the 50 MB minimum.";

function file(status: ProcessingFile["status"], reason: string) {
  return {
    status,
    status_reason: reason,
    failure_class: null,
  } as ProcessingFile;
}

describe("a file under the workflow's minimum size", () => {
  it("is recognised by the skip the server records for it", () => {
    expect(isBelowMinimumSize(file("skipped", UNDER_MINIMUM))).toBe(true);
    expect(
      isBelowMinimumSize(
        file(
          "skipped",
          "Skipped because this file is 0.0 MB, under the 1 MB minimum.",
        ),
      ),
    ).toBe(true);
  });

  it("is not any other skip, nor a file in another state", () => {
    expect(
      isBelowMinimumSize(
        file(
          "skipped",
          "Skipped because this file is 90.0 MB and exceeds the 50 MB workflow maximum.",
        ),
      ),
    ).toBe(false);
    expect(isBelowMinimumSize(file("skipped", "Not a video file."))).toBe(
      false,
    );
    expect(isBelowMinimumSize(file("processed", UNDER_MINIMUM))).toBe(false);
  });

  it("reads as left alone, and any other skip as skipped", () => {
    expect(processingFileStatusLabel(file("skipped", UNDER_MINIMUM))).toBe(
      "Left alone",
    );
    expect(processingFileLead(file("skipped", UNDER_MINIMUM))).toBe(
      `Left alone. ${UNDER_MINIMUM}`,
    );
    expect(
      processingFileStatusLabel(file("skipped", "Not a video file.")),
    ).toBe("Skipped");
  });
});
