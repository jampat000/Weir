import { useEffect, useLayoutEffect, useState } from "react";

/**
 * How many of a header's controls have given up their words for a mark so that a row of chips shows whole. Level 0 is
 * everything in full; each level above it is one more control made small, in an order the caller chooses. It climbs only
 * while the row is wider than the room it has, a level at a time, before the screen is painted, so it settles on the first
 * level at which the row fits, and no higher. When the room changes, or `refit` does, it starts again from 0, so room that
 * comes back gives the words back.
 *
 * @param row The chips' own scrolling box, once it is on the page.
 * @param levels How many levels there are above 0.
 * @param refit Anything besides the room that changes how wide the row or the controls beside it want to be.
 * @param enabled False where the row has a line of its own and making the controls small would gain it nothing.
 */
export function useFitLevels(
  row: HTMLElement | null,
  levels: number,
  refit: string,
  enabled = true,
): number {
  const [level, setLevel] = useState(0);
  // Counts the times the room changed, so a row that is still at level 0 is measured again.
  const [roomChanges, setRoomChanges] = useState(0);

  useLayoutEffect(() => {
    setLevel(0);
  }, [refit, enabled]);

  useLayoutEffect(() => {
    if (!enabled || !row || level >= levels) return;
    if (row.scrollWidth > row.clientWidth + 1) setLevel(level + 1);
  }, [enabled, row, level, levels, refit, roomChanges]);

  useEffect(() => {
    const header = row?.closest("header");
    if (!header || typeof ResizeObserver === "undefined") return undefined;
    let width = header.clientWidth;
    const observer = new ResizeObserver(() => {
      if (header.clientWidth === width) return;
      width = header.clientWidth;
      setLevel(0);
      setRoomChanges((count) => count + 1);
    });
    observer.observe(header);
    return () => observer.disconnect();
  }, [row]);

  return enabled ? level : 0;
}
