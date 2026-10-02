/** The direction a list is reordered in: down the page for rows, across it for columns. */
export type DragAxis = "x" | "y";

/** How close to the edge of what is on screen the pointer must be before the page scrolls, in pixels. */
const EDGE_ZONE_PX = 56;
/** The most the page scrolls in one frame, at the very edge. */
const MAX_SCROLL_STEP_PX = 18;

export function clamp(value: number, low: number, high: number): number {
  return Math.min(Math.max(value, low), Math.max(low, high));
}

/** The order with `id` moved to `index`, counted among the others. */
export function moved<Id>(order: readonly Id[], id: Id, index: number): Id[] {
  const rest = order.filter((other) => other !== id);
  rest.splice(index, 0, id);
  return rest;
}

export function sameOrder<Id>(a: readonly Id[], b: readonly Id[]): boolean {
  return a.length === b.length && a.every((id, index) => id === b[index]);
}

/**
 * Where a held item belongs: after every other item whose centre it has passed. The centres are where the others
 * stood when the drag began, which do not move while the held item does, so the answer never flips back and forth.
 */
export function slotIndex(
  otherCentres: readonly number[],
  heldCentre: number,
): number {
  return otherCentres.filter((centre) => centre < heldCentre).length;
}

/**
 * How far to scroll this frame: negative towards the start, positive towards the end, zero away from the edges.
 * It grows the closer the pointer gets to the edge, and the edge of a view too short for two zones is its middle.
 */
export function edgeScrollStep(
  pointer: number,
  viewStart: number,
  viewEnd: number,
): number {
  const zone = Math.min(EDGE_ZONE_PX, (viewEnd - viewStart) / 2);
  if (pointer < viewStart + zone) {
    return (
      -MAX_SCROLL_STEP_PX * Math.min(1, (viewStart + zone - pointer) / zone)
    );
  }
  if (pointer > viewEnd - zone) {
    return (
      MAX_SCROLL_STEP_PX * Math.min(1, (pointer - (viewEnd - zone)) / zone)
    );
  }
  return 0;
}

/** The element the page itself scrolls. */
function pageScroller(): Element {
  return document.scrollingElement ?? document.documentElement;
}

/** The nearest ancestor that scrolls along the axis, or the page itself. */
export function scrollParentOf(element: Element, axis: DragAxis): Element {
  const overflow = axis === "y" ? "overflowY" : "overflowX";
  for (
    let parent = element.parentElement;
    parent;
    parent = parent.parentElement
  ) {
    const mode = getComputedStyle(parent)[overflow];
    const canScroll =
      axis === "y"
        ? parent.scrollHeight > parent.clientHeight
        : parent.scrollWidth > parent.clientWidth;
    if ((mode === "auto" || mode === "scroll") && canScroll) return parent;
  }
  return pageScroller();
}

export function startOf(rect: DOMRect, axis: DragAxis): number {
  return axis === "y" ? rect.top : rect.left;
}

export function sizeOf(rect: DOMRect, axis: DragAxis): number {
  return axis === "y" ? rect.height : rect.width;
}

export function scrollPosition(scroller: Element, axis: DragAxis): number {
  return axis === "y" ? scroller.scrollTop : scroller.scrollLeft;
}

/** The part of the scroller that is on screen, along the axis: the page's own window, or the scroller's box. */
export function visibleSpan(
  scroller: Element,
  axis: DragAxis,
): readonly [number, number] {
  if (scroller === pageScroller()) {
    return [0, axis === "y" ? window.innerHeight : window.innerWidth];
  }
  const box = scroller.getBoundingClientRect();
  return [startOf(box, axis), startOf(box, axis) + sizeOf(box, axis)];
}
