import { useLayoutEffect, useRef, useState, type RefObject } from "react";

import { columnsOf, rowsHeight, tilesBeyond, wholeRows } from "./whole-rows";

export type WholeRows = {
  /** The box whose height is the room for the tiles. */
  roomRef: RefObject<HTMLDivElement | null>;
  /** The grid of tiles, set to the height of the rows that fit. */
  gridRef: RefObject<HTMLDivElement | null>;
  /** The grid's height in px, or undefined until measured. */
  height: number | undefined;
  /** Tiles beyond the rows that show. */
  hidden: number;
};

type Measure = { height: number | undefined; hidden: number };

/** A length in px, or `fallback` where the style does not say one ("auto", "normal", or an environment that does not lay out). */
function pixels(length: string, fallback: number): number {
  const value = Number.parseFloat(length);
  return /px$/.test(length) && Number.isFinite(value) ? value : fallback;
}

/**
 * Sizes a grid of fixed-height tiles to the whole rows its room holds, measured before paint and whenever the room
 * changes size or the fonts arrive; `tiles` is how many it holds. The rows are as tall, and as far apart, as the
 * grid's own style says (`grid-auto-rows`, `row-gap`), so a card can set smaller tiles in a shorter band; `rowPx` and
 * `gapPx` stand where the style does not say.
 */
export function useWholeRows(
  tiles: number,
  rowPx: number,
  gapPx: number,
): WholeRows {
  const roomRef = useRef<HTMLDivElement | null>(null);
  const gridRef = useRef<HTMLDivElement | null>(null);
  const [measure, setMeasure] = useState<Measure>({
    height: undefined,
    hidden: 0,
  });
  useLayoutEffect(() => {
    const room = roomRef.current;
    const grid = gridRef.current;
    if (!room || !grid) return undefined;
    let live = true;
    const fit = () => {
      if (!live) return;
      const style = getComputedStyle(grid);
      const row = pixels(style.gridAutoRows, rowPx);
      const gap = pixels(style.rowGap, gapPx);
      const rows = wholeRows(room.clientHeight, row, gap);
      const columns = columnsOf(style.gridTemplateColumns);
      const next = {
        height: rowsHeight(rows, row, gap),
        hidden: tilesBeyond(tiles, rows, columns),
      };
      setMeasure((current) =>
        current.height === next.height && current.hidden === next.hidden
          ? current
          : next,
      );
    };
    fit();
    const observer =
      typeof ResizeObserver === "undefined" ? null : new ResizeObserver(fit);
    observer?.observe(room);
    void document.fonts?.ready.then(fit);
    return () => {
      live = false;
      observer?.disconnect();
    };
  }, [tiles, rowPx, gapPx]);
  return { roomRef, gridRef, ...measure };
}
