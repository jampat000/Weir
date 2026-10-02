/** What the Scheduled tasks card says: each task's state, how long ago it last ran, and a countdown to its next run. */
import type { SystemTask } from "../../../../lib/system/system-tasks-frame";
import { parseAppTime } from "../../../../lib/ui/mm-format-date";
import { plural } from "../../../../lib/ui/mm-plural";
import { checkedAgo } from "../health-model";

export type TaskState = "running" | "ok" | "failed" | "never";

export type TaskRow = {
  key: string;
  label: string;
  state: TaskState;
  /** When it last finished, in ms since the epoch. */
  lastAt: number | null;
  /** When it runs next, in ms since the epoch. */
  nextAt: number | null;
  /** Why the last run failed, in words; only for a failed task. */
  why: string | null;
};

const SECOND_MS = 1000;
const MINUTE_MS = 60 * SECOND_MS;
const HOUR_MS = 60 * MINUTE_MS;
const DAY_MS = 24 * HOUR_MS;
const SECONDS_PER_MINUTE = 60;
const MINUTES_PER_HOUR = 60;

/** What a failed task says when the server gave no reason. */
export const FAILED_WITHOUT_REASON =
  "It did not finish. The log has the details.";

function stateOf(task: SystemTask): TaskState {
  if (task.running) return "running";
  if (task.last_ok === false) return "failed";
  return task.last_ok === true ? "ok" : "never";
}

function rowOf(task: SystemTask): TaskRow {
  const state = stateOf(task);
  return {
    key: task.key,
    label: task.label,
    state,
    lastAt: parseAppTime(task.last_run_at),
    nextAt: parseAppTime(task.next_run_at),
    why:
      state === "failed"
        ? task.last_error?.trim() || FAILED_WITHOUT_REASON
        : null,
  };
}

/** Running tasks first, then failed ones, then the one due soonest; a task with no next time goes last, in the order it came. */
export function taskRows(tasks: readonly SystemTask[]): TaskRow[] {
  return tasks
    .map(rowOf)
    .sort(
      (a, b) =>
        Number(b.state === "running") - Number(a.state === "running") ||
        Number(b.state === "failed") - Number(a.state === "failed") ||
        (a.nextAt ?? Infinity) - (b.nextAt ?? Infinity),
    );
}

/** The few words beside the card's title: what is running and what failed, else how many tasks there are. */
export function tasksSummary(rows: readonly TaskRow[]): string {
  const running = rows.filter((row) => row.state === "running").length;
  const failed = rows.filter((row) => row.state === "failed").length;
  const parts = [
    running > 0 ? `${running.toLocaleString()} running` : null,
    failed > 0 ? `${failed.toLocaleString()} failed` : null,
  ].filter((part): part is string => part !== null);
  if (parts.length > 0) return parts.join(" · ");
  return rows.length === 0 ? "" : plural(rows.length, "task", "tasks");
}

/**
 * How long until a task runs: "0:42" under an hour, counted to the second; "5 h 10 min" or "5 h" under a day; then
 * days. A time that has passed reads "due".
 */
export function countdownWords(ms: number): string {
  if (ms <= 0) return "due";
  if (ms < HOUR_MS) {
    const seconds = Math.ceil(ms / SECOND_MS);
    const minutes = Math.floor(seconds / SECONDS_PER_MINUTE);
    return `${minutes}:${String(seconds % SECONDS_PER_MINUTE).padStart(2, "0")}`;
  }
  if (ms < DAY_MS) {
    const minutes = Math.floor(ms / MINUTE_MS);
    const hours = Math.floor(minutes / MINUTES_PER_HOUR);
    const rest = minutes % MINUTES_PER_HOUR;
    return rest === 0 ? `${hours} h` : `${hours} h ${rest} min`;
  }
  return `${Math.round(ms / DAY_MS)} d`;
}

/** The "Last result" cell: "running", or how long ago it ran (the mark is drawn beside it), or "—" for a task that never ran. */
export function lastResultWords(row: TaskRow, now: number): string {
  if (row.state === "running") return "running";
  if (row.state === "never" || row.lastAt === null) return "—";
  return checkedAgo(row.lastAt, now);
}

/** The "Next" cell: a countdown, or "—" while the task runs or has no next time. */
export function nextWords(row: TaskRow, now: number): string {
  if (row.state === "running" || row.nextAt === null) return "—";
  return countdownWords(row.nextAt - now);
}
