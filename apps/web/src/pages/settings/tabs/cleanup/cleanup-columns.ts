import type { TableColumnsConfig } from "../../../../lib/ui/table-columns";

export type CleanupColumnId =
  "job" | "on" | "every" | "lastRun" | "nextRun" | "runNow";

/**
 * The cleanup jobs. The job's name is also the name of the settings listed under it, so it keeps the first place; the
 * button that runs a job keeps the last. All the jobs are loaded, so the browser sorts them, and a setting stays under
 * the job it belongs to wherever that job goes.
 */
export const CLEANUP_COLUMNS: TableColumnsConfig<CleanupColumnId> = {
  tableId: "cleanup-jobs",
  sortable: true,
  defaultSort: null,
  columns: [
    { id: "job", label: "Job", movable: false },
    { id: "on", label: "On", firstDirection: "desc" },
    { id: "every", label: "Every" },
    { id: "lastRun", label: "Last run", firstDirection: "desc" },
    { id: "nextRun", label: "Next run" },
    { id: "runNow", label: "Run now", movable: false, sortable: false },
  ],
};
