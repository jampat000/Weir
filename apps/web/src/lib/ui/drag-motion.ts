import type { DragAxis } from "./drag-geometry";
import { motionAllowed } from "./motion-allowed";
import { windowIsResizing } from "./resizing-class";

/** How long the items that make way for a held one take to slide into their new places. */
export const SLIDE_MS = 180;
/** How long a dropped item takes to settle into its place before the new order is saved. */
export const SETTLE_MS = 150;

/** The attribute a held item carries, which the stylesheet raises; it is also how a test finds it. */
export const HELD_ATTRIBUTE = "data-dragging";

/** Whether things may move on their own now: not for someone who asked for less motion, nor while the window resizes. */
export function motionWanted(): boolean {
  return motionAllowed() && !windowIsResizing();
}

function translation(axis: DragAxis, pixels: number): string {
  return `translate${axis === "x" ? "X" : "Y"}(${pixels}px)`;
}

/** Puts the elements back where layout puts them, with nothing left running. */
export function clearMotion(elements: readonly HTMLElement[]): void {
  for (const element of elements) {
    element.style.transition = "";
    element.style.transform = "";
  }
}

/** Shows the elements `pixels` away from their place, straight away: the held item follows the pointer this way. */
export function placeAt(
  elements: readonly HTMLElement[],
  axis: DragAxis,
  pixels: number,
): void {
  for (const element of elements) {
    element.style.transition = "none";
    element.style.transform = translation(axis, pixels);
  }
}

/**
 * Moves the elements from `pixels` away back to their place over `ms`. Called after layout has already put them
 * there, so they appear to slide from where they were seen.
 */
export function slideHome(
  elements: readonly HTMLElement[],
  axis: DragAxis,
  pixels: number,
  ms: number,
): void {
  if (elements.length === 0 || !motionWanted()) {
    clearMotion(elements);
    return;
  }
  placeAt(elements, axis, pixels);
  // Reading a layout property commits the offset above, so the change below is a transition from it.
  void elements[0].offsetWidth;
  for (const element of elements) {
    element.style.transition = `transform ${ms}ms ease`;
    element.style.transform = "";
  }
}

export function markHeld(
  elements: readonly HTMLElement[],
  held: boolean,
): void {
  for (const element of elements) {
    if (held) element.setAttribute(HELD_ATTRIBUTE, "");
    else element.removeAttribute(HELD_ATTRIBUTE);
  }
}

/** Swallows the click a drag would otherwise end with, so letting go over a button does not press it. */
export function swallowNextClick(): void {
  const swallow = (event: Event) => {
    event.stopPropagation();
    event.preventDefault();
  };
  window.addEventListener("click", swallow, { capture: true, once: true });
  window.setTimeout(
    () => window.removeEventListener("click", swallow, { capture: true }),
    0,
  );
}
