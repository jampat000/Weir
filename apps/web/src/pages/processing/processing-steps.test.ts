import { describe, expect, it } from "vitest";
import type { ProcessingFile } from "../../lib/processing/files-api";
import { buildLanes } from "./processing-model";

function file(overrides: Partial<ProcessingFile>): ProcessingFile {
  return {
    id: 1,
    library_id: 1,
    library_name: "TV",
    relative_path: "The.Quiet.Harbour.S01E03.1080p.WEB-DL.mkv",
    status: "processing",
    progress_percent: null,
    progress_status: null,
    progress_stage: null,
    ...overrides,
  } as ProcessingFile;
}

function lanesFor(...files: ProcessingFile[]) {
  return buildLanes(files, [], new Map(), new Map());
}

describe("the step a card is on", () => {
  it("is the stage the server names for a file being worked on", () => {
    const lanes = lanesFor(file({ progress_stage: "planning" }));

    expect(lanes.working[0].step).toBe("plan");
  });

  it("falls back to Checking with no percent and Write with one when the server names no stage", () => {
    const lanes = lanesFor(
      file({ id: 1, progress_percent: null }),
      file({ id: 2, progress_percent: 30 }),
    );

    expect(lanes.working.map((item) => item.step)).toEqual([
      "checking",
      "write",
    ]);
  });

  it("puts a file on its final checks on Verify, or on Hand back once the server says so", () => {
    const lanes = lanesFor(
      file({ id: 1, progress_status: "finishing" }),
      file({
        id: 2,
        progress_status: "finishing",
        progress_stage: "handing_back",
      }),
    );

    expect(lanes.handing.map((item) => item.step)).toEqual([
      "verify",
      "hand-back",
    ]);
  });

  it("ignores a stage it does not know", () => {
    const lanes = lanesFor(file({ progress_percent: 5, progress_stage: "?" }));

    expect(lanes.working[0].step).toBe("write");
  });
});
