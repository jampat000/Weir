import { useMemo } from "react";

import {
  LIBRARY_FILE_CLEANED_EVENT,
  REMUX_PASS_COMPLETED_EVENT,
} from "../../../../lib/activity/event-types";
import {
  finishedFileFromEvent,
  type FinishedFile,
} from "../../../../lib/activity/processing-outcome";
import { activityKeys } from "../../../../lib/activity/query-keys";
import { useActivityWindowQuery } from "../../../../lib/activity/queries";
import { useActivityStreamInvalidations } from "../../../../lib/activity/use-activity-stream-invalidation";
import { figuresOf } from "../use-today-figures";
import { RECENT_MINUTES, type RecentWork } from "./processing-card-model";

const MINUTE_MS = 60_000;
/** One page of 100 is more than ten minutes of work comes to; the count is the server's own whatever the page holds. */
const ONE_PAGE = 1;
/** Finished work changes only when a file ends, so the figures follow the stream no faster than this. */
const REFRESH_THROTTLE_MS = 3_000;
const RECENT_KEYS = [activityKeys.recent] as const;

/** The start of the last ten minutes, on a minute boundary so the query is not asked again every second. */
export function recentSince(now: number): string {
  const minute = Math.floor(now / MINUTE_MS) * MINUTE_MS;
  return new Date(minute - RECENT_MINUTES * MINUTE_MS)
    .toISOString()
    .replace("Z", "+00:00");
}

/**
 * What the window came to: the files finished (the server's count of passes and of library cleans, whatever page
 * was fetched) and the space the fetched passes saved.
 */
export function recentWorkOf(
  passes: readonly FinishedFile[],
  passesTotal: number,
  cleansTotal: number,
): RecentWork {
  return {
    done: passesTotal + cleansTotal,
    savedBytes: figuresOf(passes).savedBytes,
  };
}

/** The files Weir finished in the last ten minutes and the space they saved, following the Activity stream. */
export function useRecentWork(now: number): RecentWork | undefined {
  useActivityStreamInvalidations(RECENT_KEYS, {
    throttleMs: REFRESH_THROTTLE_MS,
  });
  const date_from = recentSince(now);
  const passes = useActivityWindowQuery(
    { event_type: REMUX_PASS_COMPLETED_EVENT, date_from },
    ONE_PAGE,
  ).data;
  const cleans = useActivityWindowQuery(
    { event_type: LIBRARY_FILE_CLEANED_EVENT, date_from },
    ONE_PAGE,
  ).data;
  return useMemo(() => {
    if (!passes || !cleans) return undefined;
    const finished = passes.items
      .map(finishedFileFromEvent)
      .filter((file): file is FinishedFile => file !== null);
    // A pass that finished nothing (a skip) is in the server's count but not among the finished files fetched.
    const finishedNothing = passes.items.length - finished.length;
    return recentWorkOf(finished, passes.total - finishedNothing, cleans.total);
  }, [passes, cleans]);
}
