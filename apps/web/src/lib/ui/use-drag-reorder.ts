import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type PointerEvent as ReactPointerEvent,
} from "react";

import { startOf, type DragAxis } from "./drag-geometry";
import { DragGesture, type DragHost, type DragOutcome } from "./drag-gesture";
import { SLIDE_MS, clearMotion, slideHome } from "./drag-motion";

type DragReorderOptions<Id> = {
  /** Down the page for rows, across it for columns. */
  axis: DragAxis;
  /** The items' ids in their saved order. */
  ids: readonly Id[];
  /** The elements that make up one item: a row, or every cell of a column. The first is the one measured. */
  elementsOf: (id: Id) => readonly HTMLElement[];
  /** Pixels the pointer travels before a press becomes a drag; zero picks up at once. */
  threshold: number;
  /** The held item was dropped in a new place and has settled. The owner saves the order and then calls `clear` to show the saved one. */
  onDrop: (order: Id[], clear: () => void) => void;
};

export type DragReorder<Id> = {
  /** The ids in the order to show: the saved one, or the one being dragged to. */
  order: readonly Id[];
  /** The item held by the pointer. */
  heldId: Id | null;
  /** Starts a gesture. Attach to the grip's pointer down. */
  press: (id: Id, event: ReactPointerEvent) => void;
  /** The next change to the list's order is shown with the items sliding from where they are now to their places. */
  slide: () => void;
  /** Shows another order, the items sliding to their places. */
  show: (next: Id[]) => void;
  /** Goes back to the saved order, with everything sliding home. */
  restore: () => void;
  /** Stops showing a dragged order and shows the saved one, where nothing needs to move. */
  clear: () => void;
  /** The order on screen now, for a keyboard move to start from. */
  current: () => readonly Id[];
};

/**
 * Reordering a list by holding one of its items, rows or columns alike: the held item follows the pointer, the
 * others slide out of its way, and a drop settles before it is reported (see {@link DragGesture}). The motion is the
 * same whichever way the list runs, and a move that is not made with the pointer can slide the same way through
 * `show` and `restore`.
 */
export function useDragReorder<Id>(
  options: DragReorderOptions<Id>,
): DragReorder<Id> {
  const [draft, setDraft] = useState<Id[] | null>(null);
  const [heldId, setHeldId] = useState<Id | null>(null);
  const latest = useRef(options);
  const shown = useRef<Id[] | null>(null);
  const gesture = useRef<DragGesture<Id> | null>(null);
  const before = useRef<Map<Id, number> | null>(null);
  const grip = useRef<Element | null>(null);

  useLayoutEffect(() => {
    latest.current = options;
  });

  const current = useCallback(
    (): readonly Id[] => shown.current ?? latest.current.ids,
    [],
  );

  /** Remembers where every item is seen now, so the next layout can slide each from there to its new place. */
  const slide = useCallback(() => {
    const { axis, elementsOf } = latest.current;
    const seen = new Map<Id, number>();
    for (const id of current()) {
      const element = elementsOf(id)[0];
      if (element) seen.set(id, startOf(element.getBoundingClientRect(), axis));
    }
    before.current = seen;
  }, [current]);

  const show = useCallback(
    (next: Id[]) => {
      slide();
      shown.current = next;
      setDraft(next);
    },
    [slide],
  );

  const clear = useCallback(() => {
    shown.current = null;
    setDraft(null);
  }, []);

  const restore = useCallback(() => {
    slide();
    clear();
  }, [slide, clear]);

  const ended = useCallback(
    (outcome: DragOutcome<Id>) => {
      gesture.current = null;
      setHeldId(null);
      // A grip pressed and dragged with the pointer is not left looking selected: only the keyboard keeps focus on it.
      if (
        outcome.kind !== "click" &&
        grip.current?.contains(document.activeElement)
      ) {
        (document.activeElement as HTMLElement).blur();
      }
      if (outcome.kind === "cancel") restore();
      else if (outcome.kind === "drop")
        latest.current.onDrop(outcome.order, clear);
      else clear();
    },
    [restore, clear],
  );

  useLayoutEffect(() => {
    const sightings = before.current;
    before.current = null;
    const { axis, elementsOf } = latest.current;
    if (sightings) {
      for (const [id, was] of sightings) {
        if (gesture.current && id === heldId) continue;
        const elements = elementsOf(id);
        clearMotion(elements);
        if (!elements[0]) continue;
        const now = startOf(elements[0].getBoundingClientRect(), axis);
        if (Math.abs(was - now) > 0.5)
          slideHome(elements, axis, was - now, SLIDE_MS);
      }
    }
    gesture.current?.afterLayout();
  });

  useEffect(() => () => gesture.current?.dispose(), []);

  const press = useCallback(
    (id: Id, event: ReactPointerEvent) => {
      const host: DragHost<Id> = {
        axis: latest.current.axis,
        threshold: latest.current.threshold,
        ids: () => latest.current.ids,
        order: current,
        elementsOf: (item) => latest.current.elementsOf(item),
        reorder: show,
        began: setHeldId,
        ended,
      };
      gesture.current?.dispose();
      grip.current = event.currentTarget;
      gesture.current = new DragGesture(host, id, event);
    },
    [current, show, ended],
  );

  return {
    order: draft ?? options.ids,
    heldId,
    press,
    slide,
    show,
    restore,
    clear,
    current,
  };
}
