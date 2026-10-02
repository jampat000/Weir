/**
 * What the full caption can say of a tile at its poster's width, decided by measuring the words rather than letting
 * them clip. The caption is as wide as the poster, so a line either fits it or gives something up: the outcome tries
 * its whole sentence on two lines and falls back to the short words, which end in an ellipsis where they need a third
 * line; the saving and when try to share a line and fall back to the saving alone, the time going to the tile's
 * tooltip and name, which already carry it. A tile that cannot say even its saving gets the compact caption instead.
 */
import type { ShelfTile } from "./shelf-model";

/** The width in px of a line of text in the caption's own font. */
export type MeasureText = (text: string) => number;

/** The caption's lines under the title, as they are drawn. */
export type FullCaptionLines = {
  /** What was done, which wraps over two lines. */
  outcome: string;
  /** The saving with when, the saving alone, or just when for a file that saved nothing. */
  last: string;
};

/** The room left by the type's own rounding: the last line must be this much narrower than the poster, or its end is cut. */
const LAST_LINE_MARGIN_PX = 1;
/** The outcome has this many lines. */
const OUTCOME_LINES = 2;

/** The canvas context everything is measured on, made once. */
let context: CanvasRenderingContext2D | null | undefined;

function measuringContext(): CanvasRenderingContext2D | null {
  if (context === undefined) {
    context =
      typeof document === "undefined"
        ? null
        : document.createElement("canvas").getContext("2d");
  }
  return context;
}

/** Measures text in `font` (a CSS font shorthand) on a canvas, or null where the browser has no canvas. */
export function canvasMeasure(font: string): MeasureText | null {
  const canvas = measuringContext();
  if (!canvas || !font) return null;
  return (text) => {
    canvas.font = font;
    return canvas.measureText(text).width;
  };
}

/** The font the caption's small lines are set in, read from a probe laid out as a full caption inside `host`. */
export function captionFont(host: HTMLElement): string {
  const caption = document.createElement("span");
  caption.className = "mm-shelf__caption mm-shelf__caption--full";
  caption.style.visibility = "hidden";
  const line = document.createElement("small");
  caption.append(line);
  host.append(caption);
  // The `font` shorthand reads back empty in some browsers, so the font is put together from its parts.
  const { fontStyle, fontWeight, fontSize, fontFamily } =
    getComputedStyle(line);
  caption.remove();
  return `${fontStyle} ${fontWeight} ${fontSize} ${fontFamily}`;
}

const fits = (text: string, width: number, measure: MeasureText, margin = 0) =>
  measure(text) <= width - margin;

/** Whether `text` wraps onto `OUTCOME_LINES` lines or fewer in `width` px, breaking at spaces. */
function wrapsWithin(text: string, width: number, measure: MeasureText) {
  let lines = 1;
  let line = "";
  for (const word of text.split(" ")) {
    if (!fits(word, width, measure)) return false;
    const next = line ? `${line} ${word}` : word;
    if (fits(next, width, measure)) {
      line = next;
    } else {
      lines += 1;
      line = word;
    }
  }
  return lines <= OUTCOME_LINES;
}

/**
 * The full caption's lines for a tile whose poster is `width` px wide, or null where not even its saving (or, for a
 * file that saved nothing, its time) fits under it, and the tile is better with the compact caption.
 */
export function fullCaptionLines(
  tile: Pick<ShelfTile, "outcome" | "what" | "saved" | "savedAgo">,
  width: number,
  measure: MeasureText,
): FullCaptionLines | null {
  const outcome = wrapsWithin(tile.outcome, width, measure)
    ? tile.outcome
    : tile.what;
  // A file that saved something gives up its time before its saving; one that saved nothing has only its time.
  const last = (
    tile.saved ? [tile.savedAgo, tile.saved] : [tile.savedAgo]
  ).find((words) => fits(words, width, measure, LAST_LINE_MARGIN_PX));
  return last === undefined ? null : { outcome, last };
}
