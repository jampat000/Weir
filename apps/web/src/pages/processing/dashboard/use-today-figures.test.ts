import { describe, expect, it } from "vitest";

import type { FinishedFile } from "../../../lib/activity/processing-outcome";
import { combinedFigures, figuresOf } from "./use-today-figures";

function finished(
  kind: FinishedFile["kind"],
  savedBytes: number | null,
): FinishedFile {
  return {
    id: 1,
    source: "download",
    kind,
    relativePath: "A.mkv",
    libraryId: 1,
    savedBytes,
    removedAudio: 0,
    removedSubtitles: 0,
    sentence: null,
    finishedAt: "2026-10-02T10:00:00",
  };
}

describe("figuresOf", () => {
  it("counts the files Weir cleaned and adds up the space they saved", () => {
    expect(
      figuresOf([
        finished("cleaned", 1000),
        finished("cleaned", 500),
        finished("already", null),
        finished("failed", null),
      ]),
    ).toEqual({ cleaned: 2, savedBytes: 1500 });
  });

  it("is zero for a day with nothing finished", () => {
    expect(figuresOf([])).toEqual({ cleaned: 0, savedBytes: 0 });
  });
});

describe("combinedFigures", () => {
  const downloads = { cleaned: 12, savedBytes: 4000 };

  it("adds a library's cleans to new downloads' on Everything, with the downloads' space saved", () => {
    expect(combinedFigures("all", downloads, 30)).toEqual({
      cleaned: 42,
      savedBytes: 4000,
    });
  });

  it("counts only new downloads for new downloads", () => {
    expect(combinedFigures("download", downloads, 30)).toEqual({
      cleaned: 12,
      savedBytes: 4000,
    });
  });

  it("counts only a library's cleans for library cleaning, which record no space saved", () => {
    expect(combinedFigures("library", downloads, 30)).toEqual({
      cleaned: 30,
      savedBytes: null,
    });
  });
});
