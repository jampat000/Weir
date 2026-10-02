import type { ReactNode } from "react";

import { errorMessage } from "../../lib/api/error-message";
import type {
  LibraryModeSchedule,
  LibraryScanInfo,
} from "../../lib/processing/library-mode-api";
import { useTriggerLibraryScan } from "../../lib/processing/library-mode-queries";
import {
  useAppClockFormatter,
  useAppDateFormatter,
  useAppDayClockFormatter,
} from "../../lib/ui/mm-format-date";
import { nextScheduled, nextScheduledBrief, scanned } from "./library-model";

/**
 * What the page puts on the header's title line, before Pause: when the numbers were counted and when the schedule
 * runs next, in a few words with the whole sentence in the tooltip, then a way to count again.
 */
export function LibraryScanStatus({
  libraryId,
  scan,
  schedule,
  now,
  after,
}: {
  libraryId: number;
  scan: LibraryScanInfo | null;
  schedule: LibraryModeSchedule | undefined;
  now: number;
  /** Sits at the end of the row, after the way to check again. */
  after?: ReactNode;
}) {
  const formatDate = useAppDateFormatter();
  const clock = useAppClockFormatter();
  const dayClock = useAppDayClockFormatter();
  const rescan = useTriggerLibraryScan(libraryId);
  const running = Boolean(scan?.running);
  const when = running
    ? "Checking this workflow now"
    : scanned(scan?.generated_at ?? null, now);
  const scheduleLine = nextScheduled(schedule, now, formatDate);
  const scheduleBrief = nextScheduledBrief(schedule, now, clock, dayClock);
  const sentence = [when, scheduleLine].filter(Boolean).join(" · ");
  return (
    <>
      <span
        className="mm-library-scan"
        data-testid="library-scan"
        title={sentence}
      >
        {running ? (
          <i className="mm-live-pulse" aria-hidden="true" />
        ) : (
          <span className="mm-library-scan__dot" aria-hidden="true" />
        )}
        <span>{when}</span>
        {scheduleBrief ? (
          <span
            className="mm-library-scan__next"
            data-testid="library-schedule"
          >
            · {scheduleBrief}
          </span>
        ) : null}
        <span className="sr-only">
          {scheduleLine ? `, ${scheduleLine}` : ""}
        </span>
      </span>
      <button
        type="button"
        className="mm-head-control"
        disabled={rescan.isPending || running}
        onClick={() => rescan.mutate()}
      >
        {rescan.isPending ? "Starting a check…" : "Check again"}
      </button>
      {after}
      {rescan.isError ? (
        <span
          className="mm-library-scan__error"
          role="alert"
          title={errorMessage(
            rescan.error,
            "Weir couldn't start a check. Try again in a moment.",
          )}
          data-testid="library-scan-error"
        >
          {errorMessage(
            rescan.error,
            "Weir couldn't start a check. Try again in a moment.",
          )}
        </span>
      ) : null}
    </>
  );
}
