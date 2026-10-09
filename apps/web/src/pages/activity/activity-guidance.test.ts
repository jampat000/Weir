import { describe, expect, it } from "vitest";

import type { ProcessingFile } from "../../lib/processing/files-api";
import { fileGuidance } from "./activity-guidance";

function skipped(reason: string): ProcessingFile {
  return { status: "skipped", status_reason: reason } as ProcessingFile;
}

describe("what a person can do about a skipped file", () => {
  it("is nothing for a file under the workflow's minimum size, which is left alone", () => {
    const guidance = fileGuidance(
      skipped("Skipped because this file is 12.1 MB, under the 50 MB minimum."),
      false,
    );

    expect(guidance.title).toBe(
      "Left alone because it is under the workflow's minimum size.",
    );
    expect(guidance.next).toMatch(/nothing to do/i);
    expect(guidance.next).not.toMatch(/change the named workflow rule/i);
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
