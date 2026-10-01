import {
  LIBRARY_FILE_CLEANED_EVENT,
  REMUX_PASS_COMPLETED_EVENT,
} from "../../lib/activity/event-types";
import {
  finishedFileFromEvent,
  type FinishedFile,
} from "../../lib/activity/processing-outcome";
import { useActivityWindowQuery } from "../../lib/activity/queries";
import {
  handedBack,
  handedBackSince,
  type HandedBack,
} from "./handed-back-model";
import type { Filter } from "./processing-filter";

/** Five pages of 100 per kind: far more than two hours of work on any install seen so far. */
const HANDED_BACK_PAGES = 5;

export type HandedBackSummary = {
  handed: HandedBack;
  /** The server's count, which can reach further back than the buckets. */
  total: number;
  /** More than a page of either kind, so the oldest files are counted but not drawn. */
  partial: boolean;
};

/**
 * The last two hours of finished files for the chosen source and workflow. Both kinds are always fetched, so
 * switching the filter only changes which ones are counted.
 */
export function useHandedBack(
  filter: Filter,
  now: number,
  workflowId: number | null = null,
): HandedBackSummary {
  const since = new Date(handedBackSince(now))
    .toISOString()
    .replace("Z", "+00:00");
  const workflow = workflowId === null ? {} : { library_id: workflowId };
  const passes = useActivityWindowQuery(
    { event_type: REMUX_PASS_COMPLETED_EVENT, date_from: since, ...workflow },
    HANDED_BACK_PAGES,
  );
  const cleans = useActivityWindowQuery(
    { event_type: LIBRARY_FILE_CLEANED_EVENT, date_from: since, ...workflow },
    HANDED_BACK_PAGES,
  );
  const responses = [
    ...(filter === "library" ? [] : [passes.data]),
    ...(filter === "download" ? [] : [cleans.data]),
  ];
  const handed = handedBack(
    responses
      .flatMap((response) => response?.items ?? [])
      .map(finishedFileFromEvent)
      .filter((item): item is FinishedFile => item !== null),
    now,
  );
  const partial = responses.some(
    (response) => response != null && !response.complete,
  );
  const total = partial
    ? responses.reduce((sum, response) => sum + (response?.total ?? 0), 0)
    : handed.totals.all;
  return { handed, total, partial };
}
