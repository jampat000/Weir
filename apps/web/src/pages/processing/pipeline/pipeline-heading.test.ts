import { describe, expect, it } from "vitest";

import { NOW, aCleanJob, aFile, aWriting, lanesOf } from "./pipeline-fixtures";
import { allDoneBy, clockTime, pipelineCount } from "./pipeline-heading";

describe("the heading", () => {
  it("says how many are in progress and when they should all be done", () => {
    const at = new Date(2026, 9, 2, 14, 28).getTime();

    expect(pipelineCount(8, at)).toBe(
      "8 in progress · all done by about 2:28 pm",
    );
  });

  it("leaves the time out when none can be given", () => {
    expect(pipelineCount(3)).toBe("3 in progress");
  });

  it("says plainly that nothing is in progress", () => {
    expect(pipelineCount(0)).toBe("nothing in progress right now");
  });

  it("reads the clock the way people say it", () => {
    expect(clockTime(new Date(2026, 9, 2, 0, 5).getTime())).toBe("12:05 am");
    expect(clockTime(new Date(2026, 9, 2, 12, 0).getTime())).toBe("12:00 pm");
    expect(clockTime(new Date(2026, 9, 2, 9, 41).getTime())).toBe("9:41 am");
  });
});

describe("when everything should be done", () => {
  it("is the slowest of the passes that are writing, from each pass's own estimate", () => {
    const lanes = lanesOf([
      aWriting(1, { progress_eta_seconds: 300 }),
      aWriting(2, { progress_eta_seconds: 900 }),
    ]);

    expect(allDoneBy(lanes, "all", NOW)).toBe(NOW + 900_000);
  });

  it("is not given while a file is still waiting or arriving, because nothing says how long it will take", () => {
    expect(
      allDoneBy(lanesOf([aWriting(1), aFile(2, "unprocessed")]), "all", NOW),
    ).toBeUndefined();
    expect(
      allDoneBy(lanesOf([aWriting(1), aFile(2, "on_hold")]), "all", NOW),
    ).toBeUndefined();
  });

  it("is not given while a pass has no estimate yet", () => {
    const lanes = lanesOf([
      aWriting(1),
      aFile(2, "processing", { progress_stage: "planning" }),
    ]);

    expect(allDoneBy(lanes, "all", NOW)).toBeUndefined();
  });

  it("is not given when nothing is being written", () => {
    expect(allDoneBy(lanesOf([]), "all", NOW)).toBeUndefined();
  });

  it("counts only what the filter lets through", () => {
    const lanes = lanesOf(
      [aWriting(1, { progress_eta_seconds: 300 })],
      [aCleanJob(7, "pending")],
    );

    expect(allDoneBy(lanes, "all", NOW)).toBeUndefined();
    expect(allDoneBy(lanes, "download", NOW)).toBe(NOW + 300_000);
    expect(allDoneBy(lanes, "library", NOW)).toBeUndefined();
  });
});
