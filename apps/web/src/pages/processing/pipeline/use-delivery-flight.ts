import { useLayoutEffect, useRef, type RefObject } from "react";

import type { PipelineCard } from "./pipeline-card-types";
import { flyToShelf, holdShelfTiles, type Delivery } from "./delivery-flight";

/** Set on each card's tile, to the card's key: where a tile is found to read its place on screen. */
export const TILE_ATTRIBUTE = "data-pipeline-tile";

function tileRects(root: HTMLElement): Map<string, DOMRect> {
  const rects = new Map<string, DOMRect>();
  for (const tile of root.querySelectorAll<HTMLElement>(
    `[${TILE_ATTRIBUTE}]`,
  )) {
    rects.set(
      tile.getAttribute(TILE_ATTRIBUTE) ?? "",
      tile.getBoundingClientRect(),
    );
  }
  return rects;
}

/**
 * Hands each delivered card's tile to `onDelivered` the moment its "Delivered" hold ends, and keeps the
 * shelf's tile for the same file out of sight until then. `endedKeys` are the keys of every ended card
 * before the page's filter, so a card hidden by the filter is not mistaken for one whose hold ended.
 */
export function useDeliveryFlight(
  root: RefObject<HTMLElement | null>,
  cards: readonly PipelineCard[],
  endedKeys: ReadonlySet<string>,
  onDelivered: (delivery: Delivery) => void = flyToShelf,
): void {
  const shown = useRef(new Map<string, Delivery>());
  useLayoutEffect(() => {
    const host = root.current;
    if (!host) return undefined;
    const rects = tileRects(host);
    const delivered = cards.filter((card) => card.end === "delivered");
    const now = new Map<string, Delivery>();
    for (const card of delivered) {
      const from = rects.get(card.key);
      if (from) {
        now.set(card.key, {
          path: card.path,
          title: card.title,
          workflow: card.workflow,
          from,
        });
      }
    }
    for (const [key, delivery] of shown.current) {
      if (!now.has(key) && !endedKeys.has(key)) onDelivered(delivery);
    }
    shown.current = now;
    holdShelfTiles(new Set(delivered.map((card) => card.path)));
  });
  useLayoutEffect(() => () => holdShelfTiles(new Set()), []);
}
