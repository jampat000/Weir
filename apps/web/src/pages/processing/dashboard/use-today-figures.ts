import { useMemo } from "react";

import {
  LIBRARY_FILE_CLEANED_EVENT,
  REMUX_PASS_COMPLETED_EVENT,
} from "../../../lib/activity/event-types";
import {
  finishedFileFromEvent,
  type FinishedFile,
} from "../../../lib/activity/processing-outcome";
import { useActivityWindowQuery } from "../../../lib/activity/queries";
import { useProcessingOverviewStatsQuery } from "../../../lib/processing/queries";
import { HANDED_BACK_BUCKET_MS } from "../handed-back-model";
import { TODAY_DAYS } from "../processing-filter";

/** Five pages of 100 per kind, the most a day of one workflow's work has come to. */
const TODAY_PAGES = 5;
const DAY_MS = 24 * 60 * 60_000;

export type TodayFigures = {
  /** Files Weir cleaned. */
  cleaned: number;
  savedBytes: number;
};

/** What a day's finished files come to for one workflow: the ones Weir cleaned, and the space that saved. */
export function figuresOf(finished: readonly FinishedFile[]): TodayFigures {
  return {
    cleaned: finished.filter((file) => file.kind === "cleaned").length,
    savedBytes: finished.reduce((sum, file) => sum + (file.savedBytes ?? 0), 0),
  };
}

/**
 * Today's two figures. For every workflow they are the server's own totals; the totals cannot be split by
 * workflow, so for one workflow they are counted from that workflow's finished files over the same day. The
 * window starts on a five-minute boundary so the query is not refetched every second.
 */
export function useTodayFigures(
  workflowId: number | null,
  now: number,
): TodayFigures | undefined {
  const totals = useProcessingOverviewStatsQuery(TODAY_DAYS).data;
  const oneWorkflow = workflowId !== null;
  const since = new Date(
    Math.floor(now / HANDED_BACK_BUCKET_MS) * HANDED_BACK_BUCKET_MS - DAY_MS,
  )
    .toISOString()
    .replace("Z", "+00:00");
  const dayWindow = {
    date_from: since,
    library_id: workflowId ?? undefined,
  };
  const passes = useActivityWindowQuery(
    { event_type: REMUX_PASS_COMPLETED_EVENT, ...dayWindow },
    TODAY_PAGES,
    { enabled: oneWorkflow },
  ).data;
  const cleans = useActivityWindowQuery(
    { event_type: LIBRARY_FILE_CLEANED_EVENT, ...dayWindow },
    TODAY_PAGES,
    { enabled: oneWorkflow },
  ).data;

  return useMemo(() => {
    if (!oneWorkflow) {
      return totals
        ? {
            cleaned: totals.files_processed,
            savedBytes: totals.net_space_saved_bytes,
          }
        : undefined;
    }
    if (!passes || !cleans) return undefined;
    return figuresOf(
      [...passes.items, ...cleans.items]
        .map(finishedFileFromEvent)
        .filter((file): file is FinishedFile => file !== null),
    );
  }, [oneWorkflow, totals, passes, cleans]);
}
