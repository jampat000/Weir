import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type KeyboardEvent,
  type PointerEvent,
} from "react";

/** A row is held by the pointer, which drops it where it is released, or by the keyboard, which holds it until dropped. */
type Grab = { id: number; via: "pointer" | "keyboard" };

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

function moved(order: readonly number[], id: number, index: number): number[] {
  const rest = order.filter((other) => other !== id);
  rest.splice(index, 0, id);
  return rest;
}

function sameOrder(a: readonly number[], b: readonly number[]): boolean {
  return a.length === b.length && a.every((id, index) => id === b[index]);
}

function directionOf(key: string): -1 | 0 | 1 {
  if (key === "ArrowUp") return -1;
  return key === "ArrowDown" ? 1 : 0;
}

/**
 * Reordering rows by dragging a handle, or from the keyboard, saved when the row is dropped.
 * Pointer: press the handle and move; the row takes the place the pointer is over, and Escape puts it back.
 * Keyboard: Alt with the up or down arrow moves the row and saves at once; Space or Enter picks it up, the
 * arrows move it, Space or Enter drops it and saves, and Escape puts it back. Each keyboard move is announced.
 */
export function useRowReorder({
  ids,
  nameOf,
  onCommit,
}: ReorderOptions): RowReorder {
  const [draft, setDraft] = useState<number[] | null>(null);
  const [grab, setGrab] = useState<Grab | null>(null);
  const [saving, setSaving] = useState(false);
  const [announcement, setAnnouncement] = useState("");

  const draftOrder = useRef<number[] | null>(null);
  const startOrder = useRef<readonly number[]>(ids);
  const rows = useRef(new Map<number, HTMLElement>());
  const handles = useRef(new Map<number, HTMLElement>());
  /** The handle to focus once a move has put its row in a new place, which takes focus from it. */
  const focusAfterMove = useRef<number | null>(null);

  const place = useCallback((next: number[] | null) => {
    draftOrder.current = next;
    setDraft(next);
  }, []);

  const announcePlace = (id: number, order: readonly number[], what: string) =>
    setAnnouncement(
      `${nameOf(id)} ${what} position ${order.indexOf(id) + 1} of ${order.length}.`,
    );

  const save = useCallback(
    (order: number[], refocus: number | null) => {
      setSaving(true);
      void onCommit(order).finally(() => {
        focusAfterMove.current = refocus;
        place(null);
        setSaving(false);
      });
    },
    [onCommit, place],
  );

  const pickUp = (id: number, via: Grab["via"]) => {
    startOrder.current = ids;
    place([...ids]);
    setGrab({ id, via });
  };

  const release = useCallback(
    (outcome: "drop" | "cancel") => {
      const final = draftOrder.current;
      const refocus = grab?.via === "keyboard" ? grab.id : null;
      setGrab(null);
      if (
        outcome === "cancel" ||
        !final ||
        sameOrder(final, startOrder.current)
      ) {
        place(null);
        return;
      }
      save(final, refocus);
    },
    [grab, place, save],
  );

  /** Where the held row belongs: after every other row whose middle is above the pointer. */
  const indexAt = useCallback((id: number, pointerY: number): number => {
    const order = draftOrder.current ?? [];
    return order.filter((other) => {
      const row = rows.current.get(other);
      if (other === id || !row) return false;
      const box = row.getBoundingClientRect();
      return box.top + box.height / 2 < pointerY;
    }).length;
  }, []);

  const heldByPointer = grab?.via === "pointer" ? grab.id : null;
  useEffect(() => {
    if (heldByPointer === null) return;
    const follow = (event: globalThis.PointerEvent) => {
      const current = draftOrder.current;
      if (!current) return;
      const index = indexAt(heldByPointer, event.clientY);
      if (current.indexOf(heldByPointer) !== index) {
        place(moved(current, heldByPointer, index));
      }
    };
    const drop = () => release("drop");
    const cancel = () => release("cancel");
    const cancelOnEscape = (event: globalThis.KeyboardEvent) => {
      if (event.key === "Escape") cancel();
    };
    window.addEventListener("pointermove", follow);
    window.addEventListener("pointerup", drop);
    window.addEventListener("pointercancel", cancel);
    window.addEventListener("keydown", cancelOnEscape);
    return () => {
      window.removeEventListener("pointermove", follow);
      window.removeEventListener("pointerup", drop);
      window.removeEventListener("pointercancel", cancel);
      window.removeEventListener("keydown", cancelOnEscape);
    };
  }, [heldByPointer, indexAt, release, place]);

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
    const held = grab?.id === id && grab.via === "keyboard";
    if (by !== 0 && event.altKey && !held) {
      event.preventDefault();
      const next = oneStep(id, by, ids);
      if (!next) return;
      place(next);
      announcePlace(id, next, "moved to");
      save(next, id);
    } else if (by !== 0 && held) {
      event.preventDefault();
      const next = oneStep(id, by, draftOrder.current ?? ids);
      if (!next) return;
      place(next);
      announcePlace(id, next, "moved to");
    } else if (event.key === " " || event.key === "Enter") {
      event.preventDefault();
      if (held) {
        announcePlace(id, draftOrder.current ?? ids, "dropped at");
        release("drop");
        return;
      }
      pickUp(id, "keyboard");
      announcePlace(id, ids, "picked up at");
    } else if (held && event.key === "Escape") {
      event.preventDefault();
      setAnnouncement(`${nameOf(id)} put back.`);
      release("cancel");
    }
  };

  return {
    orderedIds: draft ?? ids,
    grabbedId: grab?.id ?? null,
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
      "aria-pressed": grab?.id === id && grab.via === "keyboard",
      onPointerDown: (event) => {
        if (saving || event.button !== PRIMARY_BUTTON) return;
        event.preventDefault();
        event.currentTarget.focus();
        pickUp(id, "pointer");
      },
      onKeyDown: onKeyDown(id),
      onBlur: () => {
        // A move that puts the row elsewhere in the table takes focus from the handle for a moment; that is not
        // the person leaving it.
        if (grab?.via === "keyboard" && focusAfterMove.current === null) {
          release("cancel");
        }
      },
    }),
  };
}
