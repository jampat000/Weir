import type { Schema } from "../api/types";

/** The name of the frame on the Activity stream. */
export const SYSTEM_TASKS_EVENT = "system.tasks";

/** One periodic task Weir runs on its own, with how its last run went and when the next one is due. */
export type SystemTask = Schema<"SystemTaskOut">;

const isText = (value: unknown): value is string => typeof value === "string";
const isTextOrNull = (value: unknown): value is string | null =>
  value === null || isText(value);

function isTask(value: unknown): value is SystemTask {
  if (typeof value !== "object" || value === null) return false;
  const task = value as Record<string, unknown>;
  return (
    isText(task.key) &&
    isText(task.label) &&
    typeof task.running === "boolean" &&
    isTextOrNull(task.last_run_at) &&
    (task.last_ok === null || typeof task.last_ok === "boolean") &&
    isTextOrNull(task.last_error) &&
    isTextOrNull(task.next_run_at) &&
    (task.interval_seconds === null ||
      typeof task.interval_seconds === "number")
  );
}

/** The tasks in a response or a stream frame, leaving out any entry that is not one; null when it is not a list. */
export function parseSystemTasks(raw: unknown): SystemTask[] | null {
  return Array.isArray(raw) ? raw.filter(isTask) : null;
}

/** The tasks in a stream message, or null when it is not one this screen understands. */
export function parseSystemTasksFrame(data: string): SystemTask[] | null {
  try {
    return parseSystemTasks(JSON.parse(data));
  } catch {
    return null;
  }
}
