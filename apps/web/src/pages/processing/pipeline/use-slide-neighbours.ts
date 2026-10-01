import { useLayoutEffect, useRef, type RefObject } from "react";

import { motionAllowed } from "../../../lib/ui/motion-allowed";
import { RESIZING_CLASS } from "./use-still-while-resizing";

/** Set by the shelf on each tile, to the tile's key: how a tile is told apart from the others. */
export const TILE_KEY_ATTRIBUTE = "data-shelf-key";

const SLIDE_MS = 300;
const SLIDE_EASING = "cubic-bezier(.2, .8, .2, 1)";
/** A tile that moved less than this has not moved. */
const MOVED_PX = 1;

/**
 * When a tile is added to the shelf or taken from it, the tiles that stay slide to their new places instead of
 * jumping: each is moved back to where it was and let go. Nothing slides while the window is being resized, which
 * moves every tile at once, or when movement is not allowed.
 */
export function useSlideNeighbours(
  row: RefObject<HTMLElement | null>,
  order: string,
  /** The tiles' width: when it changes, every tile moves at once and none of them slides. */
  tileWidth: number | null,
): void {
  const lefts = useRef(new Map<string, number>());
  const lastOrder = useRef(order);
  useLayoutEffect(() => {
    const host = row.current;
    if (!host) return;
    const rowLeft = host.getBoundingClientRect().left;
    const reordered = order !== lastOrder.current;
    const slide =
      reordered &&
      motionAllowed() &&
      !document.body.classList.contains(RESIZING_CLASS);
    const now = new Map<string, number>();
    for (const tile of host.querySelectorAll<HTMLElement>(
      `[${TILE_KEY_ATTRIBUTE}]`,
    )) {
      const key = tile.getAttribute(TILE_KEY_ATTRIBUTE) ?? "";
      const left = tile.getBoundingClientRect().left - rowLeft;
      const before = lefts.current.get(key);
      now.set(key, left);
      if (
        slide &&
        before !== undefined &&
        Math.abs(before - left) > MOVED_PX &&
        typeof tile.animate === "function"
      ) {
        tile.animate(
          [
            { transform: `translateX(${before - left}px)` },
            { transform: "none" },
          ],
          { duration: SLIDE_MS, easing: SLIDE_EASING },
        );
      }
    }
    lefts.current = now;
    lastOrder.current = order;
  }, [row, order, tileWidth]);
}
