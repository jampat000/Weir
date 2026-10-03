import { describe, expect, it } from "vitest";

import type {
  LibraryFile,
  LibraryTotals,
} from "../../lib/processing/library-mode-api";
import {
  fileMeaning,
  filesCount,
  groupOf,
  headerLead,
  nextScheduledBrief,
  groupSummary,
  scanned,
  statusNote,
  statusWords,
} from "./library-model";

const NOW = Date.UTC(2026, 7, 22, 10, 0, 0);
const MINUTE = 60;
const HOUR_MS = 3_600_000;

function libraryFile(overrides: Partial<LibraryFile>): LibraryFile {
  return {
    path: "Show/Season 1/Show.S01E01.mkv",
    classification: "would_change",
    status: "needs_cleaning",
    status_reason: null,
    cleaned_at: null,
    problem_kind: null,
    manager_title: null,
    removed_audio_tracks: 0,
    removed_subtitle_tracks: 0,
    reason: null,
    summary: null,
    ...overrides,
  } as LibraryFile;
}

describe("groupOf", () => {
  it("prefers the title the media manager knows the file by", () => {
    expect(groupOf(libraryFile({ manager_title: "The Show" }))).toBe(
      "The Show",
    );
  });

  it("names a season folder together with the show above it", () => {
    expect(groupOf(libraryFile({}))).toBe("Show · Season 1");
  });

  it("uses the folder a film sits in", () => {
    expect(groupOf(libraryFile({ path: "Film (2020)/Film.mkv" }))).toBe(
      "Film (2020)",
    );
  });
});

describe("statusWords", () => {
  it("counts what would come out of a file that needs cleaning, with the plural that agrees", () => {
    const file = libraryFile({
      removed_audio_tracks: 2,
      removed_subtitle_tracks: 1,
    });

    expect(statusWords(file)).toBe("Would remove 2 audio, 1 subtitle");
  });

  it("says plainly that a file needs cleaning when the plan has nothing to count", () => {
    expect(statusWords(libraryFile({}))).toBe("Needs cleaning");
  });

  it("says a file matches your rules", () => {
    expect(statusWords(libraryFile({ status: "matches" }))).toBe(
      "Matches your rules",
    );
  });

  it("says a file is being cleaned, or has been set aside", () => {
    expect(statusWords(libraryFile({ status: "cleaning" }))).toBe(
      "Cleaning now",
    );
    expect(statusWords(libraryFile({ status: "left_alone" }))).toBe(
      "Left alone",
    );
  });

  it("gives the reason a file cannot be cleaned yet, from the problem when there is one", () => {
    expect(
      statusWords(
        libraryFile({ status: "cant_clean_yet", problem_kind: "seeding" }),
      ),
    ).toBe("Still seeding");
  });

  it("keeps only the first sentence of why Weir cannot read a file", () => {
    const file = libraryFile({
      status: "cant_clean_yet",
      classification: "cannot_process",
      reason: "Weir could not read this file. It may be damaged.",
    });

    expect(statusWords(file)).toBe("Weir could not read this file");
  });

  it("says a file that is only shared with a download is still seeding", () => {
    expect(
      statusWords(libraryFile({ status: "cant_clean_yet", link_count: 2 })),
    ).toBe("Still seeding");
  });
});

describe("statusNote", () => {
  it.each([
    ["new", "new"],
    ["replaced", "replaced"],
    ["rules_changed", "rules changed"],
  ] as const)("says why a file needs cleaning: %s", (reason, words) => {
    expect(statusNote(libraryFile({ status_reason: reason }))).toBe(words);
  });

  it("says nothing when the scan cannot say why", () => {
    expect(statusNote(libraryFile({ status_reason: null }))).toBeNull();
  });

  it("says when Weir cleaned a file that matches now, and that one it never touched was already clean", () => {
    const cleaned = libraryFile({
      status: "matches",
      cleaned_at: Date.UTC(2026, 9, 3, 12) / 1000,
    });

    expect(statusNote(cleaned)).toMatch(/^cleaned .*\S/);
    expect(statusNote(libraryFile({ status: "matches" }))).toBe(
      "already clean",
    );
  });

  it("says nothing about history on a file in any other status", () => {
    for (const status of ["cleaning", "cant_clean_yet", "left_alone"] as const)
      expect(
        statusNote(libraryFile({ status, cleaned_at: 1_700_000_000 })),
      ).toBeNull();
  });
});

