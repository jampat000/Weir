import { useEffect, useLayoutEffect, useState } from "react";

/** Room kept beside the chosen chip when the row scrolls to it. */
const EDGE = 8;

/** Scrolls the row just far enough for its chosen chip to show whole. */
function showChosenChip(row: HTMLElement) {
  const chip = row.querySelector<HTMLElement>('[aria-pressed="true"]');
  if (!chip) return;
  const right = chip.offsetLeft + chip.offsetWidth;
  if (chip.offsetLeft < row.scrollLeft + EDGE) {
    row.scrollLeft = chip.offsetLeft - EDGE;
  } else if (right > row.scrollLeft + row.clientWidth - EDGE) {
    row.scrollLeft = right - row.clientWidth + EDGE;
  }
}

/**
 * A row of chips that scrolls sideways when it is wider than the room it has. Says when it does, so the edges can
 * fade, and keeps the chosen chip in view as the choice or the room changes, such as from a link that points at the
 * last chip. The row may mount after the page does, when the header's slot it is drawn into appears, so it is held as
 * state.
 */
export function useChipRow(chosen: string) {
  const [row, setRow] = useState<HTMLDivElement | null>(null);
  const [scrolls, setScrolls] = useState(false);

  useLayoutEffect(() => {
    if (!row) return undefined;
    const measure = () => {
      setScrolls(row.scrollWidth > row.clientWidth + 1);
      showChosenChip(row);
    };
    measure();
    if (typeof ResizeObserver === "undefined") return undefined;
    const observer = new ResizeObserver(measure);
    observer.observe(row);
    if (row.firstElementChild) observer.observe(row.firstElementChild);
    return () => observer.disconnect();
  }, [row]);

  useEffect(() => {
    if (row) showChosenChip(row);
  }, [chosen, row]);

  return { setRow, scrolls };
}
