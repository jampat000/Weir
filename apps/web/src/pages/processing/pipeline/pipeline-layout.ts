/**
 * The layout rules of the Pipeline, as plain functions of measured space. Nothing on the board is locked
 * to a pixel size: it asks how tall its lanes are and sizes three rows of cards to fit, and asks how wide
 * it is to decide whether the stations sit side by side or stack. The components measure; these decide.
 */

/** The narrowest a station may be, in rem, before the stations stack one under another. */
export const MIN_STATION_REM = 8;

/** One row at the board's full size: a card is 86px tall and a row is 94px (8px between). */
export const ROW_PX = 94;
/** The gap between one row of cards and the next, in px. */
export const ROW_GAP_PX = 8;
/** The line "and N more" takes under the last row: a gap and one line of text, in px. */
export const MORE_PX = 22;
/** The smallest a card gets: below this the board gives way before the cards do. */
export const MIN_CARD_PX = 60;
/** The tallest a row step grows to on a tall window: a 122px card. */
export const MAX_STEP_PX = 130;
/** Under this card height the title is one line instead of two. */
export const TWO_LINE_TITLE_PX = 76;
/** The rows of cards the board always shows. */
export const PIPELINE_ROWS = 3;
/** A card's tile is this much shorter than the card, in px, which leaves the card's padding and border around it. */
export const TILE_INSET_PX = 17;

/** What a card is drawn at, for the room the lanes have. */
export type CardSize = {
  /** Height of one card, in px. */
  card: number;
  /** Distance from one row's top to the next, in px. */
  step: number;
  /** Height of the 2:3 tile, in px. */
  tile: number;
  /** Titles are clamped to one line. */
  oneLine: boolean;
};

const clamp = (value: number, min: number, max: number) =>
  Math.min(max, Math.max(min, value));

/**
 * The step is the lanes' height, less the 22px kept for "and N more", plus the 8px gap, over three rows,
 * between 68 and 130px; the card is the step less the gap; the 2:3 tile is 17px shorter than the card.
 * Without a measured height it is the full 94px step.
 */
export function cardSize(room: number | undefined): CardSize {
  const step =
    room === undefined
      ? ROW_PX
      : clamp(
          Math.floor((room - MORE_PX + ROW_GAP_PX) / PIPELINE_ROWS),
          MIN_CARD_PX + ROW_GAP_PX,
          MAX_STEP_PX,
        );
  const card = step - ROW_GAP_PX;
  return {
    card,
    step,
    tile: card - TILE_INSET_PX,
    oneLine: card < TWO_LINE_TITLE_PX,
  };
}

/** The height three rows of cards at this size, and the line "and N more" under them, take. */
export function lanesHeight(size: CardSize): number {
  return PIPELINE_ROWS * size.step - ROW_GAP_PX + MORE_PX;
}

/** Under the side-by-side threshold the page scrolls, and the board may be at most this share of a window tall. */
const STACKED_BOARD_SHARE = 0.7;

/** The tallest the whole board may be on a page that scrolls; nothing without a measured window. */
export function stackedBoardBudget(windowHeight: number): number | undefined {
  return windowHeight > 0
    ? Math.floor(windowHeight * STACKED_BOARD_SHARE)
    : undefined;
}

/** The height the cards may use: the board's budget less the header, stations and spacing around the lanes (chrome). */
export function cardsBudget(
  budget: number | undefined,
  chrome: number,
): number | undefined {
  return budget === undefined ? undefined : Math.max(0, budget - chrome);
}

/** The stations sit side by side when each has room, and stack when it does not (a phone, a narrow window). */
export function boardMode(
  width: number,
  stations: number,
  remPx: number,
): "columns" | "stacked" {
  if (!(width > 0)) return "columns";
  return width >= stations * MIN_STATION_REM * remPx ? "columns" : "stacked";
}

/** A detail line is this tall, in px, and the card keeps the space under its last one. */
const DETAIL_LINE_PX = 15;
const DETAIL_BOTTOM_PX = 8;

/** How many whole detail lines a card has room for, from where its card ends and where its detail block starts. */
export function detailRoom(cardBottom: number, blockTop: number): number {
  return Math.max(
    0,
    Math.floor((cardBottom - DETAIL_BOTTOM_PX - blockTop) / DETAIL_LINE_PX),
  );
}
