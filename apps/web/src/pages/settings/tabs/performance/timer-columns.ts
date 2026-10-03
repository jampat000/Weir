import type { TableColumnsConfig } from "../../../../lib/ui/table-columns";

export type TimerColumnId = "job" | "when" | "lastRun" | "nextRun" | "change";

/** Weir's timers: a handful of jobs, all loaded, so the browser sorts them, in the order they are listed until told. */
export const TIMER_COLUMNS: TableColumnsConfig<TimerColumnId> = {
  tableId: "performance-timers",
  sortable: true,
  defaultSort: null,
  columns: [
    { id: "job", label: "Job" },
    { id: "when", label: "When" },
    { id: "lastRun", label: "Last run", firstDirection: "desc" },
    { id: "nextRun", label: "Next run" },
    {
      id: "change",
      label: "Where to change it",
      movable: false,
      sortable: false,
    },
  ],
};
