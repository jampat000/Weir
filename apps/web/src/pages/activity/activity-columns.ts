import type { TableColumnsConfig } from "../../lib/ui/table-columns";

export type ActivityColumnId = "file" | "status" | "when";

/**
 * Activity's list of what Weir did to each file. The server sorts the downloads it returns, so a sort picks which
 * downloads fill the page; the library cleans listed with them are sorted in the browser the same way.
 */
export const ACTIVITY_COLUMNS: TableColumnsConfig<ActivityColumnId> = {
  tableId: "activity-files",
  sortable: true,
  defaultSort: { id: "when", direction: "desc" },
  columns: [
    { id: "file", label: "File" },
    { id: "status", label: "What happened" },
    { id: "when", label: "When", firstDirection: "desc" },
  ],
};

export type KeptColumnId = "file" | "workflow" | "size" | "actions";

/** The files kept without processing again: all of them are loaded, so the browser sorts them. */
export const KEPT_COLUMNS: TableColumnsConfig<KeptColumnId> = {
  tableId: "activity-kept",
  sortable: true,
  defaultSort: null,
  columns: [
    { id: "file", label: "File" },
    { id: "workflow", label: "Workflow" },
    { id: "size", label: "Size", firstDirection: "desc" },
    { id: "actions", label: "Actions", movable: false, sortable: false },
  ],
};
