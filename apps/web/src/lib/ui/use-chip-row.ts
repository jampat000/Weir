import { useEffect, useLayoutEffect, useState } from "react";

/** Room kept beside the chosen chip when the row scrolls to it. */
const EDGE = 8;

/** Sub-pixel rounding, not a chip: how far a row may be from an end and still count as at it. */
const END_TOLERANCE_PX = 1;

/** Which sides of a scrolling row have chips past them. */
export type ChipRowMore = { before: boolean; after: boolean };

const NOTHING_MORE: ChipRowMore = { before: false, after: false };

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

function moreOf(row: HTMLElement): ChipRowMore {
  return {
    before: row.scrollLeft > END_TOLERANCE_PX,
    after:
      row.scrollLeft + row.clientWidth < row.scrollWidth - END_TOLERANCE_PX,
  };
}

function sameMore(a: ChipRowMore, b: ChipRowMore): boolean {
  return a.before === b.before && a.after === b.after;
}

/**
 * A mouse wheel turns up and down, which a row that only scrolls sideways would ignore: it scrolls the row instead,
 * until the row's end, where the page gets the wheel back.
 */
function turnWheelSideways(event: WheelEvent) {
  const row = event.currentTarget as HTMLElement;
  if (event.deltaX !== 0 || event.deltaY === 0) return;
  const furthest = Math.max(0, row.scrollWidth - row.clientWidth);
  const next = Math.min(furthest, Math.max(0, row.scrollLeft + event.deltaY));
  if (next === row.scrollLeft) return;
  row.scrollLeft = next;
  event.preventDefault();
}

/**
 * A row of chips that scrolls sideways when it is wider than the room it has. Says when it does, and which side has
 * more, so the edges can fade where chips are cut off, and keeps the chosen chip in view as the choice or the room
 * changes, such as from a link that points at the last chip. The row may mount after the page does, when the
 * header's slot it is drawn into appears, so it is held as state.
 */
export function useChipRow(chosen: string) {
  const [row, setRow] = useState<HTMLDivElement | null>(null);
  const [scrolls, setScrolls] = useState(false);
  const [more, setMore] = useState<ChipRowMore>(NOTHING_MORE);

  useLayoutEffect(() => {
    if (!row) return undefined;
    const readMore = () => {
      const next = moreOf(row);
      setMore((current) => (sameMore(current, next) ? current : next));
    };
    const measure = () => {
      setScrolls(row.scrollWidth > row.clientWidth + 1);
      showChosenChip(row);
      readMore();
    };
    measure();
    row.addEventListener("scroll", readMore, { passive: true });
    row.addEventListener("wheel", turnWheelSideways, { passive: false });
    const observer =
      typeof ResizeObserver === "undefined"
        ? null
        : new ResizeObserver(measure);
    observer?.observe(row);
    if (row.firstElementChild) observer?.observe(row.firstElementChild);
    return () => {
      row.removeEventListener("scroll", readMore);
      row.removeEventListener("wheel", turnWheelSideways);
      observer?.disconnect();
    };
  }, [row]);

  useEffect(() => {
    if (row) showChosenChip(row);
  }, [chosen, row]);

  return { setRow, scrolls, more };
}