describe("fileMeaning", () => {
  const held = (problem_kind: LibraryFile["problem_kind"]) =>
    libraryFile({ status: "cant_clean_yet", problem_kind });

  it("is the meaning of the file's status", () => {
    expect(fileMeaning(libraryFile({ status: "needs_cleaning" }))).toBe("todo");
    expect(fileMeaning(libraryFile({ status: "cleaning" }))).toBe("doing");
    expect(fileMeaning(libraryFile({ status: "matches" }))).toBe("done");
    expect(fileMeaning(held("seeding"))).toBe("attention");
    expect(fileMeaning(libraryFile({ status: "left_alone" }))).toBe("idle");
  });

  it("is broken for a file Weir cannot read or write, and not for one held back for another reason", () => {
    expect(fileMeaning(held("unreadable"))).toBe("broken");
    expect(fileMeaning(held("no_permission"))).toBe("broken");
    expect(fileMeaning(held("manager_redownload"))).toBe("attention");
    expect(fileMeaning(held(null))).toBe("attention");
  });
});

describe("groupSummary", () => {
  const filesIn = (...statuses: LibraryFile["status"][]) =>
    statuses.map((status) => libraryFile({ status }));

  it("says how many of a title's files need cleaning, as files still to do, whatever else they are", () => {
    expect(
      groupSummary(filesIn("matches", "needs_cleaning", "needs_cleaning")),
    ).toEqual({ meaning: "todo", text: "2 need cleaning" });
  });

  it("says how many match, as done, when none needs anything", () => {
    expect(groupSummary(filesIn("matches", "matches", "left_alone"))).toEqual({
      meaning: "done",
      text: "2 match",
    });
  });

  it("puts Weir at work, then a file held back, before a match", () => {
    expect(groupSummary(filesIn("matches", "cleaning"))).toEqual({
      meaning: "doing",
      text: "1 cleaning",
    });
    expect(groupSummary(filesIn("matches", "cant_clean_yet"))).toEqual({
      meaning: "attention",
      text: "1 can't clean yet",
    });
  });

  it("says a title's held files are broken when Weir cannot read one of them", () => {
    const files = [
      libraryFile({ status: "cant_clean_yet", problem_kind: "seeding" }),
      libraryFile({ status: "cant_clean_yet", problem_kind: "unreadable" }),
    ];

    expect(groupSummary(files)).toEqual({
      meaning: "broken",
      text: "2 can't clean yet",
    });
  });

  it("says that files set aside are left alone, as idle", () => {
    expect(groupSummary(filesIn("left_alone", "left_alone"))).toEqual({
      meaning: "idle",
      text: "2 left alone",
    });
  });
});

describe("headerLead", () => {
  it("says Weir only changes a file when asked while the daily clean is off", () => {
    expect(headerLead(undefined, false)).toBe(
      "Weir reads your library where it is and only changes one when you ask.",
    );
  });

  it("says Weir cleans once a day on its own while the daily clean is on", () => {
    const lead = headerLead(undefined, true);

    expect(lead).toContain("cleans what would change once a day");
    expect(lead).not.toContain("when you ask");
  });
});

describe("scanned", () => {
  it("says when a library has never been scanned", () => {
    expect(scanned(null, NOW)).toBe("not scanned yet");
  });

  it("reads minutes, then hours, then days", () => {
    const at = (secondsAgo: number) => NOW / 1000 - secondsAgo;

    expect(scanned(at(10), NOW)).toBe("checked just now");
    expect(scanned(at(5 * MINUTE), NOW)).toBe("checked 5 min ago");
    expect(scanned(at(3 * 60 * MINUTE), NOW)).toBe("checked 3 h ago");
    expect(scanned(at(3 * 24 * 60 * MINUTE), NOW)).toBe("checked 3 days ago");
  });
});

describe("nextScheduledBrief", () => {
  const clock = (ms: number) => `at ${ms - NOW}`;
  const dayClock = (ms: number) => `on ${ms - NOW}`;
  const run = (hours: number) => ({
    enabled: true,
    next_run_at: new Date(NOW + hours * HOUR_MS).toISOString(),
  });

  it("says nothing while the schedule is off", () => {
    expect(
      nextScheduledBrief(
        { enabled: false, next_run_at: null },
        NOW,
        clock,
        dayClock,
      ),
    ).toBeNull();
  });

  it("tells a run later today as a clock time and a later one with its day", () => {
    expect(nextScheduledBrief(run(3), NOW, clock, dayClock)).toBe(
      `next at ${3 * HOUR_MS}`,
    );
    expect(nextScheduledBrief(run(30), NOW, clock, dayClock)).toBe(
      `next on ${30 * HOUR_MS}`,
    );
  });

  it("says a due run is starting and an impossible one cannot run", () => {
    expect(nextScheduledBrief(run(-1), NOW, clock, dayClock)).toBe(
      "next starting now",
    );
    expect(
      nextScheduledBrief(
        { enabled: true, next_run_at: null },
        NOW,
        clock,
        dayClock,
      ),
    ).toBe("schedule cannot run");
  });
});

describe("filesCount", () => {
  it("gives the number of files and the room they take", () => {
    expect(
      filesCount({ files: 1234, size_bytes: 2_000_000_000 } as LibraryTotals),
    ).toBe("1,234 files · 1.86 GB");
  });
});
