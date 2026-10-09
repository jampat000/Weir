import { describe, expect, it } from "vitest";

import type { ProcessingFile } from "../../lib/processing/files-api";
import { fileGuidance } from "./activity-guidance";

function held(partial: Partial<ProcessingFile> = {}): ProcessingFile {
  return {
    status: "on_hold",
    status_reason: "Weir is confirming that nothing is still writing to it.",
    ...partial,
  } as ProcessingFile;
}

describe("what Activity tells a person about a held file", () => {
  it("tells them to wait for a file that is settling", () => {
    expect(fileGuidance(held(), false).title).toBe(
      "Waiting for the file to settle.",
    );
  });

  it("says there is nothing to do for a file that is no longer in the watched folder, and names no action that needs the file", () => {
    const guidance = fileGuidance(held({ source_gone: true }), false);

    expect(guidance.title).toBe(
      "This file is no longer in the watched folder.",
    );
    expect(guidance.next).toContain("nothing to do");
    expect(guidance.next).not.toContain("Check again");
  });
});

function skipped(
  reason: string,
  skipKind: string | null = null,
): ProcessingFile {
  return {
    status: "skipped",
    status_reason: reason,
    skip_kind: skipKind,
  } as ProcessingFile;
}

describe("what a person can do about a skipped file", () => {
  it("is nothing for a file under the workflow's minimum size, which is left alone", () => {
    const guidance = fileGuidance(
      skipped(
        "Skipped because this file is 12.1 MB, under the 50 MB minimum.",
        "below_minimum_size",
      ),
      false,
    );

    expect(guidance.title).toBe(
      "This is the workflow's minimum size doing its job.",
    );
    expect(guidance.next).toMatch(/nothing to do/i);
    expect(guidance.next).toMatch(/pass through unchanged/i);
    expect(guidance.next).not.toMatch(/change the named workflow rule/i);
  });

  it("is nothing, and offers no file to pass through, for a file the workflow removed", () => {
    const guidance = fileGuidance(
      skipped(
        "Skipped because this file is 12.1 MB, under the 50 MB minimum.",
        "below_minimum_size_removed",
      ),
      false,
    );

    expect(guidance.next).toMatch(/removed from the watched folder/i);
    expect(guidance.next).toMatch(/nothing to do/i);
    expect(guidance.next).not.toMatch(/pass through/i);
  });

  it("is to change the rule, or pass the file through, for any other rule's skip", () => {
    const guidance = fileGuidance(
      skipped(
        "Skipped because its path matches this workflow's exclude patterns.",
      ),
      false,
    );

    expect(guidance.title).toBe("This file does not match the workflow rules.");
    expect(guidance.next).toMatch(/change the named workflow rule/i);
  });
});
