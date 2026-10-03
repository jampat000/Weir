import type {
  SystemLogQuery,
  SystemLogSort,
} from "../../../../lib/system/system-log-api";
import type {
  TableColumnsConfig,
  TableSort,
} from "../../../../lib/ui/table-columns";

export type LogColumnId =
  "time" | "level" | "source" | "category" | "workflow" | "title";

/**
 * The Log card's list. The server sorts it, so a sort covers the whole log and not just the rows on screen; what a row
 * says it was is not something to sort by. Until someone chooses, the newest come first. The arrow at a row's end
 * belongs to no column.
 */
export const LOG_COLUMNS: TableColumnsConfig<LogColumnId> = {
  tableId: "system-log",
  sortable: true,
  defaultSort: { id: "time", direction: "desc" },
  columns: [
    { id: "time", label: "Time", firstDirection: "desc" },
    { id: "level", label: "Level" },
    { id: "source", label: "Source" },
    { id: "category", label: "Category" },
    { id: "workflow", label: "Workflow" },
    { id: "title", label: "What happened", sortable: false },
  ],
};

/** Where each column sits in the row's grid, and how much room it takes. */
const TRACKS: Record<LogColumnId, string> = {
  time: "5.5rem",
  level: "3.5rem",
  source: "4rem",
  category: "6.5rem",
  workflow: "8rem",
  title: "minmax(0, 1fr)",
};

/** The same, once the card is narrow: the workflow and the title have left the first line. */
const NARROW_TRACKS: Partial<Record<LogColumnId, string>> = {
  time: "5.5rem",
  level: "3.5rem",
  source: "auto",
  category: "minmax(0, 1fr)",
};

const CHEVRON_TRACK = "0.875rem";

/** The grid of every row and of the headings over them, for the order the columns are in. */
export function logGrid(order: readonly LogColumnId[]): {
  "--log-cols": string;
  "--log-cols-narrow": string;
} {
  const narrow = order.flatMap((id) => NARROW_TRACKS[id] ?? []);
  return {
    "--log-cols": [...order.map((id) => TRACKS[id]), CHEVRON_TRACK].join(" "),
    "--log-cols-narrow": [...narrow, CHEVRON_TRACK].join(" "),
  };
}

/** What a sort adds to a request for the log: nothing for the order the server gives without being asked. */
export function logSortQuery(
  sort: TableSort<LogColumnId> | null,
): Pick<SystemLogQuery, "sort" | "direction"> {
  if (!sort || sort.id === "title") return {};
  const isDefault = sort.id === "time" && sort.direction === "desc";
  return isDefault
    ? {}
    : { sort: sort.id satisfies SystemLogSort, direction: sort.direction };
}
