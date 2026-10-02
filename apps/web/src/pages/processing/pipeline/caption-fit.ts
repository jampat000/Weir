/**
 * What a caption line says at a poster's width, decided by measuring the words rather than letting them clip. The
 * caption is as wide as the poster, so a line says the fullest of its words that fit and the shortest where none
 * does: a cleaned file says "221 MB saved" where that fits and "221 MB" where it does not, and what was removed from
 * it says "−1 audio · −7 subs" and then "−8 tracks". The status line starts with a dot, which the words do not have
 * room for. A line none of whose words fit ends in an ellipsis, and the tile's name and tooltip say it whole.
 */
import type { LineWords } from "./shelf-model";

/** The width in px of a line of text in the caption's own font. */
export type MeasureText = (text: string) => number;

/** The status dot and the space after it, which the words do not have. */
export const STATUS_MARK_PX = 11;

/** The room left by the type's own rounding: a line must be this much narrower than the room, or its end is cut. */
const LINE_MARGIN_PX = 1;

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

/** The font the status line is set in, read from a probe laid out as the caption's status line inside `host`. */
export function statusFont(host: HTMLElement): string {
  const line = document.createElement("span");
  line.className = "mm-shelf__line mm-shelf__status";
  line.style.visibility = "hidden";
  const words = document.createElement("span");
  line.append(words);
  host.append(line);
  // The `font` shorthand reads back empty in some browsers, so the font is put together from its parts.
  const { fontStyle, fontWeight, fontSize, fontFamily } =
    getComputedStyle(words);
  line.remove();
  return `${fontStyle} ${fontWeight} ${fontSize} ${fontFamily}`;
}

/** The words for a line `room` px wide: the fullest that fit, else the shortest. */
export function lineWords(
  words: LineWords,
  room: number,
  measure: MeasureText,
): string {
  return (
    words.find((candidate) => measure(candidate) <= room - LINE_MARGIN_PX) ??
    words[words.length - 1]
  );
}
