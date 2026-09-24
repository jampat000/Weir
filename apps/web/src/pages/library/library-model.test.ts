import { describe, expect, it } from "vitest";

import type { LibraryFile } from "../../lib/processing/library-mode-api";
import { groupOf, scanned, verdictOf } from "./library-model";

const NOW = Date.UTC(2026, 7, 22, 10, 0, 0);
const MINUTE = 60;

function libraryFile(overrides: Partial<LibraryFile>): LibraryFile {
  return {
    path: "Show/Season 1/Show.S01E01.mkv",
    classification: "would_change",
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

describe("verdictOf", () => {
  it("counts what would come out, with the plural that agrees", () => {
    const file = libraryFile({
      removed_audio_tracks: 2,
      removed_subtitle_tracks: 1,
    });

    expect(verdictOf(file)).toBe("Removes 2 audio, 1 subtitle");
  });

  it("keeps only the first sentence of why a file is not touched", () => {
    const file = libraryFile({
      classification: "cannot_process",
      reason: "Still seeding. Weir waits for the torrent to finish.",
    });

    expect(verdictOf(file)).toBe("Still seeding");
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
