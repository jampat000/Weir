import type { TableColumnsConfig } from "../../../../lib/ui/table-columns";

export type WorkflowColumnId =
  | "priority"
  | "workflow"
  | "kind"
  | "watches"
  | "cleans"
  | "rules"
  | "on"
  | "actions";

/**
 * The workflows table. Its rows are in priority order, which is the point of it, so no heading sorts it; the columns
 * can still be moved, and the one with the buttons stays last.
 */
export const WORKFLOW_COLUMNS: TableColumnsConfig<WorkflowColumnId> = {
  tableId: "setup-workflows",
  sortable: false,
  defaultSort: null,
  columns: [
    { id: "priority", label: "Priority" },
    { id: "workflow", label: "Workflow" },
    { id: "kind", label: "Kind" },
    { id: "watches", label: "Watches" },
    { id: "cleans", label: "Cleans into" },
    { id: "rules", label: "Rules" },
    { id: "on", label: "On" },
    { id: "actions", label: "Actions", movable: false },
  ],
};

/** The class that sets each column's width, on its heading and on every cell under it. */
export const WORKFLOW_COLUMN_CLASS: Record<WorkflowColumnId, string> = {
  priority: "mm-workflow-table__fit",
  workflow: "mm-workflow-table__workflow",
  kind: "mm-workflow-table__kind",
  watches: "mm-workflow-table__path",
  cleans: "mm-workflow-table__path",
  rules: "mm-workflow-table__rules",
  on: "mm-workflow-table__fit",
  actions: "mm-workflow-table__fit",
};
