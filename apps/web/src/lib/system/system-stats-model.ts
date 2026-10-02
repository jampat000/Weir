import type { TraceSample } from "../../components/charts/live-trace-math";
import { parseAppTime } from "../ui/mm-format-date";
import type {
  SystemDrive,
  SystemPoint,
  SystemStats,
  SystemStatsFrame,
} from "./system-stats-types";

/** How many samples the history keeps: the window the server reports, at its sampling interval. */
export function historyLimit(
  stats: Pick<SystemStats, "window_s" | "interval_ms">,
): number {
  return Math.max(1, Math.ceil((stats.window_s * 1000) / stats.interval_ms));
}

/**
 * The stats with a frame's newest reading and history point added, and the oldest points dropped to the window.
 * A frame whose point is not newer than the last one (a replay after a reconnect, or the same second twice) leaves
 * the history as it is and only refreshes the reading.
 */
export function withFrame(
  stats: SystemStats,
  frame: SystemStatsFrame,
): SystemStats {
  const newest = stats.history.at(-1);
  const at = parseAppTime(frame.point.at);
  const lastAt = newest ? parseAppTime(newest.at) : null;
  const isNew = at !== null && (lastAt === null || at > lastAt);
  const history = isNew ? [...stats.history, frame.point] : stats.history;
  const limit = historyLimit(stats);
  return {
    ...stats,
    now: frame.now,
    history:
      history.length > limit ? history.slice(history.length - limit) : history,
  };
}

/**
 * One line of a trace from the history: the pick of each point as a sample at the point's own time. A point
 * the pick cannot read (null) or whose time is unreadable is left out, so a gap is a straight stretch of line,
 * never a drop to zero.
 */
export function seriesOf(
  history: readonly SystemPoint[],
  pick: (point: SystemPoint) => number | null,
): TraceSample[] {
  return history.flatMap((point) => {
    const value = pick(point);
    const at = parseAppTime(point.at);
    return value === null || at === null ? [] : [{ at, value }];
  });
}

/** The drives a workflow uses (as its watched, work or output folder), or every drive when none is picked. */
export function drivesUsedBy(
  drives: readonly SystemDrive[],
  workflowId: number | null,
): SystemDrive[] {
  return workflowId === null
    ? [...drives]
    : drives.filter((drive) =>
        drive.workflows.some((workflow) => workflow.id === workflowId),
      );
}
