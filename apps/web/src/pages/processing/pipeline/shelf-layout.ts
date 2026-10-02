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

/** The fewest whole tiles the shelf shows across, however wide the window: the tiles narrow to fit them. */
export const SHELF_MIN_TILES = 5;

/**
 * The tile size, from the row's own measured height, and no wider than lets `SHELF_MIN_TILES` whole tiles fit across
 * the row's measured width (when it is known). Tiles stay exactly 2:3: a narrower tile is also shorter, never
 * stretched, and none is narrower than the least art allows.
 */
export function shelfFit(rowHeight: number, rowWidth = 0): ShelfFit {
  const inner = rowHeight - ROW_PAD_Y;
  const captions = inner >= CAPTION_MIN_ROW_PX;
  const art = Math.max(
    MIN_ART_PX,
    Math.min(MAX_ART_PX, inner - (captions ? CAPTION_PX : 0)),
  );
  const byHeight = Math.floor(art / 1.5);
  if (!(rowWidth > 0)) return { captions, width: byHeight };
  const forLeast =
    Math.floor((rowWidth - ROW_PAD_X + TILE_GAP_PX) / SHELF_MIN_TILES) -
    TILE_GAP_PX;
  return {
    captions,
    width: Math.max(MIN_TILE_WIDTH_PX, Math.min(byHeight, forLeast)),
  };
}

/** Taller than any row the tiles can use: asking the fit for it gives the widest tile a row of that width allows. */
const UNLIMITED_ROW_PX = MAX_ART_PX + CAPTION_PX + ROW_PAD_Y;

/**
 * The height of a row of `rowWidth` px at which its tiles stop growing: the `SHELF_MIN_TILES` rule has capped them by
 * width, and a taller row would only leave empty space under them. It is the tile's art (1.5 times its width), the
 * caption when the row is tall enough to show one, and the row's padding. The page gives a row this tall at most
 * while something else can use the rest.
 */
export function shelfRowNeed(rowWidth: number): number {
  const { width } = shelfFit(UNLIMITED_ROW_PX, rowWidth);
  const art = Math.max(MIN_ART_PX, Math.ceil(width * 1.5));
  const captions = art + CAPTION_PX >= CAPTION_MIN_ROW_PX;
  return ROW_PAD_Y + art + (captions ? CAPTION_PX : 0);
}

/**
 * A poster narrower than this is too small for its decorations: the saved-space badge and the workflow's tag
 * would cover a third of the art or more (measured: the badge is 54px by 15px, the tag 19px tall and the poster's
 * width less 12px, so together they take 30% of a 64px poster and 36% of a 56px one).
 */
export const FULL_DECORATIONS_MIN_PX = 64;

export type PosterSize = "small" | "full";

/**
 * How a poster of this width is dressed. A "full" poster wears the saved badge and the workflow's whole tag; a
 * "small" one wears a slim strip in its workflow's colour, and what the badge and the tag say goes into the
 * tile's tooltip and name. Not measured yet, it is "full".
 */
export function posterSize(width: number | null): PosterSize {
  return width !== null && width < FULL_DECORATIONS_MIN_PX ? "small" : "full";
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
