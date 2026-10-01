/**
 * The shelf's tiles are sized from the height of the row they stand in, exactly 2:3, and only whole tiles
 * are drawn: a row too narrow for one more simply shows one fewer.
 */

/**
 * The caption (the title and what happened) under a tile, measured on the rendered shelf: two 14.4px lines and the 6px
 * above them, 34.8px, taken up to a whole pixel.
 */
export const CAPTION_PX = 35;
/** The space between tiles. */
export const TILE_GAP_PX = 12;
/** The padding round the row, which the tiles do not use (the row's own padding: 7 above, 5 below and either side). */
const ROW_PAD_Y = 12;
const ROW_PAD_X = 10;
/** A row at least this tall has room for a caption under each tile. */
const CAPTION_MIN_ROW_PX = 130;
const MIN_ART_PX = 36;
/** The narrowest a tile gets, however many must stand in the row: 2:3 under the least art. */
const MIN_TILE_WIDTH_PX = Math.floor(MIN_ART_PX / 1.5);
/** No tile grows taller than this, however tall the row. */
const MAX_ART_PX = 255;

export type ShelfFit = {
  /** A caption (the title and what happened) goes under each tile. */
  captions: boolean;
  /** The width of a tile; its height is exactly one and a half times this. */
  width: number;
};

/** The fewest whole tiles the shelf shows when the page has its right column beside it, a window of at least this size. */
export const SHELF_MIN_TILES = 5;

/** The row's width, and how many whole tiles at the least it must hold: the tiles are no wider than that allows. */
export type ShelfRoom = { width: number; least: number };

/** The widest a tile may be for `room.least` of them to stand in the row. */
function widestForRoom({ width, least }: ShelfRoom): number {
  return Math.max(
    MIN_TILE_WIDTH_PX,
    Math.floor((width - ROW_PAD_X + TILE_GAP_PX) / least) - TILE_GAP_PX,
  );
}

/**
 * The tile size for a row of this height. The height decides it (the tiles fill the row, with a caption when it is
 * tall enough); where the row must hold `room.least` tiles, a tile is also no wider than that allows, and a tile
 * made shorter by it shows its caption when the row has the room. A tile is never stretched.
 */
export function shelfFit(rowHeight: number, room?: ShelfRoom): ShelfFit {
  const inner = rowHeight - ROW_PAD_Y;
  const tall = inner >= CAPTION_MIN_ROW_PX;
  const art = Math.max(
    MIN_ART_PX,
    Math.min(MAX_ART_PX, inner - (tall ? CAPTION_PX : 0)),
  );
  const byHeight = Math.floor(art / 1.5);
  const width = room ? Math.min(byHeight, widestForRoom(room)) : byHeight;
  const spare = inner - width * 1.5;
  return { captions: tall || spare >= CAPTION_PX, width };
}

/** Whole tiles across a row of `rowWidth` px, at least one; never a part of one. */
export function tilesAcross(rowWidth: number, tileWidth: number): number {
  return Math.max(
    1,
    Math.floor(
      (rowWidth - ROW_PAD_X + TILE_GAP_PX) / (tileWidth + TILE_GAP_PX),
    ),
  );
}
