import {
  useCallback,
  useLayoutEffect,
  useRef,
  useState,
  type KeyboardEvent,
  type PointerEvent,
} from "react";

import { moved, sameOrder } from "../../lib/ui/drag-geometry";
import { useDragReorder } from "../../lib/ui/use-drag-reorder";

type ReorderOptions = {
  /** The rows' ids in their saved order. */
  ids: readonly number[];
  /** What a row is called when its place is announced. */
  nameOf: (id: number) => string;
  /** Saves the new order. The rows stay where they were dropped until it settles, whether it saved or not. */
  onCommit: (ids: number[]) => Promise<unknown>;
};

/** What one row's drag handle needs to be given. */
export type ReorderHandleProps = {
  ref: (handle: HTMLElement | null) => void;
  "aria-pressed": boolean;
  onPointerDown: (event: PointerEvent<HTMLElement>) => void;
  onKeyDown: (event: KeyboardEvent<HTMLElement>) => void;
  onBlur: () => void;
};

export type RowReorder = {
  /** The rows' ids in the order to show: the saved one, or the one being dragged to. */
  orderedIds: readonly number[];
  /** The row being moved, for its highlight. */
  grabbedId: number | null;
  /** What a screen reader is told about the last keyboard move. */
  announcement: string;
  /** Attach to each row, so the pointer knows where the rows are. */
  rowRef: (id: number) => (row: HTMLElement | null) => void;
  handleProps: (id: number) => ReorderHandleProps;
};

const PRIMARY_BUTTON = 0;

function directionOf(key: string): -1 | 0 | 1 {
  if (key === "ArrowUp") return -1;
  return key === "ArrowDown" ? 1 : 0;
}

/**
 * Reordering rows by dragging a handle, or from the keyboard, saved when the row is dropped.
 * Pointer: press the handle and move; the row follows the pointer, the others slide out of its way, and it settles
 * into the place it is dropped in. Escape puts it back.
 * Keyboard: Alt with the up or down arrow moves the row and saves at once; Space or Enter picks it up, the
 * arrows move it, Space or Enter drops it and saves, and Escape puts it back. Each keyboard move is announced.
 */
export function useRowReorder({
  ids,
  nameOf,
  onCommit,
}: ReorderOptions): RowReorder {
  const [heldByKeyboard, setHeldByKeyboard] = useState<number | null>(null);
  const [saving, setSaving] = useState(false);
  const [announcement, setAnnouncement] = useState("");

  const startOrder = useRef<readonly number[]>(ids);
  const rows = useRef(new Map<number, HTMLElement>());
  const handles = useRef(new Map<number, HTMLElement>());
  /** The handle to focus once a move has put its row in a new place, which takes focus from it. */
  const focusAfterMove = useRef<number | null>(null);

  const save = useCallback(
    (order: number[], refocus: number | null, clear: () => void) => {
      setSaving(true);
      void onCommit(order).finally(() => {
        focusAfterMove.current = refocus;
        clear();
        setSaving(false);
      });
    },
    [onCommit],
  );

  const drag = useDragReorder<number>({
    axis: "y",
    ids,
    elementsOf: (id) => {
      const row = rows.current.get(id);
      return row ? [row] : [];
    },
    threshold: 0,
    onDrop: (order, clear) => save(order, null, clear),
  });

  const announcePlace = (id: number, order: readonly number[], what: string) =>
    setAnnouncement(
      `${nameOf(id)} ${what} position ${order.indexOf(id) + 1} of ${order.length}.`,
    );

  const releaseKeyboardHold = (outcome: "drop" | "cancel") => {
    const final = drag.current();
    const refocus = heldByKeyboard;
    setHeldByKeyboard(null);
    if (outcome === "cancel" || sameOrder(final, startOrder.current)) {
      drag.restore();
      return;
    }
    save([...final], refocus, drag.clear);
  };

  useLayoutEffect(() => {
    const id = focusAfterMove.current;
    if (id === null) return;
    focusAfterMove.current = null;
    const handle = handles.current.get(id);
    if (handle && document.activeElement !== handle) handle.focus();
  });

  /** The order with the row one place along, or null (and a word on why) when it is already at that end. */
  const oneStep = (id: number, by: -1 | 1, order: readonly number[]) => {
    const index = order.indexOf(id) + by;
    if (index >= 0 && index < order.length) {
      focusAfterMove.current = id;
      return moved(order, id, index);
    }
    announcePlace(
      id,
      order,
      `is already at the ${by < 0 ? "top," : "bottom,"}`,
    );
    return null;
  };

  const onKeyDown = (id: number) => (event: KeyboardEvent<HTMLElement>) => {
    if (saving) return;
    const by = directionOf(event.key);
    const held = heldByKeyboard === id;
    if (by !== 0 && event.altKey && !held) {
      event.preventDefault();
      const next = oneStep(id, by, ids);
      if (!next) return;
      drag.show(next);
      announcePlace(id, next, "moved to");
      save(next, id, drag.clear);
    } else if (by !== 0 && held) {
      event.preventDefault();
      const next = oneStep(id, by, drag.current());
      if (!next) return;
      drag.show(next);
      announcePlace(id, next, "moved to");
    } else if (event.key === " " || event.key === "Enter") {
      event.preventDefault();
      if (held) {
        announcePlace(id, drag.current(), "dropped at");
        releaseKeyboardHold("drop");
        return;
      }
      startOrder.current = ids;
      setHeldByKeyboard(id);
      announcePlace(id, ids, "picked up at");
    } else if (held && event.key === "Escape") {
      event.preventDefault();
      setAnnouncement(`${nameOf(id)} put back.`);
      releaseKeyboardHold("cancel");
    }
  };

  return {
    orderedIds: drag.order,
    grabbedId: drag.heldId ?? heldByKeyboard,
    announcement,
    rowRef: (id) => (row) => {
      if (row) rows.current.set(id, row);
      else rows.current.delete(id);
    },
    handleProps: (id) => ({
      ref: (handle) => {
        if (handle) handles.current.set(id, handle);
        else handles.current.delete(id);
      },
      "aria-pressed": heldByKeyboard === id,
      onPointerDown: (event) => {
        if (saving || event.button !== PRIMARY_BUTTON) return;
        event.preventDefault();
        drag.press(id, event);
      },
      onKeyDown: onKeyDown(id),
      onBlur: () => {
        // A move that puts the row elsewhere in the table takes focus from the handle for a moment; that is not
        // the person leaving it.
        if (heldByKeyboard === id && focusAfterMove.current === null) {
          releaseKeyboardHold("cancel");
        }
      },
    }),
  };
}
