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
