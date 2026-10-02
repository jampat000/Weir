import {
  useCallback,
  useId,
  useLayoutEffect,
  useRef,
  useState,
  type KeyboardEvent,
  type PointerEvent,
} from "react";

import {
  STORAGE_PREFIX,
  columnOf,
  defaultLayout,
  isMovable,
  isSortable,
  movableOrder,
  readLayout,
  sameLayout,
  sameSort,
  shiftedOrder,
  withMovableOrder,
  type SortDirection,
  type TableColumn,
  type TableColumnsConfig,
  type TableLayout,
  type TableSort,
} from "./table-columns";
import { useDragReorder } from "./use-drag-reorder";

/** How far the pointer must travel along a heading before pressing it drags the column rather than sorting by it. */
const COLUMN_DRAG_THRESHOLD_PX = 5;

const PRIMARY_BUTTON = 0;

function loadLayout<Id extends string>(
  config: TableColumnsConfig<Id>,
): TableLayout<Id> {
  try {
    const text = localStorage.getItem(STORAGE_PREFIX + config.tableId);
    return readLayout(config, text === null ? null : JSON.parse(text));
  } catch {
    return defaultLayout(config);
  }
}

/** A table whose layout is back to what it started as has nothing worth remembering. */
function saveLayout<Id extends string>(
  config: TableColumnsConfig<Id>,
  layout: TableLayout<Id>,
): void {
  const key = STORAGE_PREFIX + config.tableId;
  try {
    if (sameLayout(layout, defaultLayout(config))) localStorage.removeItem(key);
    else localStorage.setItem(key, JSON.stringify(layout));
  } catch {
    // A browser that will not remember the layout is no reason to refuse the change.
  }
}

/** Every cell of a column, its heading first, in the table that carries `data-table`. */
function cellsOf<Id extends string>(
  config: TableColumnsConfig<Id>,
  id: Id,
): HTMLElement[] {
  return Array.from(
    document.querySelectorAll<HTMLElement>(
      `[data-table="${config.tableId}"] [data-col="${id}"]`,
    ),
  );
}

/** Puts focus back on a column's heading after a move has redrawn it in another place. */
function focusHeading<Id extends string>(
  config: TableColumnsConfig<Id>,
  id: Id,
): void {
  const heading = cellsOf(config, id)[0];
  (heading?.querySelector<HTMLElement>("button") ?? heading)?.focus();
}

/** What one heading is given to draw and to react with. */
export type ColumnHeading<Id extends string> = {
  column: TableColumn<Id>;
  /** The way the table is sorted by this column, or null when it is sorted by another. */
  sorted: SortDirection | null;
  sortable: boolean;
  movable: boolean;
  /** The column is being dragged. */
  held: boolean;
  /** The id of the text that says how to move a column. */
  hintId: string;
  onSort: () => void;
  onPointerDown: (event: PointerEvent<HTMLElement>) => void;
  onKeyDown: (event: KeyboardEvent<HTMLElement>) => void;
};

export type TableColumns<Id extends string> = {
  /** Every column's id in the order to show, the dragged-to one while a column is held. */
  order: readonly Id[];
  sort: TableSort<Id> | null;
  /** Whether the headings sort the table. */
  sortable: boolean;
  /** False when the layout is the one the table started as. */
  changed: boolean;
  heading: (id: Id) => ColumnHeading<Id>;
  reset: () => void;
  /** Spread onto the element that holds the headings and the cells, whose cells carry `data-col`. */
  tableProps: { "data-table": string };
  hintId: string;
  /** What a screen reader is told about the last move. */
  announcement: string;
};

/**
 * The columns of one list table: their order, which of them it is sorted by and which way, all kept in this browser
 * for this table so they are as they were left. A heading is dragged to move its column, or moved from the keyboard
 * with Alt and an arrow; clicking it sorts, and clicking again reverses. `reset` puts the table back as it started.
 * The cells of a column carry `data-col` with its id, so a dragged column moves whole. `onSortChange` hears of a sort
 * the person chose or a reset changed, for a table whose rows are sorted by the server.
 */
export function useTableColumns<Id extends string>(
  config: TableColumnsConfig<Id>,
  {
    onSortChange,
  }: { onSortChange?: (sort: TableSort<Id> | null) => void } = {},
): TableColumns<Id> {
  const [layout, setLayout] = useState(() => loadLayout(config));
  const [announcement, setAnnouncement] = useState("");
  const hintId = useId();
  const focusAfterMove = useRef<Id | null>(null);

  const commit = useCallback(
    (next: TableLayout<Id>) => {
      setLayout(next);
      saveLayout(config, next);
    },
    [config],
  );

  const drag = useDragReorder<Id>({
    axis: "x",
    ids: movableOrder(config, layout.order),
    elementsOf: (id) => cellsOf(config, id),
    threshold: COLUMN_DRAG_THRESHOLD_PX,
    onDrop: (movable, clear) => {
      commit({ ...layout, order: withMovableOrder(config, movable) });
      clear();
    },
  });

  useLayoutEffect(() => {
    const id = focusAfterMove.current;
    if (id === null) return;
    focusAfterMove.current = null;
    focusHeading(config, id);
  });

  const order = withMovableOrder(config, drag.order);

  const moveBy = (id: Id, by: -1 | 1) => {
    const next = shiftedOrder(config, layout.order, id, by);
    if (!next) return;
    drag.slide();
    focusAfterMove.current = id;
    commit({ ...layout, order: next });
    setAnnouncement(
      `${columnOf(config, id).label} moved to position ${next.indexOf(id) + 1} of ${next.length}.`,
    );
  };

  const sortBy = (id: Id) => {
    if (!isSortable(config, id)) return;
    const direction: SortDirection =
      layout.sort?.id === id
        ? layout.sort.direction === "asc"
          ? "desc"
          : "asc"
        : (columnOf(config, id).firstDirection ?? "asc");
    const sort = { id, direction };
    commit({ ...layout, sort });
    onSortChange?.(sort);
  };

  const reset = () => {
    drag.slide();
    const start = defaultLayout(config);
    commit(start);
    if (!sameSort(layout.sort, start.sort)) onSortChange?.(start.sort);
    setAnnouncement("Columns put back as they were.");
  };

  const heading = (id: Id): ColumnHeading<Id> => {
    const column = columnOf(config, id);
    const movable = isMovable(column);
    return {
      column,
      sorted: layout.sort?.id === id ? layout.sort.direction : null,
      sortable: isSortable(config, id),
      movable,
      held: drag.heldId === id,
      hintId,
      onSort: () => sortBy(id),
      onPointerDown: (event) => {
        if (!movable || event.button !== PRIMARY_BUTTON) return;
        drag.press(id, event);
      },
      onKeyDown: (event) => {
        if (!movable || !event.altKey) return;
        const by =
          event.key === "ArrowLeft" ? -1 : event.key === "ArrowRight" ? 1 : 0;
        if (by === 0) return;
        event.preventDefault();
        moveBy(id, by);
      },
    };
  };

  return {
    order,
    sort: layout.sort,
    sortable: config.sortable,
    changed: !sameLayout(layout, defaultLayout(config)),
    heading,
    reset,
    tableProps: { "data-table": config.tableId },
    hintId,
    announcement,
  };
}
