import { describe, expect, it } from "vitest";

import type { FinishedFile } from "../../lib/activity/processing-outcome";
import {
  finishedLine,
  finishedNote,
  ordinal,
  timeLeft,
  timeRemaining,
} from "./processing-words";

function finished(overrides: Partial<FinishedFile> = {}): FinishedFile {
  return {
    id: 1,
    source: "download",
    kind: "cleaned",
    relativePath: "Show.S01E01.mkv",
    libraryId: 1,
    savedBytes: null,
    removedAudio: 0,
    removedSubtitles: 0,
    sentence: null,
    finishedAt: "2026-10-02T10:00:00Z",
    ...overrides,
  };
}

describe("how a finished file's outcome reads", () => {
  it("says a file that stopped couldn't finish, and what became of the original", () => {
    expect(finishedLine(finished({ kind: "failed" }))).toBe(
      "Couldn't finish · original kept",
    );
    expect(finishedLine(finished({ kind: "passed" }))).toBe(
      "Couldn't finish · passed through",
    );
  });
});

describe("what the Activity stream adds under an entry", () => {
  it("adds only what the entry's own words do not already say", () => {
    expect(finishedNote(finished({ kind: "failed" }))).toBe("Original kept");
    expect(finishedNote(finished({ kind: "passed" }))).toBe(
      "Couldn't finish it",
    );
    expect(
      finishedNote(
        finished({
          kind: "rejected",
          sentence:
            "Rejected: None of its audio tracks are in English, and ...",
        }),
      ),
    ).toBe("By your rules");
  });

  it("keeps what a cleaned download saved and removed", () => {
    expect(
      finishedNote(
        finished({ savedBytes: 318 * 1024 * 1024, removedAudio: 2 }),
      ),
    ).toBe("Saved 318 MB · removed 2 audio");
  });

  it("reads a library clean's removals from its own entry, and says nothing when there are none to read", () => {
    const library = (sentence: string) =>
      finished({ source: "library", kind: "cleaned", sentence });

    expect(
      finishedNote(
        library(
          "Cleaned X.mkv: removed 2 audio tracks and 1 subtitle track. The original was kept at D:/Keep/X.mkv.",
        ),
      ),
    ).toBe("removed 2 audio tracks and 1 subtitle track");
    expect(finishedNote(library("Cleaned X.mkv."))).toBe("");
  });
});

describe("how a place in line reads", () => {
  it("says first, second, third and the rest with the right ending", () => {
    expect([0, 1, 2, 3, 10, 11, 12, 20, 21].map(ordinal)).toEqual([
      "1st",
      "2nd",
      "3rd",
      "4th",
      "11th",
      "12th",
      "13th",
      "21st",
      "22nd",
    ]);
  });
});

describe("how long a pass has still to run", () => {
  it("says seconds under a minute and a half, then minutes, then hours", () => {
    expect(timeRemaining(41)).toBe("41 s");
    expect(timeRemaining(300)).toBe("5 min");
    expect(timeRemaining(3 * 3600)).toBe("3 h");
  });

  it("adds that it is what is left, and says nothing for an estimate it does not have", () => {
    expect(timeLeft(300)).toBe("5 min left");
    expect(timeLeft(null)).toBe("");
    expect(timeRemaining(null)).toBe("");
  });
});
