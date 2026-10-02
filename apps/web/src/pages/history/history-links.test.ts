import { describe, expect, it } from "vitest";

import type { ProcessingFile } from "../../lib/processing/files-api";
import { historyFilePath } from "./history-links";

const aFile = (status: ProcessingFile["status"]) =>
  ({ id: 41, status }) as ProcessingFile;

describe("the address of one file in History", () => {
  it("opens the file in the group it belongs to, over everything History keeps", () => {
    expect(historyFilePath(aFile("on_hold"))).toBe(
      "/history?show=needs&file=41&within=all",
    );
    expect(historyFilePath(aFile("rejected"))).toBe(
      "/history?show=failed&file=41&within=all",
    );
    expect(historyFilePath(aFile("skipped"))).toBe(
      "/history?show=skipped&file=41&within=all",
    );
  });

  it("opens a file that is in no group among all of them", () => {
    expect(
      historyFilePath(aFile("cancelled" as ProcessingFile["status"])),
    ).toBe("/history?file=41&within=all");
  });
});
