import { describe, expect, it } from "vitest";

import {
  MIN_STATION_REM,
  PIPELINE_ROWS,
  boardMode,
  cardSize,
  detailRoom,
  lanesHeight,
} from "./pipeline-layout";
import { shelfFit, tilesAcross } from "./shelf-layout";

describe("the card size", () => {
  it("is sized from the lanes' height: three rows always, 60px cards at the least, a 130px step at the most", () => {
    expect(cardSize(undefined)).toEqual({
      card: 86,
      step: 94,
      tile: 69,
      oneLine: false,
    });
    // (lanes 208 - 22 for the "and N more" line + 8) / 3 = 64.67, so the least: step 68, card 60, a one-line title
    expect(cardSize(208)).toEqual({
      card: 60,
      step: 68,
      tile: 43,
      oneLine: true,
    });
    // 292px of lanes: step floor((292 - 22 + 8) / 3) = 92, card 84
    expect(cardSize(292)).toEqual({
      card: 84,
      step: 92,
      tile: 67,
      oneLine: false,
    });
    expect(cardSize(256)).toEqual({
      card: 72,
      step: 80,
      tile: 55,
      oneLine: true,
    });
    // a tall window: the step stops at 130, a 122px card
    expect(cardSize(900)).toEqual({
      card: 122,
      step: 130,
      tile: 105,
      oneLine: false,
    });
    expect(cardSize(40)).toMatchObject({ card: 60, step: 68 });
  });

  it("makes the tile 2:3 at every size: its width is (card - 17) / 1.5", () => {
    for (const room of [208, 256, 292, 380, 900]) {
      const { card, tile } = cardSize(room);
      expect(tile).toBe(card - 17);
    }
  });

  it("holds exactly three rows and the line under them in the room it was sized for", () => {
    expect(PIPELINE_ROWS).toBe(3);
    expect(lanesHeight(cardSize(undefined))).toBe(296);
    expect(lanesHeight(cardSize(0))).toBe(218);
    expect(lanesHeight(cardSize(292))).toBeLessThanOrEqual(292);
  });
});

describe("the board's mode", () => {
  it("keeps the stations side by side while each has room, and stacks them when it does not", () => {
    expect(boardMode(5 * MIN_STATION_REM * 16, 5, 16)).toBe("columns");
    expect(boardMode(5 * MIN_STATION_REM * 16 - 1, 5, 16)).toBe("stacked");
    expect(boardMode(390, 5, 16)).toBe("stacked");
    // Nothing measured yet: the columns.
    expect(boardMode(0, 5, 16)).toBe("columns");
  });
});

describe("the detail lines a card has room for", () => {
  it("counts whole 15px lines under the card's block, keeping 8px under them", () => {
    expect(detailRoom(419, 400)).toBe(0);
    expect(detailRoom(500, 440)).toBe(3);
    expect(detailRoom(100, 120)).toBe(0);
  });
});

describe("the shelf's tiles", () => {
  it("are sized from the row's height, exactly 2:3, with a caption only when the row is tall enough", () => {
    // 190px of row less 12px of padding and a 34px caption = 144px of art, 96px wide.
    expect(shelfFit(190)).toEqual({ captions: true, width: 96 });
    expect(shelfFit(100)).toEqual({ captions: false, width: 58 });
    expect(shelfFit(10).width).toBe(24);
    expect(shelfFit(2000).width).toBe(110);
  });

  it("draws whole tiles only", () => {
    // 96px tiles with a 12px gap in a 450px row (440px inside its padding): 4 whole ones, never a part of one.
    expect(tilesAcross(450, 96)).toBe(4);
    expect(tilesAcross(440, 96)).toBe(4);
    expect(tilesAcross(429, 96)).toBe(3);
    expect(tilesAcross(30, 96)).toBe(1);
  });
});
