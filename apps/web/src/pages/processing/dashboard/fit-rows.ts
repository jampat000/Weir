/**
 * Shows only the rows that fit: a band tile lists what it has (what is running, what comes next) and the tile's
 * height decides how many whole rows it shows, so a short band shows fewer and a tall one more, and a row is never
 * cut through. Rows mark themselves with `data-fit`; the ones that do not fit are hidden (they keep their place, so
 * what fits is measured the same way every time) and the hook says how many do. Measured before the first paint,
 * whenever the tile changes size, and again when the fonts are in.
 */
import { useLayoutEffect, useRef, useState, type RefObject } from "react";

/**
 * Rows mark themselves with this attribute: `data-fit=""`. A row that is a heading for the rows after it says
 * `data-fit="with-next"`, and is left out when the row after it is, so a heading is never the last thing shown.
 */
const FIT_ATTRIBUTE = "data-fit";
const WITH_NEXT = "with-next";

/** How far a row may run past the tile before it no longer fits, in px: sub-pixel rounding, not a line. */
const FIT_TOLERANCE_PX = 0.5;

/**
 * How many of a host's rows fit inside its height, hiding the rest. Without `enabled` (a tile that grows with its
 * rows) every row fits.
 */
export function fitRows(host: HTMLElement, enabled = true): number {
  const rows = Array.from(
    host.querySelectorAll<HTMLElement>(`[${FIT_ATTRIBUTE}]`),
  );
  for (const row of rows) row.style.visibility = "";
  if (!enabled) return rows.length;
  const limit = host.getBoundingClientRect().bottom;
  let fits = 0;
  for (const row of rows) {
    // The space a row keeps under its own content (its padding) may run past the tile; its content may not.
    const padding = parseFloat(getComputedStyle(row).paddingBottom) || 0;
    if (row.getBoundingClientRect().bottom - padding > limit + FIT_TOLERANCE_PX)
      break;
    fits++;
  }
  while (fits > 0 && rows[fits - 1].getAttribute(FIT_ATTRIBUTE) === WITH_NEXT) {
    fits--;
  }
  rows.forEach((row, index) => {
    row.style.visibility = index < fits ? "" : "hidden";
  });
  return fits;
}

/** The host to put on the tile's body, and how many of its `data-fit` rows fit it (all of them until measured). */
export function useFittingRows(
  enabled = true,
): [RefObject<HTMLDivElement | null>, number] {
  const ref = useRef<HTMLDivElement | null>(null);
  const [fits, setFits] = useState(Number.MAX_SAFE_INTEGER);
  const measure = useRef<() => void>(() => undefined);
  // Measured after every change of what is listed...
  useLayoutEffect(() => {
    measure.current();
  });
  // ...and whenever the tile changes size or the fonts arrive.
  useLayoutEffect(() => {
    const host = ref.current;
    if (!host) return undefined;
    let live = true;
    measure.current = () => {
      if (!live) return;
      const next = fitRows(host, enabled);
      setFits((current) => (current === next ? current : next));
    };
    const again = () => measure.current();
    again();
    const observer =
      typeof ResizeObserver === "undefined" ? null : new ResizeObserver(again);
    observer?.observe(host);
    void document.fonts?.ready.then(again);
    document.fonts?.addEventListener?.("loadingdone", again);
    return () => {
      live = false;
      observer?.disconnect();
      document.fonts?.removeEventListener?.("loadingdone", again);
    };
  }, [enabled]);
  return [ref, fits];
}
