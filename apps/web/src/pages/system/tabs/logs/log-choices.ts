import type {
  SystemLogCategory,
  SystemLogPage,
} from "../../../../lib/system/system-log-api";
import { LOG_LEVEL_CHOICES, type LogFilters } from "./log-filters";

type Counts = SystemLogPage["counts"];

/** Whether choosing `category` would show anything; true while nothing has been counted yet. */
export function categoryHasRows(
  counts: Counts | undefined,
  category: SystemLogCategory,
): boolean {
  return counts === undefined || counts.category[category] > 0;
}

/** How many rows a level choice would show: its levels together. */
export function levelChoiceRows(
  counts: Counts,
  choice: (typeof LOG_LEVEL_CHOICES)[number],
): number {
  return choice.levels.reduce((sum, level) => sum + counts.level[level], 0);
}

/** Whether a workflow has any rows under the other filters; true while nothing has been counted yet. */
export function workflowHasRows(
  counts: Counts | undefined,
  workflow: number,
): boolean {
  return counts === undefined || (counts.workflow[String(workflow)] ?? 0) > 0;
}

/**
 * What a change of source leaves with nothing to show: the chosen categories and levels that have no rows under the new
 * source, and the workflow when the source is the server log, whose lines belong to none. Null when nothing is left
 * over, so a choice never narrows the list to nothing from a picker that no longer offers it.
 */
export function choicesLeftEmpty(
  filters: LogFilters,
  counts: Counts,
): Partial<LogFilters> | null {
  const left: Partial<LogFilters> = {};
  const categories = filters.categories.filter((category) =>
    categoryHasRows(counts, category),
  );
  if (categories.length < filters.categories.length) {
    left.categories = categories;
  }
  const emptyLevels = LOG_LEVEL_CHOICES.filter(
    (choice) => levelChoiceRows(counts, choice) === 0,
  ).flatMap((choice) => choice.levels);
  const levels = filters.levels.filter((level) => !emptyLevels.includes(level));
  if (levels.length < filters.levels.length) left.levels = levels;
  if (filters.workflow !== null && filters.source === "server") {
    left.workflow = null;
  }
  return Object.keys(left).length > 0 ? left : null;
}
