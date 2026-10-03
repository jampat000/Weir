import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { flushSync } from "react-dom";

import { isFitHeld, onFitRelease, useFitHeld } from "./fit-hold";

function overflows(row: HTMLElement): boolean {
  return row.scrollWidth > row.clientWidth + 1;
}

/**
 * How many steps a header has taken so that a row of chips shows whole: each step makes one control smaller, moves it
 * elsewhere or folds a chip away, in an order the caller chooses. Level 0 is everything in full. It climbs only while the
 * row is wider than the room it has, a level at a time, before the screen is painted, so it settles on the first level at
 * which the row fits, and no higher. When the room changes, or `refit` does, it starts again from 0, so room that comes
 * back gives the words back. While a popover is open it waits (see fit-hold): moving a control remounts it and would close
 * the popover, so a change that comes meanwhile is acted on once the last popover has closed.
 *
 * @param row The chips' own box, once it is on the page. The box it sits in must fill the room the controls have: that
 *   is the box whose size says when the room changes.
 * @param levels How many levels there are above 0.
 * @param refit Anything besides the room that changes how wide the row or the controls beside it want to be.
 * @param enabled False where the row has a line of its own and making the controls small would gain it nothing.
 * @param tooWide Says whether the row is too wide for its room; by default, whether its content overflows it.
 */
export function useFitLevels(
  row: HTMLElement | null,
  levels: number,
  refit: string,
  enabled = true,
  tooWide: (row: HTMLElement) => boolean = overflows,
): number {
  const [level, setLevel] = useState(0);
  // Counts the times the room changed, so a row that is still at level 0 is measured again.
  const [roomChanges, setRoomChanges] = useState(0);
  const held = useFitHeld();
  // What `refit` was when the layout was last free to change: while a popover is open it keeps that value.
  const [settledRefit, setSettledRefit] = useState(refit);
  if (!held && settledRefit !== refit) setSettledRefit(refit);
  const roomChangedWhileHeld = useRef(false);

  useLayoutEffect(() => {
    setLevel(0);
  }, [settledRefit, enabled]);

  useLayoutEffect(() => {
    if (!enabled || !row || held || level >= levels) return;
    if (tooWide(row)) setLevel(level + 1);
  }, [enabled, row, held, level, levels, settledRefit, roomChanges, tooWide]);

  useEffect(() => {
    const room = row?.parentElement;
    if (!room || typeof ResizeObserver === "undefined") return undefined;
    let width = room.clientWidth;
    const refitToRoom = () => {
      // Settled before the next paint, so a row that no longer fits is never drawn spilling over its neighbours.
      flushSync(() => {
        setLevel(0);
        setRoomChanges((count) => count + 1);
      });
    };
    const observer = new ResizeObserver(() => {
      if (room.clientWidth === width) return;
      width = room.clientWidth;
      if (isFitHeld()) roomChangedWhileHeld.current = true;
      else refitToRoom();
    });
    observer.observe(room);
    const stopWaiting = onFitRelease(() => {
      if (!roomChangedWhileHeld.current) return;
      roomChangedWhileHeld.current = false;
      refitToRoom();
    });
    return () => {
      observer.disconnect();
      stopWaiting();
    };
  }, [row]);

  return enabled ? level : 0;
}
