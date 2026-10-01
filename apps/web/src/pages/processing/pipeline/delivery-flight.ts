/**
 * The one movement on the Pipeline that is not a number changing: a file that has been delivered shows
 * it first (its card full and green for a moment), and then its tile flies from the card to the same
 * file on the Just finished shelf, where it lands in its place. The shelf's tile is kept out of sight
 * while the card still shows "Delivered", so the file is never in two places at once.
 *
 * With reduced motion, or a page nobody is looking at, nothing flies and the shelf tile simply shows.
 * The two components find each other in the document through the attributes below.
 */
import { motionAllowed } from "../../../lib/ui/motion-allowed";
import { initialsOf, workflowHue } from "./title-tile";

/** Set by the shelf on each tile, to the path of the file it shows. */
export const SHELF_TILE_ATTRIBUTE = "data-shelf-path";

const HELD_ATTRIBUTE = "data-delivery-held";
const FLYING_ATTRIBUTE = "data-delivery-flying";

const FLIGHT_MS = 1100;
const FLIGHT_EASING = "cubic-bezier(.45,.05,.25,1)";
const FLIGHT_Z_INDEX = "60";

/** A delivered file's tile as it was drawn on its card, and what it needs to be drawn again in flight. */
export type Delivery = {
  path: string;
  title: string;
  workflow: string;
  /** Where the tile was on screen. */
  from: DOMRect;
};

function shelfTiles(): HTMLElement[] {
  return Array.from(
    document.querySelectorAll<HTMLElement>(`[${SHELF_TILE_ATTRIBUTE}]`),
  );
}

function hold(tile: HTMLElement): void {
  tile.setAttribute(HELD_ATTRIBUTE, "");
  tile.style.visibility = "hidden";
}

function release(tile: HTMLElement): void {
  tile.removeAttribute(HELD_ATTRIBUTE);
  tile.removeAttribute(FLYING_ATTRIBUTE);
  tile.style.visibility = "";
}

/** Keeps the shelf's tiles for the files whose cards still say "Delivered" out of sight, and shows every other. */
export function holdShelfTiles(paths: ReadonlySet<string>): void {
  for (const tile of shelfTiles()) {
    if (tile.hasAttribute(FLYING_ATTRIBUTE)) continue;
    const wanted =
      motionAllowed() &&
      paths.has(tile.getAttribute(SHELF_TILE_ATTRIBUTE) ?? "");
    if (wanted && !tile.hasAttribute(HELD_ATTRIBUTE)) hold(tile);
    if (!wanted && tile.hasAttribute(HELD_ATTRIBUTE)) release(tile);
  }
}

function ghostOf(delivery: Delivery): HTMLElement {
  const ghost = document.createElement("span");
  ghost.className = "mm-tile mm-tile--flying";
  ghost.setAttribute("aria-hidden", "true");
  ghost.textContent = initialsOf(delivery.title);
  const { from } = delivery;
  Object.assign(ghost.style, {
    position: "fixed",
    left: `${from.left}px`,
    top: `${from.top}px`,
    width: `${from.width}px`,
    height: `${from.height}px`,
    zIndex: FLIGHT_Z_INDEX,
    pointerEvents: "none",
    transformOrigin: "top left",
  });
  ghost.style.setProperty("--tile-hue", String(workflowHue(delivery.workflow)));
  return ghost;
}

/**
 * Flies a copy of the delivered tile to the file's tile on the shelf. Without a shelf tile for the file, or
 * where movement is not allowed, there is nothing to fly to and the shelf's tile is simply shown.
 */
export function flyToShelf(delivery: Delivery): void {
  const target = shelfTiles().find(
    (tile) => tile.getAttribute(SHELF_TILE_ATTRIBUTE) === delivery.path,
  );
  if (!target) return;
  const to = target.getBoundingClientRect();
  const { from } = delivery;
  if (
    !motionAllowed() ||
    typeof target.animate !== "function" ||
    to.width === 0 ||
    to.height === 0 ||
    from.width === 0 ||
    from.height === 0
  ) {
    release(target);
    return;
  }
  const ghost = ghostOf(delivery);
  document.body.appendChild(ghost);
  hold(target);
  target.setAttribute(FLYING_ATTRIBUTE, "");
  const land = () => {
    ghost.remove();
    release(target);
  };
  const flight = ghost.animate(
    [
      { transform: "translate(0, 0) scale(1, 1)" },
      {
        transform: `translate(${to.left - from.left}px, ${to.top - from.top}px) scale(${to.width / from.width}, ${to.height / from.height})`,
      },
    ],
    { duration: FLIGHT_MS, easing: FLIGHT_EASING, fill: "forwards" },
  );
  flight.onfinish = land;
  flight.oncancel = land;
}
