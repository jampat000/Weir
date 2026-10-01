import { describe, expect, it } from "vitest";

import type { FinishedFile } from "../../../lib/activity/processing-outcome";
import { NOW } from "./pipeline-fixtures";
import { SHELF_LIMIT, shelfTiles } from "./shelf-model";
import { initialsOf, workflowHue } from "./title-tile";

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

const NAMES = new Map([[2, "TV"]]);

describe("the shelf's tiles", () => {
  it("say the title, what the file shrank by, what was removed and how long ago", () => {
    const [tile] = shelfTiles([finished(1)], "all", NOW, NAMES);

    expect(tile).toMatchObject({
      title: "The Quiet Harbour S01E01",
      saved: "−318 MB",
      what: "2 audio, 4 subtitles removed",
      ago: "3 min ago",
      workflow: "TV",
      fresh: false,
    });
  });

  it("wear the ring while the file has only just finished", () => {
    const [tile] = shelfTiles(
      [finished(1, { finishedAt: new Date(NOW - 10_000).toISOString() })],
      "all",
      NOW,
      NAMES,
    );

    expect(tile.fresh).toBe(true);
    expect(tile.ago).toBe("just now");
  });

  it("say what became of a file that nothing was removed from, and claim no saving that was not recorded", () => {
    const tiles = shelfTiles(
      [
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
      ],
      "all",
      NOW,
      NAMES,
    );

    expect(tiles.map((tile) => tile.what)).toEqual([
      "Cleaned",
      "Already right",
      "Cleaned in place",
    ]);
    expect(tiles.every((tile) => tile.saved === null)).toBe(true);
  });

  it("honour the page filter", () => {
    const items = [finished(1), finished(2, { source: "library" })];

    expect(
      shelfTiles(items, "download", NOW, NAMES).map((tile) => tile.key),
    ).toEqual(["1"]);
    expect(
      shelfTiles(items, "library", NOW, NAMES).map((tile) => tile.key),
    ).toEqual(["2"]);
    expect(shelfTiles(items, "all", NOW, NAMES)).toHaveLength(2);
  });

  it("are at most as many as the shelf holds, newest first", () => {
    const items = Array.from({ length: SHELF_LIMIT + 5 }, (_, i) =>
      finished(i + 1),
    );

    const tiles = shelfTiles(items, "all", NOW, NAMES);

    expect(tiles).toHaveLength(SHELF_LIMIT);
    expect(tiles[0].key).toBe("1");
  });
});

describe("the tile", () => {
  it("takes the first letters of the first two words of the title", () => {
    expect(initialsOf("The Quiet Harbour (2024)")).toBe("QH");
    expect(initialsOf("Lioness S03E08")).toBe("L");
    expect(initialsOf("A Paper Lantern")).toBe("PL");
  });

  it("is tinted for the workflow, the same every time and not the same for every workflow", () => {
    expect(workflowHue("Movies")).toBe(workflowHue("Movies"));
    expect(workflowHue("Movies")).not.toBe(workflowHue("TV"));
    expect(workflowHue("Movies")).toBeLessThan(360);
  });
});
