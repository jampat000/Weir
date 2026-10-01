import { describe, expect, it } from "vitest";

import {
  MIN_STATION_REM,
  PIPELINE_ROWS,
  boardMode,
  cardSize,
  cardsBudget,
  detailRoom,
  lanesHeight,
  stackedBoardBudget,
} from "./pipeline-layout";
import {
  SHELF_MIN_TILES,
  shelfFit,
  tilesAcross,
  type ShelfRoom,
} from "./shelf-layout";

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

describe("the board on a page that scrolls", () => {
  it("may be at most most of a window tall, and nothing without a measured window", () => {
    expect(stackedBoardBudget(1000)).toBe(700);
    expect(stackedBoardBudget(844)).toBe(590);
    expect(stackedBoardBudget(0)).toBeUndefined();
  });

  it("leaves the cards what the chrome around the lanes does not use", () => {
    expect(cardsBudget(500, 120)).toBe(380);
    expect(cardsBudget(undefined, 120)).toBeUndefined();
    expect(cardsBudget(100, 120)).toBe(0);
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
    // 190px of row less 12px of padding and a 35px caption = 143px of art, 95px wide.
    expect(shelfFit(190)).toEqual({ captions: true, width: 95 });
    expect(shelfFit(100)).toEqual({ captions: false, width: 58 });
    expect(shelfFit(10).width).toBe(24);
    expect(shelfFit(2000).width).toBe(170);
  });

  it("shows at least five whole tiles in a row wide enough, by making the tiles narrower, never wider than their height allows", () => {
    expect(SHELF_MIN_TILES).toBe(5);
    // A tall row of 570px: 250px of row would give 135px tiles, but five must stand across.
    const room: ShelfRoom = { width: 570, least: SHELF_MIN_TILES };
    const tall = shelfFit(250, room);
    expect(tall.width).toBe(102);
    expect(tilesAcross(570, tall.width)).toBeGreaterThanOrEqual(5);
    expect(shelfFit(250).width).toBe(135);
    // A short row already holds smaller tiles than five need: the height decides.
    expect(shelfFit(100, room)).toEqual(shelfFit(100));
    // The tile stays exactly 2:3 and is never stretched: its height is one and a half times its width.
    expect(tall.width * 1.5).toBeLessThan(250 - 12);
  });

  it("shows the captions of tiles the cap has made shorter than their row", () => {
    // 108px of room under the padding, 28px tiles (42px tall): 66px is spare, and a caption takes 35px.
    expect(shelfFit(120, { width: 200, least: 5 })).toEqual({
      captions: true,
      width: 28,
    });
    // 28px of spare is not enough for one.
    expect(shelfFit(112, { width: 300, least: 5 }).captions).toBe(false);
  });

  it("never makes a tile narrower than the least art, however narrow the row", () => {
    expect(shelfFit(250, { width: 100, least: 5 }).width).toBe(24);
  });

  it("draws whole tiles only", () => {
    // 96px tiles with a 12px gap in a 450px row (440px inside its padding): 4 whole ones, never a part of one.
    expect(tilesAcross(450, 96)).toBe(4);
    expect(tilesAcross(440, 96)).toBe(4);
    expect(tilesAcross(429, 96)).toBe(3);
    expect(tilesAcross(30, 96)).toBe(1);
  });
});
