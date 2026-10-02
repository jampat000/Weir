import { describe, expect, it } from "vitest";

import type { FinishedFile } from "../../../lib/activity/processing-outcome";
import { NOW } from "./pipeline-fixtures";
import {
  SHELF_LIMIT,
  shelfOf,
  todayWords,
  type ShelfScope,
} from "./shelf-model";

function finished(
  id: number,
  overrides: Partial<FinishedFile> = {},
): FinishedFile {
  return {
    id,
    source: "download",
    kind: "cleaned",
    relativePath: `The.Quiet.Harbour.S01E0${id}.1080p.WEB-DL.mkv`,
    libraryId: 2,
    savedBytes: 318 * 1024 ** 2,
    removedAudio: 2,
    removedSubtitles: 4,
    sentence: null,
    finishedAt: new Date(NOW - 3 * 60_000).toISOString(),
    ...overrides,
  };
}

const NAMES = new Map([
  [1, "Movies"],
  [2, "TV"],
]);
const EVERYTHING: ShelfScope = { filter: "all", workflowId: null, now: NOW };
const yesterday = () => new Date(NOW - 36 * 3_600_000).toISOString();

function shelf(items: FinishedFile[], scope: ShelfScope = EVERYTHING) {
  return shelfOf(items, scope, NAMES);
}

describe("the shelf's tiles", () => {
  it("say the title, what the file shrank by, what was removed and how long ago", () => {
    const [tile] = shelf([finished(1)]).tiles;

    expect(tile).toMatchObject({
      title: "The Quiet Harbour S01E01",
      saved: "−318 MB",
      what: "2 audio, 4 subtitles removed",
      ago: "3 min ago",
      workflow: "TV",
      workflowKnown: true,
      fresh: false,
    });
  });

  it("give the full caption the outcome in words and the saving with when", () => {
    const { tiles } = shelf([
      finished(1),
      finished(2, { savedBytes: null, removedAudio: 0, removedSubtitles: 0 }),
      finished(3, {
        kind: "already",
        savedBytes: null,
        removedAudio: 0,
        removedSubtitles: 0,
      }),
      finished(4, {
        kind: "rejected",
        savedBytes: null,
        sentence: "Rejected · no English audio",
      }),
    ]);

    expect(tiles.map((tile) => [tile.outcome, tile.savedAgo])).toEqual([
      ["2 audio, 4 subtitles removed", "−318 MB · 3 min ago"],
      ["Cleaned", "3 min ago"],
      ["Already right · nothing to change", "3 min ago"],
      ["Rejected · no English audio", "3 min ago"],
    ]);
  });

  it("wear the ring while the file has only just finished", () => {
    const [tile] = shelf([
      finished(1, { finishedAt: new Date(NOW - 10_000).toISOString() }),
    ]).tiles;

    expect(tile.fresh).toBe(true);
    expect(tile.ago).toBe("just now");
  });

  it("say what became of a file that nothing was removed from, and claim no saving that was not recorded", () => {
    const { tiles } = shelf([
      finished(1, { removedAudio: 0, removedSubtitles: 0, savedBytes: null }),
      finished(2, {
        kind: "already",
        savedBytes: null,
        removedAudio: 0,
        removedSubtitles: 0,
      }),
      finished(3, {
        source: "library",
        removedAudio: 0,
        removedSubtitles: 0,
        savedBytes: null,
      }),
    ]);

    expect(tiles.map((tile) => tile.what)).toEqual([
      "Cleaned",
      "Already right",
      "Cleaned in place",
    ]);
    expect(tiles.every((tile) => tile.saved === null)).toBe(true);
  });

  it("do not name a workflow that is not known", () => {
    const [tile] = shelf([finished(1, { libraryId: 99 })]).tiles;

    expect(tile.workflowKnown).toBe(false);
  });

  it("honour the page filter", () => {
    const items = [finished(1), finished(2, { source: "library" })];
    const keys = (filter: ShelfScope["filter"]) =>
      shelf(items, { ...EVERYTHING, filter }).tiles.map((tile) => tile.key);

    expect(keys("download")).toEqual(["1"]);
    expect(keys("library")).toEqual(["2"]);
    expect(keys("all")).toEqual(["1", "2"]);
  });

  it("honour the workflow the shelf is narrowed to", () => {
    const items = [
      finished(1, { libraryId: 2 }),
      finished(2, { libraryId: 1 }),
    ];

    const narrowed = shelf(items, { ...EVERYTHING, workflowId: 1 });

    expect(narrowed.tiles.map((tile) => tile.key)).toEqual(["2"]);
    expect(narrowed.today).toBe(1);
  });

  it("are at most as many as the shelf holds, newest first", () => {
    const items = Array.from({ length: SHELF_LIMIT + 5 }, (_, i) =>
      finished(i + 1),
    );

    const { tiles } = shelf(items);

    expect(tiles).toHaveLength(SHELF_LIMIT);
    expect(tiles[0].key).toBe("1");
  });
});

describe("what the shelf adds up to", () => {
  it("counts what finished today and what it saved", () => {
    const result = shelf([
      finished(1),
      finished(2, { savedBytes: 2 * 1024 ** 2 }),
      finished(3, { kind: "rejected", savedBytes: null }),
      finished(4, { kind: "failed", savedBytes: null }),
      finished(5, { finishedAt: yesterday() }),
    ]);

    expect(result).toMatchObject({
      today: 4,
      savedBytes: 320 * 1024 ** 2,
      latest: false,
    });
    expect(result.tiles.map((tile) => tile.key)).toEqual(["1", "2", "3", "4"]);
    expect(todayWords(result)).toBe("4 today · 320 MB saved");
  });

  it("shows the latest from before when nothing finished today", () => {
    const result = shelf([finished(5, { finishedAt: yesterday() })]);

    expect(result).toMatchObject({ today: 0, latest: true });
    expect(result.tiles.map((tile) => tile.key)).toEqual(["5"]);
    expect(todayWords(result)).toBe("Nothing yet today · latest arrivals");
  });

  it("is empty, and says nothing finished today, when there are no files at all", () => {
    expect(shelf([])).toMatchObject({ today: 0, latest: true, tiles: [] });
  });
});
