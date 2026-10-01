/**
 * The shelf's tiles are sized from the height of the row they stand in, exactly 2:3, and only whole tiles
 * are drawn: a row too narrow for one more simply shows one fewer.
 */

/** The caption (the title and what happened) under a tile, and the space between tiles, in px. */
export const CAPTION_PX = 34;
export const TILE_GAP_PX = 12;
/** The padding round the row, which the tiles do not use: above and below, and either side. */
const ROW_PAD_Y = 12;
const ROW_PAD_X = 10;
/** A row at least this tall has room for a caption under each tile. */
const CAPTION_MIN_ROW_PX = 130;
const MIN_ART_PX = 36;
/** No tile grows taller than this, however tall the row. */
const MAX_ART_PX = 165;

export type ShelfFit = {
  /** A caption (the title and what happened) goes under each tile. */
  captions: boolean;
  /** The width of a tile; its height is exactly one and a half times this. */
  width: number;
};

/** The tile size for a row of this height. */
export function shelfFit(rowHeight: number): ShelfFit {
  const inner = rowHeight - ROW_PAD_Y;
  const captions = inner >= CAPTION_MIN_ROW_PX;
  const art = Math.max(
    MIN_ART_PX,
    Math.min(MAX_ART_PX, inner - (captions ? CAPTION_PX : 0)),
  );
  return { captions, width: Math.floor(art / 1.5) };
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
