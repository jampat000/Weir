import { describe, expect, it } from "vitest";

import type { ProcessingFile } from "../../lib/processing/files-api";
import { activityFilePath, activityGroupPath } from "./activity-links";

const aFile = (status: ProcessingFile["status"]) =>
  ({ id: 41, status }) as ProcessingFile;

describe("the address of one file in Activity", () => {
  it("opens the file in the group it belongs to, over everything Activity keeps", () => {
    expect(activityFilePath(aFile("on_hold"))).toBe(
      "/activity?show=needs&file=41&within=all",
    );
    expect(activityFilePath(aFile("rejected"))).toBe(
      "/activity?show=failed&file=41&within=all",
    );
    expect(activityFilePath(aFile("skipped"))).toBe(
      "/activity?show=skipped&file=41&within=all",
    );
  });

  it("opens a file that is in no group among all of them", () => {
    expect(
      activityFilePath(aFile("cancelled" as ProcessingFile["status"])),
    ).toBe("/activity?file=41&within=all");
  });
});

describe("the address of one group's view in Activity", () => {
  it("shows the group", () => {
    expect(activityGroupPath("failed")).toBe("/activity?show=failed");
    expect(activityGroupPath("needs")).toBe("/activity?show=needs");
    expect(activityGroupPath("skipped")).toBe("/activity?show=skipped");
  });

  it("narrows to one workflow when there is one", () => {
    expect(activityGroupPath("failed", 3)).toBe(
      "/activity?show=failed&library=3",
    );
    expect(activityGroupPath("failed", null)).toBe("/activity?show=failed");
  });
});
