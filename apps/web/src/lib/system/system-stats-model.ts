import type { TraceSample } from "../../components/charts/live-trace-math";
import { parseAppTime } from "../ui/mm-format-date";
import type {
  SystemPoint,
  SystemStats,
  SystemStatsFrame,
} from "./system-stats-types";

/**
 * The stats with a frame's newest reading and history point added, and the points older than the window dropped,
 * by their own times: a history that was 10 seconds apart while nobody watched is trimmed the same way as one at
 * one second. A frame whose point is not newer than the last one (a replay after a reconnect, or the same second
 * twice) leaves the history as it is and only refreshes the reading.
 */
export function withFrame(
  stats: SystemStats,
  frame: SystemStatsFrame,
): SystemStats {
  const newest = stats.history.at(-1);
  const at = parseAppTime(frame.point.at);
  const lastAt = newest ? parseAppTime(newest.at) : null;
  if (at === null || (lastAt !== null && at <= lastAt)) {
    return { ...stats, now: frame.now };
  }
  const from = at - stats.window_s * 1000;
  const history = [...stats.history, frame.point].filter(
    (point) => (parseAppTime(point.at) ?? at) >= from,
  );
  return { ...stats, now: frame.now, history };
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
