import { moved } from "./drag-geometry";
import { STATUS_MEANINGS, type StatusMeaning } from "./status-meaning";

export type SortDirection = "asc" | "desc";

export type TableSort<Id extends string> = { id: Id; direction: SortDirection };

/** One column of a list table: what its heading says, and what may be done with it. */
export type TableColumn<Id extends string> = {
  id: Id;
  label: string;
  /** Whether the heading sorts the table. A table that cannot be sorted at all says so in its own settings. */
  sortable?: boolean;
  /** The way the first click sorts: oldest and smallest first unless the column says otherwise. */
  firstDirection?: SortDirection;
  /** Whether the column can be dragged to another place. A column that cannot be keeps its place. */
  movable?: boolean;
};

/** What a table's columns are, and what they start as. Defined once per table, outside any component. */
export type TableColumnsConfig<Id extends string> = {
  /** Names the table where its layout is remembered in this browser. */
  tableId: string;
  columns: readonly TableColumn<Id>[];
  /** What the table is sorted by when nobody has chosen, or null for the order the rows come in. */
  defaultSort: TableSort<Id> | null;
  /** False for a table whose row order is the point, which can still have its columns moved. */
  sortable: boolean;
};

/** What a person chose for one table, and what this browser remembers of it. */
export type TableLayout<Id extends string> = {
  order: Id[];
  sort: TableSort<Id> | null;
};

export const STORAGE_PREFIX = "weir-table:";

export function columnOf<Id extends string>(
  config: TableColumnsConfig<Id>,
  id: Id,
): TableColumn<Id> {
  const column = config.columns.find((candidate) => candidate.id === id);
  if (!column)
    throw new Error(`The table ${config.tableId} has no column ${id}.`);
  return column;
}

export function isMovable<Id extends string>(column: TableColumn<Id>): boolean {
  return column.movable !== false;
}

export function isSortable<Id extends string>(
  config: TableColumnsConfig<Id>,
  id: Id,
): boolean {
  return config.sortable && columnOf(config, id).sortable !== false;
}

export function defaultLayout<Id extends string>(
  config: TableColumnsConfig<Id>,
): TableLayout<Id> {
  return {
    order: config.columns.map((column) => column.id),
    sort: config.defaultSort,
  };
}

/** Puts the movable ids, in their order, into the places the movable columns hold among the fixed ones. */
function seatMovable<Id extends string>(
  columns: readonly TableColumn<Id>[],
  movable: readonly Id[],
): Id[] {
  const queue = [...movable];
  return columns.map((column) =>
    isMovable(column) ? (queue.shift() as Id) : column.id,
  );
}

/** The order of the movable columns only, which is what a drag works on. */
export function movableOrder<Id extends string>(
  config: TableColumnsConfig<Id>,
  order: readonly Id[],
): Id[] {
  const movable = new Set(
    config.columns.filter(isMovable).map((column) => column.id),
  );
  return order.filter((id) => movable.has(id));
}

/** The whole order, given the order of the movable columns. */
export function withMovableOrder<Id extends string>(
  config: TableColumnsConfig<Id>,
  movable: readonly Id[],
): Id[] {
  return seatMovable(config.columns, movable);
}

/** The movable columns' order after `id` moves `by` places, or null when it is already at that end. */
export function shiftedOrder<Id extends string>(
  config: TableColumnsConfig<Id>,
  order: readonly Id[],
  id: Id,
  by: -1 | 1,
): Id[] | null {
  const movable = movableOrder(config, order);
  const index = movable.indexOf(id) + by;
  if (index < 0 || index >= movable.length) return null;
  return withMovableOrder(config, moved(movable, id, index));
}

function isDirection(value: unknown): value is SortDirection {
  return value === "asc" || value === "desc";
}

/**
 * What a browser remembered, made safe: columns that no longer exist are dropped, new ones take their default place,
 * fixed columns stay where they are, and a sort on a column that cannot sort is forgotten. Anything unreadable is
 * ignored, so a layout from an older version never breaks the page.
 */
export function readLayout<Id extends string>(
  config: TableColumnsConfig<Id>,
  stored: unknown,
): TableLayout<Id> {
  const fallback = defaultLayout(config);
  if (typeof stored !== "object" || stored === null) return fallback;
  const { order, sort } = stored as { order?: unknown; sort?: unknown };
  const known = new Set<string>(config.columns.map((column) => column.id));
  const saved = Array.isArray(order)
    ? new Set(order.filter((id) => typeof id === "string" && known.has(id)))
    : new Set<unknown>();
  const movable = movableOrder(config, [
    ...(saved as Set<Id>),
    ...fallback.order.filter((id) => !saved.has(id)),
  ]);
  const chosen = sort as Partial<TableSort<Id>> | null | undefined;
  const validSort =
    chosen &&
    typeof chosen.id === "string" &&
    isDirection(chosen.direction) &&
    isSortable(config, chosen.id)
      ? { id: chosen.id, direction: chosen.direction }
      : fallback.sort;
  return { order: withMovableOrder(config, movable), sort: validSort };
}

export function sameSort<Id extends string>(
  a: TableSort<Id> | null,
  b: TableSort<Id> | null,
): boolean {
  return a?.id === b?.id && a?.direction === b?.direction;
}

export function sameLayout<Id extends string>(
  a: TableLayout<Id>,
  b: TableLayout<Id>,
): boolean {
  return (
    a.order.every((id, index) => id === b.order[index]) &&
    sameSort(a.sort, b.sort)
  );
}

/** What a column sorts by: words as words, everything else as a number. Nothing is a value that always sorts last. */
export type SortValue = string | number | null;

const WORDS = new Intl.Collator(undefined, {
  numeric: true,
  sensitivity: "base",
});

/** Where a status sorts: in the order its meanings are listed, so what is done and what is broken each sit together. */
export function meaningRank(meaning: StatusMeaning): number {
  return STATUS_MEANINGS.indexOf(meaning);
}

function compareValues(a: SortValue, b: SortValue): number {
  if (typeof a === "number" && typeof b === "number") return a - b;
  return WORDS.compare(String(a), String(b));
}

/**
 * The rows in the order a heading sorts them, without changing the ones given. Rows with no value come last whichever
 * way the sort runs, and rows with equal values keep the order they came in.
 */
export function sortRows<Row, Id extends string>(
  rows: readonly Row[],
  sort: TableSort<Id> | null,
  valueOf: Partial<Record<Id, (row: Row) => SortValue>>,
): Row[] {
  const read = sort ? valueOf[sort.id] : undefined;
  if (!sort || !read) return [...rows];
  const sign = sort.direction === "asc" ? 1 : -1;
  const keyed = rows.map((row, index) => ({ row, index, value: read(row) }));
  keyed.sort((a, b) => {
    if (a.value === null || b.value === null) {
      if (a.value === b.value) return a.index - b.index;
      return a.value === null ? 1 : -1;
    }
    return sign * compareValues(a.value, b.value) || a.index - b.index;
  });
  return keyed.map((entry) => entry.row);
}
