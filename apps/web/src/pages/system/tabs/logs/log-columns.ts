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

/** The time column's room: a time of day alone, and a time with the day in front of it where no heading names the day. */
const CLOCK_TRACK = "5.5rem";
const DATED_CLOCK_TRACK = "10.25rem";

/** Where each column sits in the row's grid, and how much room it takes. */
const TRACKS: Record<LogColumnId, string> = {
  time: CLOCK_TRACK,
  level: "3rem",
  source: "4rem",
  category: "6.5rem",
  workflow: "8rem",
  title: "minmax(0, 1fr)",
};

/** The same, once the card is narrow: the workflow and the title have left the first line. */
const NARROW_TRACKS: Partial<Record<LogColumnId, string>> = {
  time: CLOCK_TRACK,
  level: "3rem",
  source: "auto",
  category: "minmax(0, 1fr)",
};

const CHEVRON_TRACK = "0.875rem";

/**
 * The grid of every row and of the headings over them, for the order the columns are in. The time column is wider where
 * each time carries its day.
 */
export function logGrid(
  order: readonly LogColumnId[],
  dated = false,
): {
  "--log-cols": string;
  "--log-cols-narrow": string;
} {
  const time = dated ? DATED_CLOCK_TRACK : CLOCK_TRACK;
  const track = (
    id: LogColumnId,
    tracks: Partial<Record<LogColumnId, string>>,
  ) => (id === "time" ? time : tracks[id]);
  const narrow = order.flatMap((id) => track(id, NARROW_TRACKS) ?? []);
  return {
    "--log-cols": [...order.map((id) => track(id, TRACKS)), CHEVRON_TRACK].join(
      " ",
    ),
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
