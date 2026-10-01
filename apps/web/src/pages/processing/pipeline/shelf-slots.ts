/**
 * The shelf's slot for a file whose poster is still on its way. While the file's card on the Pipeline says
 * "Delivered" the slot is closed: it takes no room, so the shelf does not change. When the poster takes off the
 * slot opens, over the length of the flight and by the flight's own curve, so the tiles beside it move aside in
 * step with the poster that is coming to fill it, and the poster lands as the slot finishes opening. The tile's art
 * keeps its full size throughout, so the poster in flight always has one place to fly to.
 */
import { useLayoutEffect, useState, type RefObject } from "react";

import { TILE_GAP_PX } from "./shelf-layout";

/** Set on a shelf tile's art while its card still says "Delivered": the tile is kept out of sight and its slot closed. */
export const HELD_ATTRIBUTE = "data-delivery-held";
/** Set on a shelf tile's art from take-off until the poster has landed and faded. */
export const FLYING_ATTRIBUTE = "data-delivery-flying";

/** Told to the document whenever a slot closes, opens or settles: how many slots are open may have changed. */
export const SLOTS_CHANGED_EVENT = "weir:shelf-slots-changed";

export function announceSlotsChanged(): void {
  document.dispatchEvent(new Event(SLOTS_CHANGED_EVENT));
}

/** The slot a tile's art stands in: its list item. */
function slotOf(art: HTMLElement): HTMLElement | null {
  return art.closest("li");
}

/**
 * Closes the slot: no width, and the gap that would follow it taken back, so the tiles beside it stay where they
 * are. What is in it is clipped; the art itself is not narrowed.
 */
export function closeSlot(art: HTMLElement): void {
  openSlot(art, 0);
}

/**
 * Opens the slot `share` (0 to 1) of the way. Each tile beside it is pushed along by the width the slot has taken,
 * its gap included, which is its full width plus the gap when `share` is 1.
 */
export function openSlot(art: HTMLElement, share: number): void {
  const slot = slotOf(art);
  if (!slot) return;
  slot.style.width = `${art.getBoundingClientRect().width * share}px`;
  slot.style.marginRight = `${-TILE_GAP_PX * (1 - share)}px`;
  slot.style.overflow = "hidden";
}

/** Gives the slot its own width back: the tile is on the shelf like any other. */
export function settleSlot(art: HTMLElement): void {
  const slot = slotOf(art);
  if (!slot) return;
  slot.style.removeProperty("width");
  slot.style.removeProperty("margin-right");
  slot.style.removeProperty("overflow");
}

/**
 * How many tiles of the row are held or on their way, so the row can keep one more tile in the DOM for each: as a
 * slot opens, the last tile on the shelf is pushed out gradually rather than being taken away before the poster
 * has even left.
 */
export function useOpenSlots(row: RefObject<HTMLElement | null>): number {
  const [open, setOpen] = useState(0);
  useLayoutEffect(() => {
    const count = () =>
      setOpen(
        row.current?.querySelectorAll(
          `[${HELD_ATTRIBUTE}], [${FLYING_ATTRIBUTE}]`,
        ).length ?? 0,
      );
    count();
    document.addEventListener(SLOTS_CHANGED_EVENT, count);
    return () => document.removeEventListener(SLOTS_CHANGED_EVENT, count);
  }, [row]);
  return open;
}
