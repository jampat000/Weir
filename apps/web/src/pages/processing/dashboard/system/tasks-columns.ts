import {
  meaningRank,
  type TableColumnsConfig,
} from "../../../../lib/ui/table-columns";
import { TASK_MEANING, type TaskRow } from "./tasks-card-model";

export type TaskColumnId = "state" | "task" | "result" | "next";

/**
 * The Scheduled tasks card. Its dot keeps the first place and the other three can be moved. The tasks are all loaded,
 * so the browser sorts them; until someone chooses, they stay as the card lists them: running first, then the one due
 * soonest.
 */
export const TASK_COLUMNS: TableColumnsConfig<TaskColumnId> = {
  tableId: "dashboard-tasks",
  sortable: true,
  defaultSort: null,
  columns: [
    { id: "state", label: "Status", movable: false, sortable: false },
    { id: "task", label: "Task" },
    { id: "result", label: "Last result" },
    { id: "next", label: "Next" },
  ],
};

/**
 * What each column that sorts sorts by: the task's name, how its last run went in the order the meanings are listed
 * (tasks that went the same way keep the card's own order), and when it runs next. A task that is running, or has no
 * next time, is last.
 */
export const TASK_SORT_VALUES = {
  task: (row: TaskRow) => row.label,
  result: (row: TaskRow) => meaningRank(TASK_MEANING[row.state]),
  next: (row: TaskRow) => (row.state === "running" ? null : row.nextAt),
};

const TRACKS: Record<TaskColumnId, string> = {
  state: "10px",
  task: "minmax(0, 1fr)",
  result: "112px",
  next: "64px",
};

/** The grid's columns for the order, for the header and every row to share. */
export function taskGrid(order: readonly TaskColumnId[]): {
  "--task-cols": string;
} {
  return { "--task-cols": order.map((id) => TRACKS[id]).join(" ") };
}
